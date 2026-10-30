import '../utils/json_reader.dart';

/// Where a stream of samples comes from.
enum SourceKind {
  healthkit('healthkit'),
  healthConnect('health_connect'),
  oura('oura'),
  googleHealth('google_health'),
  manual('manual'),
  labScan('lab_scan');

  final String wireValue;

  const SourceKind(this.wireValue);

  String toJson() => wireValue;

  /// Devices and cloud links can be paused; the built-in check-in and lab
  /// sources cannot.
  bool get canPause => this != manual && this != labScan;

  static SourceKind fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : manual;

  static SourceKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final kind in values) {
      if (kind.wireValue == lower || kind.name.toLowerCase() == lower) {
        return kind;
      }
    }
    return manual;
  }

  String get displayName => switch (this) {
    healthkit => 'Apple Health',
    healthConnect => 'Health Connect',
    oura => 'Oura',
    googleHealth => 'Google Health',
    manual => 'Entered by hand',
    labScan => 'Lab scan',
  };

  /// True for the two on-phone collectors.
  bool get isPhoneCollector => this == healthkit || this == healthConnect;
}

/// A registered source of samples: one per device install for the phone
/// collectors, one per linked account otherwise.
class Source {
  final String id;
  final SourceKind kind;
  final String deviceName;
  final String platform;
  final bool enabled;
  final DateTime? lastSyncAt;
  final DateTime? lastSampleAt;
  final int sampleCount;

  /// Opaque resume token the client stored after its last accepted batch.
  final String? cursor;

  const Source({
    required this.id,
    required this.kind,
    this.deviceName = '',
    this.platform = '',
    this.enabled = true,
    this.lastSyncAt,
    this.lastSampleAt,
    this.sampleCount = 0,
    this.cursor,
  });

  factory Source.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Source(
      id: r.string('id'),
      kind: SourceKind.fromString(r.stringOrNull('kind')),
      deviceName: r.string('deviceName'),
      platform: r.string('platform'),
      enabled: r.boolean('enabled', fallback: true),
      lastSyncAt: r.dateTime('lastSyncAt'),
      lastSampleAt: r.dateTime('lastSampleAt'),
      sampleCount: r.integer('sampleCount'),
      cursor: r.stringOrNull('cursor'),
    );
  }

  /// Body of POST /sources.
  static Map<String, dynamic> toCreateJson({
    required SourceKind kind,
    required String deviceName,
    required String platform,
    required String appVersion,
  }) => {
    'kind': kind.wireValue,
    'deviceName': deviceName,
    'platform': platform,
    'appVersion': appVersion,
  };

  Source copyWith({
    String? deviceName,
    bool? enabled,
    DateTime? lastSyncAt,
    DateTime? lastSampleAt,
    int? sampleCount,
    String? cursor,
  }) => Source(
    id: id,
    kind: kind,
    deviceName: deviceName ?? this.deviceName,
    platform: platform,
    enabled: enabled ?? this.enabled,
    lastSyncAt: lastSyncAt ?? this.lastSyncAt,
    lastSampleAt: lastSampleAt ?? this.lastSampleAt,
    sampleCount: sampleCount ?? this.sampleCount,
    cursor: cursor ?? this.cursor,
  );
}
