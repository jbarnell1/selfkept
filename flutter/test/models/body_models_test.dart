import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/goal.dart';
import 'package:selfkept/models/journal.dart';
import 'package:selfkept/models/medication.dart';
import 'package:selfkept/models/person.dart';
import 'package:selfkept/models/progress.dart';
import 'package:selfkept/models/strength.dart';
import 'package:selfkept/models/target.dart';

import '../helpers/body_fixtures.dart';
import '../helpers/fake_api_client.dart';

void main() {
  for (final pascal in [false, true]) {
    final keys = pascal ? 'PascalCase' : 'camelCase';

    group('fromJson with $keys keys', () {
      test('Goal', () {
        final g = Goal.fromJson(BodyFixtures.goal(pascal: pascal));
        expect(g.id, 'g1');
        expect(g.slot, GoalSlot.main);
        expect(g.kind, GoalKind.loseFat);
        expect(g.title, 'Lose fat');
        expect(g.metricKey, 'weight_kg');
        expect(g.startValue, 86);
        expect(g.targetValue, 80);
        expect(g.pacePerWeek, -0.5);
        expect(g.startedOn, DateTime(2026, 9, 1));
        expect(g.targetDate, DateTime(2027, 3, 1));
        expect(g.isActive, isTrue);
        expect(g.endedAt, isNull);
      });

      test('Target', () {
        final t = Target.fromJson(
          BodyFixtures.target(pascal: pascal, sortOrder: 2),
        );
        expect(t.id, 't1');
        expect(t.goalId, 'g1');
        expect(t.label, 'Protein');
        expect(t.kind, TargetKind.metric);
        expect(t.metricKey, 'protein_g');
        expect(t.comparator, TargetComparator.atLeast);
        expect(t.value, 130);
        expect(t.period, TargetPeriod.day);
        expect(t.active, isTrue);
        expect(t.sortOrder, 2);
        expect(t.unit, 'g');
      });

      test('TargetTally keeps unknown days unknown', () {
        final t = TargetTally.fromJson(
          BodyFixtures.targetTally(pascal: pascal),
        );
        expect(t.target.label, 'Protein');
        expect(t.current, 86);
        expect(t.met, isNull);
        expect(t.week, [true, false, null, true, true, null, null]);
        expect(t.metDays, 3);
        expect(t.knownDays, 4);
      });

      test('GoalTally', () {
        final t = GoalTally.fromJson(BodyFixtures.tally(pascal: pascal));
        expect(t.day, DateTime(2026, 11, 10));
        expect(t.goals.single.id, 'g1');
        expect(t.targets.single.target.id, 't1');
        expect(t.isEmpty, isFalse);
      });

      test('JournalEntry', () {
        final e = JournalEntry.fromJson(
          BodyFixtures.journalEntry(pascal: pascal),
        );
        expect(e.id, 'j1');
        expect(e.kind, JournalKind.food);
        expect(e.text, 'Oats and berries');
        expect(e.tags, ['high_protein']);
        expect(e.portion, JournalPortion.usual);
        expect(e.source, 'typed');
        expect(e.at, DateTime.utc(2026, 11, 10, 8, 10));
        expect(e.day, DateTime(2026, 11, 10));
      });

      test('JournalTagGroup', () {
        final groups = JournalTagGroup.listFromJson(
          BodyFixtures.tagGroups(pascal: pascal),
        );
        expect(groups.first.kind, JournalKind.food);
        expect(groups.first.tags.first.tag, 'high_protein');
        expect(groups.last.tags.first.label, 'Late caffeine');
      });

      test('Medication with its ingredients', () {
        final m = Medication.fromJson(BodyFixtures.medication(pascal: pascal));
        expect(m.id, 'm1');
        expect(m.kind, MedicationKind.supplement);
        expect(m.fullName, 'Acme Daily Multi');
        expect(m.dose, '1 tablet');
        expect(m.scheduleNote, 'with breakfast');
        expect(m.generic, isFalse);
        expect(m.schedule, MedicationSchedule.daily);
        expect(m.ingredients, hasLength(2));
        expect(m.ingredients.first.name, 'Vitamin D3');
        expect(m.ingredients.first.amount, 25);
        expect(m.ingredients.first.unit, 'mcg');
        expect(m.substances, ['vitamin_d', 'zinc']);
        expect(m.startedOn, DateTime(2026, 9, 1));
        expect(m.active, isTrue);
      });

      test('StrengthLift and StrengthSet, with a half-point RPE', () {
        final lift = StrengthLift.fromJson(
          BodyFixtures.strengthLift(pascal: pascal),
        );
        expect(lift.lift, 'squat');
        expect(lift.liftName, 'Squat');
        expect(lift.sets, hasLength(2));
        expect(lift.latest?.id, 's1');
        expect(lift.latest?.rpe, 8.5);
        expect(lift.latest?.reps, 5);
        expect(lift.latest?.weightKg, 100);
        expect(lift.bestEstimatedMaxKg, 116.7);
        expect(lift.changePct, 11.1);
      });

      test('Measurement', () {
        final m = Measurement.fromJson(
          BodyFixtures.measurement(pascal: pascal),
        );
        expect(m.id, 'w1');
        expect(m.metricKey, 'weight_kg');
        expect(m.value, 82.5);
        expect(m.unit, 'kg');
        expect(m.at, DateTime.utc(2026, 11, 10, 7));
      });

      test('Progress with goals, body, series and strength', () {
        final p = Progress.fromJson(
          BodyFixtures.progress(
            pascal: pascal,
            suggestions: [BodyFixtures.suggestion()],
          ),
        );
        expect(p.day, DateTime(2026, 11, 10));
        final g = p.goals.single;
        expect(g.goal.title, 'Lose fat');
        expect(g.paceStatus, PaceStatus.onTrack);
        expect(g.fraction, 0.3);
        expect(g.actualPacePerWeek, -0.4);
        expect(g.projectedDate, DateTime(2027, 3, 15));
        expect(g.summary, contains('aiming for 80.0 kg'));
        expect(p.targets.single.target.label, 'Protein');
        expect(p.body.trendWeightKg, 84.2);
        expect(p.body.readingsSetAside, 1);
        expect(p.body.lastWeighedOn, DateTime(2026, 11, 10));
        expect(p.body.hasAnything, isTrue);
        expect(p.series.first.key, 'weight_kg');
        expect(p.series.first.points, hasLength(3));
        expect(p.series.first.trend, hasLength(3));
        expect(p.series.last.trend, isEmpty);
        expect(p.strength.single.liftName, 'Squat');
        final s = p.suggestions.single;
        expect(s.kind, GoalKind.loseFat);
        expect(s.startValue, 84);
        expect(s.targets.single.value, 135);
      });

      test('Person reads its height', () {
        final json = Fixtures.person(pascal: pascal);
        json[pascal ? 'HeightCm' : 'heightCm'] = 178.0;
        expect(Person.fromJson(json).heightCm, 178);
      });
    });
  }

  group('enums', () {
    test('read wire values and fall back for anything unknown', () {
      expect(PaceStatus.fromString('wrong_way'), PaceStatus.wrongWay);
      expect(PaceStatus.fromString('sideways'), PaceStatus.unknown);
      expect(GoalKind.fromString('keep_strength'), GoalKind.keepStrength);
      expect(GoalKind.fromString(null), GoalKind.custom);
      expect(TargetComparator.fromString('at_most'), TargetComparator.atMost);
      expect(MedicationSchedule.fromString('as_needed').wireValue, 'as_needed');
      expect(JournalPortion.fromString('huge'), isNull);
      expect(CommonLift.fromString('bench_press'), CommonLift.benchPress);
      expect(CommonLift.fromString('cable_fly'), isNull);
    });

    test('every pace status has plain words, unknown included', () {
      for (final s in PaceStatus.values) {
        expect(s.displayName, isNotEmpty);
      }
      expect(PaceStatus.unknown.isGood, isFalse);
      expect(PaceStatus.unknown.needsAttention, isFalse);
      expect(PaceStatus.wrongWay.needsAttention, isTrue);
    });
  });

  group('request bodies', () {
    test('a goal draft writes dates as days and leaves nulls in', () {
      final json = GoalDraft(
        slot: GoalSlot.secondary,
        kind: GoalKind.sleepBetter,
        startedOn: DateTime(2026, 11, 2),
      ).toJson();
      expect(json['slot'], 'secondary');
      expect(json['kind'], 'sleep_better');
      expect(json['startedOn'], '2026-11-02');
      expect(json.containsKey('targetDate'), isTrue);
    });

    test('a target draft from a target keeps it and can move it', () {
      final t = Target.fromJson(BodyFixtures.target(sortOrder: 3));
      final json = TargetDraft.fromTarget(t, sortOrder: 0).toJson();
      expect(json['label'], 'Protein');
      expect(json['metricKey'], 'protein_g');
      expect(json['comparator'], 'at_least');
      expect(json['sortOrder'], 0);
      expect(json['active'], isTrue);
    });

    test('a journal draft with only spaces is empty and sends no text', () {
      const draft = JournalDraft(kind: JournalKind.note, text: '   ');
      expect(draft.isEmpty, isTrue);
      expect(draft.toJson()['text'], isNull);
    });

    test('a medication draft carries every ingredient', () {
      final json = const MedicationDraft(
        kind: MedicationKind.supplement,
        product: 'Multi',
        ingredients: [
          MedicationIngredient(name: 'Iron', amount: 14, unit: 'mg'),
          MedicationIngredient(name: 'Biotin'),
        ],
      ).toJson();
      expect(json['ingredients'], [
        {'name': 'Iron', 'amount': 14.0, 'unit': 'mg'},
        {'name': 'Biotin', 'amount': null, 'unit': null},
      ]);
      expect(json['schedule'], 'daily');
    });

    test('the profile update sends 0 to clear the height', () {
      final person = Person.fromJson(Fixtures.person());
      expect(person.toUpdateJson()['heightCm'], 0);
      expect(person.copyWith(heightCm: 172).toUpdateJson()['heightCm'], 172);
    });
  });
}
