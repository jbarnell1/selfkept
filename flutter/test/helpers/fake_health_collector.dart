import 'dart:async';

import 'package:selfkept/collectors/health_collector.dart';
import 'package:selfkept/models/metric.dart';
import 'package:selfkept/models/sample.dart';
import 'package:selfkept/models/source.dart';

/// A scripted collector. Queue pages with [pages]; each collectSince call
/// pops one. Records what it was asked.
class FakeHealthCollector implements HealthCollector {
  @override
  SourceKind kind;
  bool available;
  PermissionState permission;
  final List<CollectResult> pages = [];
  final List<String?> cursorsSeen = [];
  final List<DateTime?> backfillsSeen = [];
  Set<MetricKey>? requested;
  int requestCount = 0;

  /// What [metricPermissions] answers. Null is a platform that cannot say,
  /// which is the default (HealthKit, web).
  MetricPermissions? metricAccess;

  /// When set, a permission request replaces [metricAccess] with this, as if
  /// the person changed their answer on the system sheet.
  MetricPermissions? metricAccessAfterRequest;

  /// When set, a permission request throws this instead of showing a sheet.
  Object? requestError;
  bool limited = false;
  int batterySettingsOpened = 0;
  Object? collectError;
  final StreamController<void> _changes = StreamController<void>.broadcast();

  FakeHealthCollector({
    this.kind = SourceKind.healthkit,
    this.available = true,
    this.permission = PermissionState.granted,
  });

  @override
  Future<bool> isAvailable() async => available;

  @override
  Future<PermissionState> requestPermissions(Set<MetricKey> metrics) async {
    requested = metrics;
    requestCount++;
    final error = requestError;
    if (error != null) throw error;
    final after = metricAccessAfterRequest;
    if (after != null) metricAccess = after;
    return permission;
  }

  @override
  Future<MetricPermissions?> metricPermissions() async => metricAccess;

  @override
  Future<CollectResult> collectSince(
    String? cursor, {
    DateTime? backfillFrom,
  }) async {
    cursorsSeen.add(cursor);
    backfillsSeen.add(backfillFrom);
    final error = collectError;
    if (error != null) throw error;
    if (pages.isEmpty) return CollectResult(cursor: cursor);
    return pages.removeAt(0);
  }

  @override
  Stream<void> get changes => _changes.stream;

  /// Simulates the platform reporting new data.
  void emitChange() => _changes.add(null);

  @override
  Future<String> deviceName() async => 'Test phone';

  @override
  Future<bool> backgroundLimited() async => limited;

  @override
  Future<void> openBatterySettings() async => batterySettingsOpened++;

  Future<void> close() => _changes.close();
}

/// A heart rate sample with a unique sourceUid.
Sample hrSample(int n, {DateTime? at}) => Sample.of(
  MetricKey.heartRateBpm,
  sourceUid: 'hk-$n',
  start: at ?? DateTime.utc(2026, 10, 29, 6, 0, n % 60),
  value: 50 + n % 40,
);

List<Sample> hrSamples(int count, {int from = 0}) => [
  for (var i = from; i < from + count; i++) hrSample(i),
];
