import '../utils/json_reader.dart';

/// A mood check-in. The mood is 1 (lowest) to 6 (highest); the note is
/// encrypted at rest on the server.
class Checkin {
  final String id;
  final String personId;
  final DateTime at;
  final int mood;
  final String? note;
  final List<String> tags;

  const Checkin({
    required this.id,
    this.personId = '',
    required this.at,
    required this.mood,
    this.note,
    this.tags = const [],
  });

  static const int minMood = 1;
  static const int maxMood = 6;

  factory Checkin.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Checkin(
      id: r.string('id'),
      personId: r.string('personId'),
      at: r.dateTime('at') ?? DateTime.fromMillisecondsSinceEpoch(0),
      mood: r.integer('mood', fallback: 3).clamp(minMood, maxMood),
      note: r.stringOrNull('note'),
      tags: r.strings('tags'),
    );
  }

  /// Body of POST /checkins. `at` is omitted when null so the server stamps now.
  static Map<String, dynamic> toCreateJson({
    required int mood,
    String? note,
    DateTime? at,
  }) => {
    if (at != null) 'at': at.toUtc().toIso8601String(),
    'mood': mood.clamp(minMood, maxMood),
    if (note != null && note.trim().isNotEmpty) 'note': note.trim(),
  };
}
