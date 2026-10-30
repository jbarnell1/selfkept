import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/band_position.dart';
import 'package:selfkept/models/insight.dart';
import 'package:selfkept/models/today.dart';
import 'package:selfkept/models/trend.dart';

import '../helpers/fake_api_client.dart';

void main() {
  for (final pascal in [false, true]) {
    final casing = pascal ? 'PascalCase' : 'camelCase';

    group('Insight.fromJson ($casing)', () {
      test('reads every field and the enums', () {
        final i = Insight.fromJson(
          Fixtures.insight(pascal: pascal, reaction: 'knew'),
        );
        expect(i.id, 'i1');
        expect(i.day, DateTime(2026, 10, 29));
        expect(i.kind, 'hrv_low_run');
        expect(i.severity, InsightSeverity.watch);
        expect(i.metrics, ['hrv_sdnn_ms']);
        expect(i.evidence['nights'], 3);
        expect(i.text, startsWith('HRV has sat below'));
        expect(i.generated, isFalse);
        expect(i.shownAt, DateTime.utc(2026, 10, 29, 6));
        expect(i.dismissedAt, isNull);
        expect(i.isDismissed, isFalse);
        expect(i.reaction, InsightReaction.knew);
      });

      test('a dismissed card and an absent reaction', () {
        final i = Insight.fromJson(
          Fixtures.insight(pascal: pascal, dismissedAt: '2026-10-29T09:00:00Z'),
        );
        expect(i.isDismissed, isTrue);
        expect(i.reaction, isNull);
      });
    });

    group('Today.fromJson ($casing)', () {
      test('reads sentence, readiness, cards and the check-in', () {
        final t = Today.fromJson(Fixtures.today(pascal: pascal));
        expect(t.day, DateTime(2026, 10, 29));
        expect(t.sentence, startsWith('You slept 7 h 40'));
        expect(t.sleepMinutes, 460);
        expect(t.readiness.hrv, BandPosition.inside);
        expect(t.readiness.restingHr, BandPosition.inside);
        expect(t.readiness.sleep, BandPosition.below);
        expect(t.cards, hasLength(2));
        expect(t.cards.first.severity, InsightSeverity.watch);
        expect(t.cards.last.kind, 'sleep_good_week');
        expect(t.lastCheckin?.mood, 5);
        expect(t.learning, isFalse);
      });

      test('null readiness means no band yet, and cards are capped at two', () {
        final t = Today.fromJson(
          Fixtures.today(
            pascal: pascal,
            learning: true,
            sleepMinutes: null,
            hrv: null,
            restingHr: null,
            sleep: null,
            cards: [
              Fixtures.insight(pascal: pascal, id: 'a'),
              Fixtures.insight(pascal: pascal, id: 'b'),
              Fixtures.insight(pascal: pascal, id: 'c'),
            ],
          ),
        );
        expect(t.learning, isTrue);
        expect(t.sleepMinutes, isNull);
        expect(t.readiness.isEmpty, isTrue);
        expect(t.cards.map((c) => c.id), ['a', 'b']);
      });
    });

    group('Trend models ($casing)', () {
      test(
        'TrendMetric.listFromJson takes the bare array the server sends',
        () {
          final wrapped = Fixtures.trends(pascal: pascal);
          final bare = (wrapped[pascal ? 'Metrics' : 'metrics'] as List)
              .toList();
          expect(
            TrendMetric.listFromJson(bare).map((m) => m.metric),
            TrendMetric.listFromJson(wrapped).map((m) => m.metric),
          );
        },
      );

      test('TrendMetric.listFromJson sorts by product priority', () {
        final list = TrendMetric.listFromJson(Fixtures.trends(pascal: pascal));
        expect(list.map((m) => m.metric), [
          'sleep_stage',
          'resting_hr_bpm',
          'steps',
        ]);
        final sleep = list.first;
        expect(sleep.unit, 'min');
        expect(sleep.label, 'Sleep');
        expect(sleep.latestValue, 460);
        expect(sleep.latestDay, DateTime(2026, 10, 29));
        expect(sleep.band, BandPosition.below);
        expect(sleep.baselineDays, 28);
      });

      test('TrendMetric falls back to the catalogue name', () {
        final m = TrendMetric.fromJson(
          Fixtures.trendMetric(
            pascal: pascal,
            metric: 'hrv_rmssd_ms',
            displayName: '',
            band: null,
          ),
        );
        expect(m.label, 'HRV (RMSSD)');
        expect(m.band, isNull);
      });

      test('TrendSeries reads points sorted by day and the band', () {
        final s = TrendSeries.fromJson(Fixtures.trendSeries(pascal: pascal));
        expect(s.metric, 'sleep_stage');
        expect(s.points, hasLength(28));
        expect(s.points.first.day.isBefore(s.points.last.day), isTrue);
        expect(s.points.last.day, DateTime(2026, 10, 29));
        expect(s.points.first.min, isNotNull);
        expect(s.band?.median28, 452);
        expect(s.band?.p25, 436);
        expect(s.band?.p90, 485);
        expect(s.band!.isOutlier(300), isTrue);
        expect(s.band!.isOutlier(450), isFalse);
        expect(s.band!.positionOf(430), BandPosition.below);
        expect(s.band!.positionOf(480), BandPosition.above);
        expect(s.band!.positionOf(450), BandPosition.inside);
      });

      test('TrendSeries without a band is still learning', () {
        final s = TrendSeries.fromJson(
          Fixtures.trendSeries(pascal: pascal, withBand: false),
        );
        expect(s.band, isNull);
        expect(s.baselineDays, 9);
      });
    });
  }

  group('enums follow the house pattern', () {
    test('InsightSeverity', () {
      expect(InsightSeverity.fromString('WATCH'), InsightSeverity.watch);
      expect(InsightSeverity.fromString('nope'), InsightSeverity.notice);
      expect(InsightSeverity.fromInt(2), InsightSeverity.clinician);
      expect(InsightSeverity.fromInt(9), InsightSeverity.notice);
      expect(InsightSeverity.clinician.toJson(), 'clinician');
      expect(InsightSeverity.watch.displayName, 'Watch');
    });

    test('InsightReaction', () {
      expect(InsightReaction.fromString('wrong'), InsightReaction.wrong);
      expect(InsightReaction.fromString(null), isNull);
      expect(InsightReaction.fromString('meh'), isNull);
      expect(InsightReaction.fromInt(1), InsightReaction.knew);
      expect(InsightReaction.knew.displayName, 'Knew that');
      expect(InsightReaction.helpful.toJson(), 'helpful');
      expect(Insight.toReactionJson(InsightReaction.wrong), {
        'reaction': 'wrong',
      });
    });

    test('BandPosition', () {
      expect(BandPosition.fromString('Above'), BandPosition.above);
      expect(BandPosition.fromString(null), isNull);
      expect(BandPosition.fromString('sideways'), isNull);
      expect(BandPosition.fromInt(0), BandPosition.below);
      expect(BandPosition.inside.displayName, 'in your range');
      expect(BandPosition.below.toJson(), 'below');
    });

    test('compareTrendMetrics puts unknown keys last, alphabetically', () {
      final keys = ['zeta', 'weight_kg', 'alpha', 'sleep_stage'];
      keys.sort(compareTrendMetrics);
      expect(keys, ['sleep_stage', 'weight_kg', 'alpha', 'zeta']);
    });
  });
}
