import 'package:flutter/foundation.dart'
    show TargetPlatform, defaultTargetPlatform, kIsWeb;

import '../models/metric.dart';
import '../models/sample.dart';
import '../models/source.dart';
import '../utils/json_reader.dart';
import 'channel_health_collector.dart';

/// Outcome of asking the platform for read access.
enum PermissionState {
  notDetermined,
  granted,

  /// Some of the requested types were refused.
  partial,
  denied,

  /// The platform has no health store (web, desktop, a phone without
  /// Health Connect).
  unavailable;

  String get wireValue => switch (this) {
    notDetermined => 'not_determined',
    granted => 'granted',
    partial => 'partial',
    denied => 'denied',
    unavailable => 'unavailable',
  };
  String toJson() => wireValue;

  static PermissionState fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : notDetermined;

  static PermissionState fromString(String? value) {
    final lower = value?.trim().toLowerCase().replaceAll('-', '_');
    for (final s in values) {
      if (s.wireValue == lower || s.name.toLowerCase() == lower) return s;
    }
    return notDetermined;
  }

  String get displayName => switch (this) {
    notDetermined => 'Not asked yet',
    granted => 'Allowed',
    partial => 'Partly allowed',
    denied => 'Not allowed',
    unavailable => 'Not available here',
  };

  /// True when at least something can be read.
  bool get allowsReading => this == granted || this == partial;
}

/// What one call to [HealthCollector.collectSince] returned.
class CollectResult {
  final List<Sample> samples;
  final List<Workout> workouts;

  /// Resume token after this page. Opaque to Dart.
  final String? cursor;

  /// True when the platform capped the page and another call will return
  /// more. The backfill is paged this way.
  final bool hasMore;

  const CollectResult({
    this.samples = const [],
    this.workouts = const [],
    this.cursor,
    this.hasMore = false,
  });

  static const CollectResult empty = CollectResult();

  bool get isEmpty => samples.isEmpty && workouts.isEmpty;
  int get length => samples.length + workouts.length;

  /// Decodes the map the platform channel hands back.
  factory CollectResult.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return CollectResult(
      samples: [for (final s in r.objects('samples')) Sample.fromJson(s)],
      workouts: [for (final w in r.objects('workouts')) Workout.fromJson(w)],
      cursor: r.stringOrNull('cursor'),
      hasMore: r.boolean('hasMore'),
    );
  }
}

/// A platform health store, read on the device. One implementation per
/// platform; the app talks to this interface only.
abstract class HealthCollector {
  /// Which server source kind this collector feeds.
  SourceKind get kind;

  /// True when the store exists and the app may ask for access.
  Future<bool> isAvailable();

  Future<PermissionState> requestPermissions(Set<MetricKey> metrics);

  /// Reads what changed since [cursor] (everything from [backfillFrom] when a
  /// type has no cursor yet). Pages: check [CollectResult.hasMore].
  Future<CollectResult> collectSince(String? cursor, {DateTime? backfillFrom});

  /// Fires when the store reports new data, including background wakes.
  Stream<void> get changes;

  /// Something to call this phone on the Sources screen.
  Future<String> deviceName();
}

/// Web and desktop: nothing to read.
class NoopHealthCollector implements HealthCollector {
  const NoopHealthCollector();

  @override
  SourceKind get kind => SourceKind.manual;

  @override
  Future<bool> isAvailable() async => false;

  @override
  Future<PermissionState> requestPermissions(Set<MetricKey> metrics) async =>
      PermissionState.unavailable;

  @override
  Future<CollectResult> collectSince(
    String? cursor, {
    DateTime? backfillFrom,
  }) async => CollectResult.empty;

  @override
  Stream<void> get changes => const Stream.empty();

  @override
  Future<String> deviceName() async => 'This device';
}

/// Picks the collector for the running platform.
HealthCollector createHealthCollector() {
  if (kIsWeb) return const NoopHealthCollector();
  return switch (defaultTargetPlatform) {
    TargetPlatform.iOS => ChannelHealthCollector(kind: SourceKind.healthkit),
    TargetPlatform.android => ChannelHealthCollector(
      kind: SourceKind.healthConnect,
    ),
    _ => const NoopHealthCollector(),
  };
}
