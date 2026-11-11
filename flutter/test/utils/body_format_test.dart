import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/enums.dart';
import 'package:selfkept/models/goal.dart';
import 'package:selfkept/models/target.dart';
import 'package:selfkept/utils/body_units.dart';
import 'package:selfkept/utils/goal_format.dart';
import 'package:selfkept/utils/target_format.dart';

import '../helpers/body_fixtures.dart';

void main() {
  group('BodyUnits', () {
    test('mass and length both ways', () {
      expect(BodyUnits.formatMass(80, Units.imperial), '176.4 lb');
      expect(BodyUnits.massToKg(176.37, Units.imperial), closeTo(80, 0.01));
      expect(BodyUnits.formatLength(90, Units.imperial), '35.4 in');
      expect(BodyUnits.lengthToCm(10, Units.imperial), 25.4);
      expect(BodyUnits.formatMassChange(-0.4, Units.metric), '-0.4 kg');
      expect(BodyUnits.formatMassChange(0.01, Units.metric), '0.0 kg');
    });

    test('height in cm or feet and inches', () {
      expect(BodyUnits.formatHeight(178, Units.metric), '178 cm');
      expect(BodyUnits.formatHeight(178, Units.imperial), '5 ft 10 in');
      expect(BodyUnits.feetAndInchesToCm(5, 10), closeTo(177.8, 0.001));
    });

    test('reads what people type', () {
      expect(BodyUnits.parse(' 82,5 '), 82.5);
      expect(BodyUnits.parse('abc'), isNull);
      expect(BodyUnits.parse(''), isNull);
      expect(BodyUnits.forField(82.0), '82');
      expect(BodyUnits.forField(82.25, decimals: 2), '82.25');
    });

    test('stored values pass through unless kg or cm', () {
      expect(BodyUnits.formatStored(24, '%', Units.imperial), '24.0%');
      expect(BodyUnits.formatStored(90, 'cm', Units.metric), '90.0 cm');
    });
  });

  group('GoalFormat', () {
    test('each kind has its usual metric', () {
      expect(GoalFormat.defaultMetricKey(GoalKind.loseFat), 'weight_kg');
      expect(
        GoalFormat.defaultMetricKey(GoalKind.recomposition),
        'body_fat_pct',
      );
      expect(GoalFormat.defaultMetricKey(GoalKind.sleepBetter), 'sleep_stage');
      expect(GoalFormat.defaultMetricKey(GoalKind.keepStrength), isNull);
    });

    test('a pace reads as an amount and a direction', () {
      expect(GoalFormat.pace(-0.4, 'kg', Units.metric), '0.4 kg down a week');
      expect(GoalFormat.pace(0.2, 'kg', Units.imperial), '0.4 lb up a week');
      expect(GoalFormat.pace(0.01, 'kg', Units.metric), 'Level');
      expect(GoalFormat.pace(15, 'min', Units.metric), '15 min up a week');
    });

    test('sleep goals are typed in hours and stored in minutes', () {
      expect(GoalFormat.fromField(7.5, 'min', Units.metric), 450);
      expect(GoalFormat.toField(450, 'min', Units.metric), 7.5);
      expect(GoalFormat.fieldUnit('min', Units.metric), 'hours');
      expect(GoalFormat.fieldUnit('kg', Units.imperial), 'lb');
      expect(
        GoalFormat.fromField(176.37, 'kg', Units.imperial),
        closeTo(80, 0.01),
      );
    });

    test('a goal reads its own metric or the usual one', () {
      final g = Goal.fromJson(BodyFixtures.goal(metricKey: null));
      expect(GoalFormat.metricOf(g)?.key, 'weight_kg');
      final w = Goal.fromJson(BodyFixtures.goal(metricKey: 'waist_cm'));
      expect(GoalFormat.metricOf(w)?.unit, 'cm');
    });
  });

  group('TargetFormat', () {
    Target target(Map<String, dynamic> json) => Target.fromJson(json);

    test('a rule in words', () {
      final t = target(BodyFixtures.target());
      expect(TargetFormat.rule(t, Units.metric), 'At least 130 g a day');
      expect(TargetFormat.standing(t, 86, Units.metric), '86 / 130 g');
      expect(TargetFormat.standing(t, null, Units.metric), '130 g');
    });

    test('a limit of zero reads as none', () {
      for (final period in ['week', 'day']) {
        final t = target(
          BodyFixtures.target(
            metricKey: null,
            comparator: 'at_most',
            value: 0,
            unit: 'entries',
            period: period,
          ),
        );
        expect(TargetFormat.rule(t, Units.metric), 'None in a $period');
        expect(TargetFormat.standing(t, null, Units.metric), 'None so far');
        expect(TargetFormat.standing(t, 0, Units.metric), 'None so far');
        expect(
          TargetFormat.standing(t, 2, Units.metric),
          '2 logged, aiming for none',
        );
      }
    });

    test('sleep reads in hours and steps have no unit', () {
      final sleep = target(
        BodyFixtures.target(metricKey: 'sleep_stage', value: 420, unit: 'min'),
      );
      expect(TargetFormat.withUnit(sleep, 450, Units.metric), '7 h 30');
      final steps = target(
        BodyFixtures.target(metricKey: 'steps', value: 8000, unit: 'count'),
      );
      expect(TargetFormat.withUnit(steps, 8000, Units.metric), '8,000');
    });

    test('a weight target is shown in pounds for an imperial reader', () {
      final w = target(
        BodyFixtures.target(
          metricKey: 'weight_kg',
          comparator: 'at_most',
          value: 80,
          unit: 'kg',
          period: 'week',
        ),
      );
      expect(TargetFormat.rule(w, Units.imperial), 'At most 176.4 lb a week');
    });
  });
}
