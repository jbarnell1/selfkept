import 'dart:async';

import 'package:flutter/foundation.dart';

import '../collectors/health_collector.dart';
import '../config/app_config.dart';
import '../models/metric.dart';
import '../models/source.dart';
import '../offline/outbox_repository.dart';
import '../offline/sync_engine.dart';
import '../utils/api_error.dart';
import '../utils/error_reporter.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'sources_provider.dart';

/// Where the on-device collector stands.
enum CollectorStatus {
  /// Not checked yet.
  unknown,

  /// No health store on this platform (web, desktop).
  unavailable,

  /// A store exists but this install has not been connected.
  disconnected,

  /// Permissions, source registration and the first collect are running.
  connecting,

  /// Connected and idle.
  connected,

  /// A collect or flush is running.
  collecting,

  /// The last connect or collect failed; [CollectorProvider.error] says why.
  error;

  String get wireValue => name;
  String toJson() => wireValue;

  static CollectorStatus fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : unknown;

  static CollectorStatus fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.name.toLowerCase() == lower) return s;
    }
    return unknown;
  }

  String get displayName => switch (this) {
    unknown => 'Checking',
    unavailable => 'Not available here',
    disconnected => 'Not connected',
    connecting => 'Connecting',
    connected => 'Connected',
    collecting => 'Syncing',
    error => 'Needs attention',
  };
}

/// Drives the platform collector: connect once, then collect on resume, on
/// store changes and on request. Collected rows go to the outbox and the
/// sync engine sends them; the collector cursor is kept locally and mirrored
/// to the server source by the engine.
class CollectorProvider extends ChangeNotifier with SafeNotifier {
  final HealthCollector _collector;
  final OutboxRepository _outbox;
  final SourcesProvider _sources;
  final SyncEngine _sync;
  final AuthProvider _auth;
  final String _platformName;
  final String _appVersion;
  final Duration _backfill;
  final DateTime Function() _clock;
  final ErrorReporter? _reporter;

  /// Guard against a platform that keeps answering hasMore.
  static const int maxPagesPerRun = 200;

  CollectorStatus _status = CollectorStatus.unknown;
  PermissionState _permission = PermissionState.notDetermined;
  String? _sourceId;
  String? _cursor;
  String? _progress;
  String? _error;
  DateTime? _lastCollectAt;
  int _lastCollected = 0;
  bool _backgroundLimited = false;
  bool _collectRequested = false;
  MetricPermissions? _metricPermissions;
  Set<MetricKey> _notNow = const {};
  bool _askingForMore = false;
  bool _askedAndStillMissing = false;
  Future<void>? _inFlight;
  StreamSubscription<void>? _changes;

  CollectorProvider({
    required this._collector,
    required this._outbox,
    required this._sources,
    required this._sync,
    required this._auth,
    String? platformName,
    this._appVersion = AppConfig.appVersion,
    this._backfill = const Duration(days: 90),
    DateTime Function()? clock,
    this._reporter,
  }) : _platformName = platformName ?? _defaultPlatformName(),
       _clock = clock ?? (() => DateTime.now().toUtc()) {
    _auth.addListener(_onAuthChanged);
  }

  static String _defaultPlatformName() {
    if (kIsWeb) return 'web';
    return defaultTargetPlatform.name.toLowerCase();
  }

  CollectorStatus get status => _status;
  PermissionState get permission => _permission;
  SourceKind get kind => _collector.kind;
  bool get isAvailable =>
      _status != CollectorStatus.unavailable &&
      _status != CollectorStatus.unknown;
  bool get isConnected => _sourceId != null && isAvailable;
  bool get isBusy =>
      _status == CollectorStatus.connecting ||
      _status == CollectorStatus.collecting;

  /// Server id of this install's source, once connected.
  String? get sourceId => _sourceId;
  String? get cursor => _cursor;

  /// One calm line about what is happening, for the Sources screen.
  String? get progress => _progress;
  String? get error => _error;
  DateTime? get lastCollectAt => _lastCollectAt;

  /// Rows the last run read from the store.
  int get lastCollected => _lastCollected;

  /// True when the phone may hold sends while the app is closed.
  bool get backgroundLimited => _backgroundLimited;

  /// Collectable metrics this phone could read but has not been allowed to.
  /// Empty when the platform cannot say (HealthKit, web) or before the first
  /// check.
  Set<MetricKey> get missingMetrics {
    final missing = _metricPermissions?.missing;
    if (missing == null) return const {};
    return missing.intersection(MetricKey.collectable);
  }

  /// True when this phone is connected and some kinds are not allowed yet.
  /// The Sources screen offers the ask whenever this holds.
  bool get hasMissingMetrics => isConnected && missingMetrics.isNotEmpty;

  /// True when Today should offer the new kinds: the phone already collects
  /// (something is granted), something collectable is not, and the person
  /// has not said "Not now" to every kind in that set. A phone that has
  /// granted nothing is left to the first-time connect.
  bool get offerMissingMetrics {
    if (!hasMissingMetrics || _askingForMore) return false;
    if (_metricPermissions?.granted.isEmpty ?? true) return false;
    return !_notNow.containsAll(missingMetrics);
  }

  /// True while the system permission sheet is up.
  bool get isAskingForMore => _askingForMore;

  /// True when the last ask came back with kinds still missing (the person
  /// declined, or Android no longer shows the sheet).
  bool get askedAndStillMissing => _askedAndStillMissing && hasMissingMetrics;

  /// Checks the platform and restores a saved connection. Call once after
  /// sign-in; safe to call again. [collect] false leaves the first pass to
  /// the caller (the background upload awaits it).
  Future<void> init({bool collect = true}) async {
    final available = await _collector.isAvailable();
    if (!available) {
      _status = CollectorStatus.unavailable;
      notify();
      return;
    }
    await _restore();
    _notNow = _decodeKeys(await _outbox.readState(notNowKey));
    await _readMetricPermissions();
    _backgroundLimited = await _collector.backgroundLimited();
    _changes ??= _collector.changes.listen((_) => collectAndFlush());
    _status = _sourceId == null
        ? CollectorStatus.disconnected
        : CollectorStatus.connected;
    notify();
    if (collect && _sourceId != null) unawaited(collectAndFlush());
  }

  /// Asks for permissions, registers the source and runs the first collect
  /// with the 90-day backfill. True when connected.
  Future<bool> connect() async {
    if (_status == CollectorStatus.unavailable) return false;
    final userId = _auth.user?.id;
    if (userId == null) return false;
    _status = CollectorStatus.connecting;
    _error = null;
    _progress = 'Asking for permission';
    notify();
    try {
      _permission = await _collector.requestPermissions(MetricKey.collectable);
      if (!_permission.allowsReading) {
        _fail(
          _permission == PermissionState.unavailable
              ? 'The health store is not available on this phone.'
              : 'Permission was not given. You can allow it in the phone settings and try again.',
          CollectorStatus.disconnected,
        );
        return false;
      }
      _progress = 'Registering this phone';
      notify();
      final deviceName = await _collector.deviceName();
      final source = await _sources.register(
        kind: _collector.kind,
        deviceName: deviceName,
        platform: _platformName,
        appVersion: _appVersion,
      );
      if (source == null) {
        _fail(_sources.error ?? 'The server did not accept this source.');
        return false;
      }
      _sourceId = source.id;
      // A reinstall resumes from the cursor the server kept.
      _cursor = source.cursor;
      await _outbox.writeState(_sourceKey(userId), source.id);
      await _outbox.writeState(_cursorKey(userId), _cursor);
      _status = CollectorStatus.connected;
      _progress = null;
      await _readMetricPermissions();
      notify();
      await collectAndFlush();
      return _error == null;
    } catch (e, stack) {
      _reporter?.report(e, stack, kind: 'collector');
      _fail(describeError(e));
      return false;
    }
  }

  /// Forgets the local connection. The server source is left alone (the
  /// Sources list can disable or delete it).
  Future<void> disconnect() async {
    final userId = _auth.user?.id;
    if (userId != null) {
      await _outbox.writeState(_sourceKey(userId), null);
      await _outbox.writeState(_cursorKey(userId), null);
    }
    _sourceId = null;
    _cursor = null;
    _progress = null;
    _error = null;
    if (_status != CollectorStatus.unavailable) {
      _status = CollectorStatus.disconnected;
    }
    notify();
  }

  /// Called by the app when it returns to the foreground.
  void onResume() {
    unawaited(_checkBackground());
    unawaited(refreshMetricPermissions());
    if (_sourceId != null) unawaited(collectAndFlush());
  }

  /// Reads per-metric access again (the person may have changed it in the
  /// Health Connect settings) and tells listeners when the answer moved.
  Future<void> refreshMetricPermissions() async {
    if (!isAvailable) return;
    final before = missingMetrics;
    final hadGranted = _metricPermissions?.granted.isNotEmpty ?? false;
    await _readMetricPermissions();
    final hasGranted = _metricPermissions?.granted.isNotEmpty ?? false;
    if (!setEquals(before, missingMetrics) || hadGranted != hasGranted) {
      notify();
    }
  }

  /// Opens the system permission request for every collectable metric, so
  /// the sheet shows the whole picture, then reads access again. Kinds that
  /// are still missing afterwards count as "Not now" for that set. Newly
  /// allowed kinds are read at once, each with its own backfill.
  Future<void> askForMissingMetrics() async {
    if (_askingForMore || !isAvailable) return;
    final before = missingMetrics;
    _askingForMore = true;
    notify();
    var shown = false;
    try {
      _permission = await _collector.requestPermissions(MetricKey.collectable);
      shown = true;
    } catch (e, stack) {
      // No sheet was shown (no visible screen, or one already up): nothing
      // was declined, so nothing is remembered.
      _reporter?.report(e, stack, kind: 'collector');
    }
    await _readMetricPermissions();
    _askingForMore = false;
    final after = missingMetrics;
    _askedAndStillMissing = shown && after.isNotEmpty;
    if (_askedAndStillMissing) await _rememberNotNow(after);
    notify();
    if (_sourceId != null && !setEquals(before, after)) {
      unawaited(collectAndFlush());
    }
  }

  /// "Not now" on the Today card: remembered on this device for exactly the
  /// kinds missing now. A later build that can read something new asks again.
  Future<void> notNowForMissingMetrics() async {
    final missing = missingMetrics;
    if (missing.isEmpty) return;
    await _rememberNotNow(missing);
    notify();
  }

  Future<void> _rememberNotNow(Set<MetricKey> keys) async {
    _notNow = Set.unmodifiable(keys);
    await _outbox.writeState(notNowKey, _encodeKeys(keys));
  }

  Future<void> _readMetricPermissions() async {
    try {
      _metricPermissions = await _collector.metricPermissions();
    } catch (e, stack) {
      _reporter?.report(e, stack, kind: 'collector');
      _metricPermissions = null;
    }
  }

  /// Device-wide, not per person: Health Connect access belongs to the app
  /// on this phone, whoever is signed in.
  static const String notNowKey = 'collector.permissions.notNow';

  static String _encodeKeys(Set<MetricKey> keys) =>
      (keys.map((k) => k.wireValue).toList()..sort()).join(',');

  static Set<MetricKey> _decodeKeys(String? raw) {
    if (raw == null || raw.isEmpty) return const {};
    return {for (final part in raw.split(',')) ?MetricKey.fromString(part)};
  }

  /// Opens the battery page. [onResume] reads the answer on the way back.
  Future<void> openBatterySettings() => _collector.openBatterySettings();

  Future<void> _checkBackground() async {
    if (!isAvailable) return;
    final limited = await _collector.backgroundLimited();
    if (limited == _backgroundLimited) return;
    _backgroundLimited = limited;
    notify();
  }

  /// Reads new data page by page, queueing and flushing each page. Collects
  /// while another run is in progress are coalesced into one more pass.
  Future<void> collectAndFlush() {
    final running = _inFlight;
    if (running != null) {
      _collectRequested = true;
      return running;
    }
    final future = _runCollect();
    _inFlight = future;
    unawaited(future.whenComplete(() => _inFlight = null));
    return future;
  }

  Future<void> _runCollect() async {
    final sourceId = _sourceId;
    if (sourceId == null || _status == CollectorStatus.unavailable) return;
    final userId = _auth.user?.id;
    _status = CollectorStatus.collecting;
    _error = null;
    _lastCollected = 0;
    notify();
    try {
      do {
        _collectRequested = false;
        var pages = 0;
        var hasMore = true;
        while (hasMore && pages < maxPagesPerRun) {
          pages += 1;
          _progress = pages == 1 ? 'Reading new data' : 'Reading, page $pages';
          notify();
          final result = await _collector.collectSince(
            _cursor,
            backfillFrom: _clock().subtract(_backfill),
          );
          _lastCollected += result.length;
          if (!result.isEmpty) {
            await _outbox.enqueue(
              CollectedBatch(
                sourceId: sourceId,
                samples: result.samples,
                workouts: result.workouts,
                cursorAfter: result.cursor,
              ),
            );
          }
          if (result.cursor != null) {
            _cursor = result.cursor;
            if (userId != null) {
              await _outbox.writeState(_cursorKey(userId), _cursor);
            }
          }
          _progress = result.isEmpty
              ? 'Nothing new'
              : 'Sending ${result.length} readings';
          notify();
          final clean = await _sync.flush();
          if (!clean) {
            // Rows stay in the outbox; stop reading more until they are sent.
            _fail(
              _sync.lastError ?? 'The server did not accept the batch.',
              CollectorStatus.connected,
            );
            return;
          }
          hasMore = result.hasMore;
        }
      } while (_collectRequested);
      _lastCollectAt = _clock();
      _progress = null;
      _status = CollectorStatus.connected;
      notify();
    } catch (e, stack) {
      _reporter?.report(e, stack, kind: 'collector');
      _fail(describeError(e), CollectorStatus.connected);
    }
  }

  void _fail(String message, [CollectorStatus next = CollectorStatus.error]) {
    _error = message;
    _progress = null;
    _status = next == CollectorStatus.connected && _sourceId == null
        ? CollectorStatus.disconnected
        : next;
    notify();
  }

  Future<void> _restore() async {
    final userId = _auth.user?.id;
    if (userId == null) return;
    _sourceId = await _outbox.readState(_sourceKey(userId));
    _cursor = await _outbox.readState(_cursorKey(userId));
    if (_sourceId != null && _cursor == null) {
      // Local state without a cursor: fall back to what the server kept.
      _cursor = _sources.byId(_sourceId!)?.cursor;
    }
  }

  static String _sourceKey(String userId) => 'collector.$userId.sourceId';
  static String _cursorKey(String userId) => 'collector.$userId.cursor';

  void _onAuthChanged() {
    if (_auth.isAuthenticated) return;
    _sourceId = null;
    _cursor = null;
    _progress = null;
    _error = null;
    // Unknown stays unknown: init() has not run yet and will decide.
    if (_status != CollectorStatus.unavailable &&
        _status != CollectorStatus.unknown) {
      _status = CollectorStatus.disconnected;
    }
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    unawaited(_changes?.cancel());
    super.dispose();
  }
}
