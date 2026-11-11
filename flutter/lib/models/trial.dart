import '../utils/json_reader.dart';
import 'date_only.dart';

/// Where a trial stands.
enum TrialStatus {
  active,
  done,
  dropped;

  String get wireValue => name;
  String toJson() => wireValue;

  static TrialStatus fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : active;

  static TrialStatus fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.name == lower) return s;
    }
    return active;
  }

  String get displayName => switch (this) {
    active => 'Running',
    done => 'Done',
    dropped => 'Dropped',
  };
}

/// One change with a planned check: what is tried, what it should move,
/// when it started and when to read the result.
class Trial {
  final String id;
  final String title;
  final String? change;
  final DateTime startedOn;
  final DateTime readOn;
  final List<String> metricKeys;
  final TrialStatus status;
  final String? outcome;
  final DateTime? createdAt;
  final DateTime? updatedAt;
  final DateTime? endedAt;

  const Trial({
    required this.id,
    this.title = '',
    this.change,
    required this.startedOn,
    required this.readOn,
    this.metricKeys = const [],
    this.status = TrialStatus.active,
    this.outcome,
    this.createdAt,
    this.updatedAt,
    this.endedAt,
  });

  bool get isActive => status == TrialStatus.active;

  factory Trial.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final epoch = DateTime.fromMillisecondsSinceEpoch(0);
    return Trial(
      id: r.string('id'),
      title: r.string('title'),
      change: r.stringOrNull('change'),
      startedOn: r.dateTime('startedOn') ?? epoch,
      readOn: r.dateTime('readOn') ?? epoch,
      metricKeys: r.strings('metricKeys'),
      status: TrialStatus.fromString(r.stringOrNull('status')),
      outcome: r.stringOrNull('outcome'),
      createdAt: r.dateTime('createdAt'),
      updatedAt: r.dateTime('updatedAt'),
      endedAt: r.dateTime('endedAt'),
    );
  }

  static List<Trial> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) Trial.fromJson(item),
  ];
}

/// What the trial editor sends. On an update only the fields that differ
/// from [original] are sent.
class TrialDraft {
  final String title;
  final String change;
  final DateTime? startedOn;
  final DateTime readOn;
  final List<String> metricKeys;

  const TrialDraft({
    required this.title,
    this.change = '',
    this.startedOn,
    required this.readOn,
    this.metricKeys = const [],
  });

  Map<String, dynamic> toCreateJson() => {
    'title': title.trim(),
    if (change.trim().isNotEmpty) 'change': change.trim(),
    if (startedOn != null) 'startedOn': dateOnly(startedOn!),
    'readOn': dateOnly(readOn),
    'metricKeys': metricKeys,
  };

  Map<String, dynamic> toUpdateJson(Trial original) => {
    if (title.trim() != original.title) 'title': title.trim(),
    if (change.trim() != (original.change ?? '')) 'change': change.trim(),
    if (startedOn != null &&
        dateOnly(startedOn!) != dateOnly(original.startedOn))
      'startedOn': dateOnly(startedOn!),
    if (dateOnly(readOn) != dateOnly(original.readOn))
      'readOn': dateOnly(readOn),
    if (!_sameList(metricKeys, original.metricKeys)) 'metricKeys': metricKeys,
  };

  static bool _sameList(List<String> a, List<String> b) {
    if (a.length != b.length) return false;
    for (var i = 0; i < a.length; i++) {
      if (a[i] != b[i]) return false;
    }
    return true;
  }
}
