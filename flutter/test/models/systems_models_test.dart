import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/band_position.dart';
import 'package:selfkept/models/context.dart';
import 'package:selfkept/models/enums.dart';
import 'package:selfkept/models/household_overview.dart';
import 'package:selfkept/models/insight.dart';
import 'package:selfkept/models/lab.dart';
import 'package:selfkept/models/lab_review.dart';
import 'package:selfkept/models/systems.dart';
import 'package:selfkept/models/trial.dart';
import 'package:selfkept/models/weekly_review.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/systems_fixtures.dart';

Map<String, dynamic> _cased(Map<String, dynamic> json, bool pascal) =>
    pascal ? Map<String, dynamic>.from(pascalize(json) as Map) : json;

void main() {
  for (final pascal in [false, true]) {
    final casing = pascal ? 'PascalCase' : 'camelCase';

    test(
      'SystemsOverview reads systems, readings, labs and cards ($casing)',
      () {
        final s = SystemsOverview.fromJson(
          _cased(SystemsFixtures.systems(), pascal),
        );
        expect(s.systems, hasLength(2));
        final heart = s.systems.first;
        expect(heart.key, 'heart');
        expect(heart.name, 'Heart and fitness');
        expect(heart.readings.single.band, BandPosition.above);
        expect(heart.readings.single.usual?.p75, 58.5);
        expect(heart.readings.single.isOutside, isTrue);
        expect(heart.readings.single.recent, isEmpty);
        expect(heart.labs.single.previousValue, 171);
        expect(heart.labs.single.isFlagged, isTrue);
        expect(heart.insights.single.severity, InsightSeverity.watch);
        expect(s.labs?.panels, 3);
        expect(s.labs?.outsideRange, 2);
      },
    );

    test('BodySystem detail carries recent days and lab history ($casing)', () {
      final s = BodySystem.fromJson(
        _cased(SystemsFixtures.system(detail: true), pascal),
      );
      expect(s.readings.single.recent, hasLength(28));
      expect(s.labs.single.history, hasLength(2));
    });

    test('ContextList reads active items and events ($casing)', () {
      final c = ContextList.fromJson(_cased(SystemsFixtures.context(), pascal));
      expect(c.day, DateTime(2026, 11, 10));
      expect(c.active, hasLength(2));
      expect(c.active.first.kind, ContextKind.illness);
      expect(c.active.first.eventId, 'e1');
      expect(c.active.last.source, ContextSource.derived);
      expect(c.active.last.eventId, isNull);
      expect(c.events.single.note, 'Streaming cold');
      expect(c.eventById('e1')?.active, isTrue);
    });

    test('Patterns reads established and building tags ($casing)', () {
      final p = Patterns.fromJson(_cased(SystemsFixtures.patterns(), pascal));
      expect(p.minNightsWith, 8);
      expect(p.windowDays, 120);
      expect(p.patterns.single.nightsWith, 9);
      expect(p.patterns.single.sentence, contains('not proof'));
      expect(p.building.single.tagLabel, 'Late meal');
    });

    test('SleepNights and Workouts read back ($casing)', () {
      final s = SleepNights.fromJson(_cased(SystemsFixtures.sleep(), pascal));
      expect(s.nights, hasLength(2));
      expect(s.nights.first.hasStages, isFalse);
      expect(s.nights.first.deepMinutes, isNull);
      expect(s.nights.last.remMinutes, 195);
      final w = Workouts.fromJson(_cased(SystemsFixtures.workouts(), pascal));
      expect(w.workouts.single.typeLabel, 'Running');
      expect(w.workouts.single.distanceM, 7200);
      expect(w.weeks.single.strength, 1);
    });

    test('WeeklyReview keeps null sections as not shared ($casing)', () {
      final r = WeeklyReview.fromJson(
        _cased(SystemsFixtures.review(shared: false), pascal),
      );
      expect(r.weekOf, DateTime(2026, 11, 2));
      expect(r.targets, isNull);
      expect(r.goals, isNull);
      expect(r.standouts, isNull);
      expect(r.insights, isNull);
      expect(r.patterns, isNull);
      expect(r.sleep?.summary, startsWith('7 nights'));
      final shared = WeeklyReview.fromJson(
        _cased(SystemsFixtures.review(), pascal),
      );
      expect(shared.targets?.kept.single.metDays, 6);
      expect(shared.goals?.single.summary, contains('0.5 kg down'));
    });

    test(
      'LabReview reads verdicts, conditions, weight and trials ($casing)',
      () {
        final r = LabReview.fromJson(
          _cased(SystemsFixtures.labReview(), pascal),
        );
        expect(r.markers.first.verdict, LabVerdict.working);
        expect(r.markers.first.beyondRcv, isTrue);
        expect(r.markers.first.variation?.cviPercent, 8.9);
        expect(r.markers.last.verdict, LabVerdict.cannotTell);
        expect(r.markers.last.reasons, contains('draw_conditions_differ'));
        expect(r.markers.last.readWithCare.single.source, 'Nordestgaard 2016');
        expect(r.conditions.fasting, DrawFasting.no);
        expect(r.conditions.drawTime, '15:40');
        expect(r.previousConditions?.defaulted, isTrue);
        expect(r.conditionsDiffer, hasLength(2));
        expect(r.weight?.changeKg, -3.5);
        expect(r.trials?.single.sharedCredit, isTrue);
        expect(r.trials?.single.verdicts['lab.ggt'], LabVerdict.working);
        expect(r.verdictCounts[LabVerdict.cannotTell], 1);
        expect(r.takingNow.single.label, 'Creatine monohydrate');
      },
    );

    test('LabTimeline and RelatedReadings ($casing)', () {
      final t = LabTimeline.fromJson(
        _cased(SystemsFixtures.timeline(), pascal),
      );
      expect(t.weightShared, isTrue);
      expect(t.panels.single.markerCount, 30);
      expect(t.panels.single.weight?.kg, 84);
      final r = RelatedReadings.fromJson(
        _cased(SystemsFixtures.related(), pascal),
      );
      expect(r.related.first.shared, isTrue);
      expect(r.related.first.links.single.source, 'WHO 2020');
      expect(r.related.last.shared, isFalse);
      expect(r.related.last.latestValue, isNull);
    });

    test('Trial reads status and readings ($casing)', () {
      final t = Trial.fromJson(_cased(SystemsFixtures.trial(), pascal));
      expect(t.title, 'Oats instead of a bagel');
      expect(t.status, TrialStatus.active);
      expect(t.metricKeys, ['lab.ldl', 'resting_hr_bpm']);
      expect(t.readOn, DateTime(2026, 12, 12));
      expect(t.endedAt, isNull);
    });

    test('HouseholdOverview leaves absent parts null ($casing)', () {
      final o = HouseholdOverview.fromJson(
        _cased(SystemsFixtures.overview(), pascal),
      );
      final ada = o.members.first;
      expect(ada.isSelf, isTrue);
      expect(ada.labs?.outOfRange, 2);
      expect(ada.body?.mainGoal, 'Build muscle');
      final charles = o.members.last;
      expect(charles.sharedCategories, [ShareCategory.activity]);
      expect(charles.activity?.value, 8421);
      expect(charles.labs, isNull);
      expect(charles.body, isNull);
      expect(charles.sleep, isNull);
    });

    test('LabReport carries its draw conditions ($casing)', () {
      final json = {
        ...Fixtures.labReport(),
        'conditions': SystemsFixtures.conditions(),
      };
      final report = LabReport.fromJson(_cased(json, pascal));
      expect(report.conditions?.fasting, DrawFasting.yes);
      expect(report.conditions?.fastingHours, 8);
      expect(report.conditions?.drawPeriod, DrawPeriod.morning);
      expect(report.conditions?.defaulted, isTrue);
    });
  }

  test('a report from an older server has no conditions', () {
    expect(LabReport.fromJson(Fixtures.labReport()).conditions, isNull);
  });

  test('enums read their wire values and fall back', () {
    expect(ContextKind.fromString('hard_training'), ContextKind.hardTraining);
    expect(ContextKind.fromString('nonsense'), ContextKind.other);
    expect(ContextKind.medicationChange.toJson(), 'medication_change');
    expect(LabVerdict.fromString('right_direction'), LabVerdict.rightDirection);
    expect(LabVerdict.fromString('not_working').displayName, 'Not working');
    expect(LabVerdict.fromString(null), LabVerdict.cannotTell);
    expect(TrialStatus.fromString('dropped'), TrialStatus.dropped);
    expect(DrawPeriod.fromString('EVENING'), DrawPeriod.evening);
    expect(DrawFasting.fromString(null), DrawFasting.unknown);
  });

  test('draw conditions send only what is known', () {
    const known = DrawConditions(
      fasting: DrawFasting.yes,
      fastingHours: 10,
      drawPeriod: DrawPeriod.morning,
      unwell: false,
    );
    expect(known.toInputJson(), {
      'fasting': 'yes',
      'fastingHours': 10,
      'drawPeriod': 'morning',
      'unwell': false,
    });
    const timed = DrawConditions(drawTime: '15:40');
    expect(timed.toInputJson(), {'drawTime': '15:40'});
    expect(const DrawConditions().toInputJson(), isEmpty);
    expect(const DrawConditions().isEmpty, isTrue);
  });

  test('context drafts: create leaves out blanks, update sends reopen', () {
    final draft = ContextDraft(
      kind: ContextKind.travel,
      startedOn: DateTime(2026, 11, 8),
    );
    expect(draft.toCreateJson(), {'kind': 'travel', 'startedOn': '2026-11-08'});
    expect(draft.toUpdateJson(), {
      'kind': 'travel',
      'startedOn': '2026-11-08',
      'reopen': true,
      'note': '',
    });
  });

  test('a trial update sends only what changed', () {
    final original = Trial.fromJson(SystemsFixtures.trial());
    final draft = TrialDraft(
      title: 'Oats instead of a bagel',
      change: 'Oats at breakfast',
      startedOn: DateTime(2026, 8, 16),
      readOn: DateTime(2027, 1, 10),
      metricKeys: const ['lab.ldl', 'resting_hr_bpm'],
    );
    expect(draft.toUpdateJson(original), {'readOn': '2027-01-10'});
  });
}
