import '../utils/json_reader.dart';

/// One month whose heart rate was folded into one row a minute.
class RetentionMonth {
  final DateTime month;
  final int rawRows;
  final int minuteRows;
  final DateTime? compactedAt;

  const RetentionMonth({
    required this.month,
    this.rawRows = 0,
    this.minuteRows = 0,
    this.compactedAt,
  });

  factory RetentionMonth.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return RetentionMonth(
      month: r.dateTime('month') ?? DateTime(1970),
      rawRows: r.integer('rawRows'),
      minuteRows: r.integer('minuteRows'),
      compactedAt: r.dateTime('compactedAt'),
    );
  }
}

/// Answer of GET /retention: how long raw readings are kept, and what was folded.
class Retention {
  /// 0 keeps raw readings for ever.
  final int rawMonths;

  /// "default", "environment" or "app".
  final String source;
  final int minimumMonths;

  /// The first day still kept raw; null when raw is kept for ever.
  final DateTime? rawSince;

  /// Newest first.
  final List<RetentionMonth> months;

  const Retention({
    this.rawMonths = 12,
    this.source = 'default',
    this.minimumMonths = 4,
    this.rawSince,
    this.months = const [],
  });

  bool get keepsForever => rawMonths == 0;

  /// Set in the server's .env, so the app shows it but cannot change it.
  bool get fromEnvironment => source == 'environment';

  factory Retention.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Retention(
      rawMonths: r.integer('rawMonths', fallback: 12),
      source: r.string('source', fallback: 'default'),
      minimumMonths: r.integer('minimumMonths', fallback: 4),
      rawSince: r.dateTime('rawSince'),
      months: [for (final m in r.objects('months')) RetentionMonth.fromJson(m)],
    );
  }
}

/// Answer of POST /retention/run.
class RetentionRun {
  /// How many months this run folded.
  final int folded;

  /// How many months it rewrote on disk.
  final int compacted;

  const RetentionRun({this.folded = 0, this.compacted = 0});

  factory RetentionRun.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return RetentionRun(
      folded: r.objects('folded').length,
      compacted: r.strings('compacted').length,
    );
  }
}
