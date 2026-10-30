import '../utils/json_reader.dart';

/// One warning or error the server logged (its own, or one an app reported).
class ProblemEntry {
  final DateTime? at;

  /// "warning" or "error".
  final String level;
  final String source;
  final String message;
  final String? detail;

  const ProblemEntry({
    this.at,
    this.level = 'warning',
    this.source = '',
    this.message = '',
    this.detail,
  });

  bool get isError => level == 'error';

  factory ProblemEntry.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ProblemEntry(
      at: r.dateTime('at'),
      level: r.string('level', fallback: 'warning'),
      source: r.string('source'),
      message: r.string('message'),
      detail: r.stringOrNull('detail'),
    );
  }
}

/// Answer of GET /diagnostics/problems: newest first.
class RecentProblems {
  final int last24Hours;
  final List<ProblemEntry> items;

  const RecentProblems({this.last24Hours = 0, this.items = const []});

  factory RecentProblems.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return RecentProblems(
      last24Hours: r.integer('last24Hours'),
      items: [for (final i in r.objects('items')) ProblemEntry.fromJson(i)],
    );
  }
}
