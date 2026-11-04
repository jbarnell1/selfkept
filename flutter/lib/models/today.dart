import '../utils/json_reader.dart';
import 'band_position.dart';
import 'checkin.dart';
import 'insight.dart';

/// A readiness metric whose range is ready but had no reading last night (a
/// watch left off or flat, a phone that has not sent yet).
class ReadingGap {
  /// The last day with a reading in the past two weeks; null for none.
  final DateTime? lastDay;

  const ReadingGap({this.lastDay});

  static ReadingGap? fromJson(Map<String, dynamic>? json) {
    if (json == null) return null;
    return ReadingGap(lastDay: JsonReader(json).dateTime('lastDay'));
  }
}

/// Last night's three readiness markers against the person's own band. Each
/// is null until the band exists, or when last night has no reading; then
/// the matching gap says so.
class Readiness {
  final BandPosition? hrv;
  final BandPosition? restingHr;
  final BandPosition? sleep;
  final ReadingGap? hrvGap;
  final ReadingGap? restingHrGap;
  final ReadingGap? sleepGap;

  const Readiness({
    this.hrv,
    this.restingHr,
    this.sleep,
    this.hrvGap,
    this.restingHrGap,
    this.sleepGap,
  });

  bool get isEmpty => hrv == null && restingHr == null && sleep == null;

  factory Readiness.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final gaps = JsonReader(r.object('gaps') ?? const {});
    return Readiness(
      hrv: BandPosition.fromString(r.stringOrNull('hrv')),
      restingHr: BandPosition.fromString(r.stringOrNull('restingHr')),
      sleep: BandPosition.fromString(r.stringOrNull('sleep')),
      hrvGap: ReadingGap.fromJson(gaps.object('hrv')),
      restingHrGap: ReadingGap.fromJson(gaps.object('restingHr')),
      sleepGap: ReadingGap.fromJson(gaps.object('sleep')),
    );
  }
}

/// Answer of GET /today: the sentence, the readiness row, at most two cards.
class Today {
  final DateTime day;
  final String sentence;

  /// True when a local model phrased the sentence.
  final bool generated;
  final int? sleepMinutes;
  final Readiness readiness;
  final List<Insight> cards;
  final Checkin? lastCheckin;

  /// True while fewer than 14 days of any baseline metric exist.
  final bool learning;

  const Today({
    required this.day,
    required this.sentence,
    this.generated = false,
    this.sleepMinutes,
    this.readiness = const Readiness(),
    this.cards = const [],
    this.lastCheckin,
    this.learning = false,
  });

  /// Today never shows more than two cards, whatever the server sends.
  static const int maxCards = 2;

  factory Today.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final readiness = r.object('readiness');
    final checkin = r.object('lastCheckin');
    final cards = [for (final c in r.objects('cards')) Insight.fromJson(c)];
    return Today(
      day: r.dateTime('day') ?? DateTime.fromMillisecondsSinceEpoch(0),
      sentence: r.string('sentence'),
      generated: r.boolean('generated'),
      sleepMinutes: readDouble(r.raw('sleepMinutes'))?.round(),
      readiness: readiness == null
          ? const Readiness()
          : Readiness.fromJson(readiness),
      cards: cards.length > maxCards ? cards.sublist(0, maxCards) : cards,
      lastCheckin: checkin == null ? null : Checkin.fromJson(checkin),
      learning: r.boolean('learning'),
    );
  }
}
