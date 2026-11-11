import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/goal.dart';
import 'package:selfkept/models/journal.dart';
import 'package:selfkept/models/medication.dart';
import 'package:selfkept/models/progress.dart';
import 'package:selfkept/models/strength.dart';
import 'package:selfkept/models/target.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/body_fixtures.dart';
import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

const _notFound = ApiException(
  'No such person, or they have not shared this with you.',
  statusCode: 404,
);
const _serverDown = ApiException('The server had a problem.', statusCode: 500);

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
  });
  tearDown(() => h.dispose());

  Map<String, dynamic>? lastQuery(String call) {
    final i = h.api.calls.lastIndexOf(call);
    return i < 0 ? null : h.api.bodies[i] as Map<String, dynamic>?;
  }

  group('TallyProvider', () {
    test('loads the tally', () async {
      h.api.on('GET', '/targets/tally', (_) => BodyFixtures.tally());
      await h.tally.load();
      expect(h.tally.hasLoaded, isTrue);
      expect(h.tally.tally?.targets.single.target.label, 'Protein');
      expect(h.tally.error, isNull);
    });

    test('a failure is kept as an error and nothing is shown', () async {
      h.api.fail('GET', '/targets/tally', _serverDown);
      await h.tally.load();
      expect(h.tally.hasLoaded, isFalse);
      expect(h.tally.tally, isNull);
      expect(h.tally.error, 'The server had a problem.');
    });

    test('reloads when the day log changes', () async {
      h.api.on('GET', '/targets/tally', (_) => BodyFixtures.tally());
      h.api.on('POST', '/journal', (_) => BodyFixtures.journalEntry());
      await h.tally.load();
      await h.journal.add(
        const JournalDraft(kind: JournalKind.food, tags: ['high_protein']),
      );
      await Future<void>.delayed(Duration.zero);
      expect(h.api.calls.where((c) => c == 'GET /targets/tally'), hasLength(2));
    });
  });

  group('JournalProvider', () {
    setUp(() {
      h.api.on('GET', '/journal', (_) => [BodyFixtures.journalEntry()]);
      h.api.on('GET', '/journal/tags', (_) => BodyFixtures.tagGroups());
    });

    test('loads today only, and the tags once', () async {
      await h.journal.load();
      expect(h.journal.entries.single.id, 'j1');
      expect(lastQuery('GET /journal'), {
        'from': '2026-11-10',
        'to': '2026-11-10',
      });
      expect(h.journal.tagsFor(JournalKind.food), hasLength(2));
      expect(h.journal.tagsFor(JournalKind.note), isEmpty);
      expect(h.journal.labelFor('late_caffeine'), 'Late caffeine');
      expect(h.journal.labelFor('my_own_tag'), 'My own tag');
      await h.journal.load();
      expect(h.api.calls.where((c) => c == 'GET /journal/tags'), hasLength(1));
    });

    test('add puts the entry on top at once, and Undo deletes it', () async {
      await h.journal.load();
      h.api.on(
        'POST',
        '/journal',
        (_) => BodyFixtures.journalEntry(id: 'j2', kind: 'drink', text: null),
      );
      h.api.on('DELETE', '/journal/j2', (_) => null);
      final entry = await h.journal.add(
        const JournalDraft(kind: JournalKind.drink, tags: ['alcohol']),
      );
      expect(entry?.id, 'j2');
      expect(h.journal.entries.map((e) => e.id), ['j2', 'j1']);
      expect(h.api.bodies.last, {
        'kind': 'drink',
        'text': null,
        'tags': ['alcohol'],
        'portion': null,
      });

      expect(await h.journal.remove('j2'), isTrue);
      expect(h.api.calls.last, 'DELETE /journal/j2');
      expect(h.journal.entries.map((e) => e.id), ['j1']);
    });

    test('a refused save says why and adds nothing', () async {
      await h.journal.load();
      h.api.fail(
        'POST',
        '/journal',
        const ApiException('Nothing to save.', statusCode: 400),
      );
      final entry = await h.journal.add(
        const JournalDraft(kind: JournalKind.note, text: 'x'),
      );
      expect(entry, isNull);
      expect(h.journal.saveError, 'Nothing to save.');
      expect(h.journal.entries, hasLength(1));
    });

    test('a failed load is an error', () async {
      h.api.fail('GET', '/journal', _serverDown);
      await h.journal.load();
      expect(h.journal.hasLoaded, isFalse);
      expect(h.journal.error, 'The server had a problem.');
    });
  });

  group('ProgressProvider', () {
    setUp(() {
      h.api.on('GET', '/progress', (_) => BodyFixtures.progress());
    });

    test('loads Progress for the signed in person', () async {
      await h.progress.load();
      expect(h.progress.hasLoaded, isTrue);
      expect(h.progress.progress?.goals.single.paceStatus, PaceStatus.onTrack);
      expect(h.progress.canEdit, isTrue);
      expect(lastQuery('GET /progress'), isEmpty);
    });

    test('a 404 for someone else is the not shared state', () async {
      h.viewing.view('p2', displayName: 'Sam');
      h.api.fail('GET', '/progress', _notFound);
      await h.progress.load();
      expect(h.progress.notShared, isTrue);
      expect(h.progress.error, isNull);
      expect(h.progress.progress, isNull);
    });

    test('a 404 on your own record is an error, never not shared', () async {
      h.api.fail('GET', '/progress', _notFound);
      await h.progress.load();
      expect(h.progress.notShared, isFalse);
      expect(h.progress.error, missingOnServerMessage);
      expect(h.progress.progress, isNull);
    });

    test('a failed page loads again when the record changes', () async {
      h.api.fail('GET', '/progress', _notFound);
      await h.progress.load();
      expect(h.progress.error, isNotNull);
      h.api.on('GET', '/progress', (_) => BodyFixtures.progress());
      h.recordChanges.changed(h.startup);
      await pumpEventQueue();
      expect(h.progress.error, isNull);
      expect(h.progress.hasLoaded, isTrue);
    });

    test('another failure is an error', () async {
      h.api.fail('GET', '/progress', _serverDown);
      await h.progress.load();
      expect(h.progress.notShared, isFalse);
      expect(h.progress.error, 'The server had a problem.');
    });

    test(
      'viewing someone else reads with their id and blocks writes',
      () async {
        await h.progress.load();
        h.viewing.view('p2', displayName: 'Sam');
        await Future<void>.delayed(Duration.zero);
        await pumpEventQueue();
        expect(lastQuery('GET /progress'), {'personId': 'p2'});
        expect(h.progress.canEdit, isFalse);
        final saved = await h.progress.addMeasurement(BodyMeasure.weight, 80);
        expect(saved, isNull);
        expect(h.api.calls, isNot(contains('POST /body/measurements')));

        h.viewing.view('p1');
        expect(h.viewing.isSelf, isTrue, reason: 'your own id is yours');
      },
    );

    test('a reading is saved metric and can be undone', () async {
      await h.progress.load();
      h.api.on('POST', '/body/measurements', (_) => BodyFixtures.measurement());
      h.api.on('DELETE', '/body/measurements/weight_kg/w1', (_) => null);
      final saved = await h.progress.addMeasurement(BodyMeasure.weight, 82.5);
      expect(saved?.id, 'w1');
      final body =
          h.api.bodies[h.api.calls.indexOf('POST /body/measurements')]
              as Map<String, dynamic>;
      expect(body['metricKey'], 'weight_kg');
      expect(body['value'], 82.5);

      expect(
        await h.progress.deleteMeasurement(BodyMeasure.weight, 'w1'),
        isTrue,
      );
      expect(h.api.calls, contains('DELETE /body/measurements/weight_kg/w1'));
    });

    test('a refused set says why', () async {
      await h.progress.load();
      h.api.fail(
        'POST',
        '/strength',
        const ApiException('reps must be between 1 and 100.', statusCode: 400),
      );
      final saved = await h.progress.addStrengthSet(
        const StrengthSetDraft(lift: 'squat', weightKg: 100, reps: 0),
      );
      expect(saved, isNull);
      expect(h.progress.actionError, 'reps must be between 1 and 100.');
    });

    test('a set saves and its undo deletes it', () async {
      await h.progress.load();
      h.api.on('POST', '/strength', (_) => BodyFixtures.strengthSet());
      h.api.on('DELETE', '/strength/s1', (_) => null);
      final saved = await h.progress.addStrengthSet(
        const StrengthSetDraft(lift: 'squat', weightKg: 100, reps: 5, rpe: 8),
      );
      expect(saved?.id, 's1');
      expect(await h.progress.deleteStrengthSet('s1'), isTrue);
    });

    test('starting a suggestion creates the goal, then its targets', () async {
      h.api.on(
        'GET',
        '/progress',
        (_) => BodyFixtures.progress(
          goals: const [],
          suggestions: [BodyFixtures.suggestion()],
        ),
      );
      await h.progress.load();
      h.api.on('POST', '/goals', (_) => BodyFixtures.goal(id: 'g9'));
      h.api.on('POST', '/targets', (_) => BodyFixtures.target());
      final ok = await h.progress.startGoal(
        h.progress.progress!.suggestions.single,
      );
      expect(ok, isTrue);
      final goalBody =
          h.api.bodies[h.api.calls.indexOf('POST /goals')]
              as Map<String, dynamic>;
      expect(goalBody['slot'], 'main');
      expect(goalBody['startValue'], 84);
      final targetBody =
          h.api.bodies[h.api.calls.indexOf('POST /targets')]
              as Map<String, dynamic>;
      expect(targetBody['goalId'], 'g9');
      expect(targetBody['metricKey'], 'protein_g');
    });
  });

  group('GoalsProvider', () {
    setUp(() {
      h.api.on('GET', '/goals', (_) => [BodyFixtures.goal()]);
      h.api.on(
        'GET',
        '/targets',
        (_) => [
          BodyFixtures.target(id: 't2', label: 'Sleep', sortOrder: 1),
          BodyFixtures.target(sortOrder: 0),
          BodyFixtures.target(
            id: 't3',
            goalId: null,
            label: 'Lifting days',
            kind: 'strength',
            metricKey: null,
            value: 2,
            period: 'week',
            unit: 'days',
            sortOrder: 2,
          ),
        ],
      );
    });

    test('loads goals and targets in sort order', () async {
      await h.goals.load();
      expect(h.goals.goalIn(GoalSlot.main)?.id, 'g1');
      expect(h.goals.goalIn(GoalSlot.secondary), isNull);
      expect(h.goals.targets.map((t) => t.id), ['t1', 't2', 't3']);
      expect(h.goals.standaloneTargets.single.id, 't3');
    });

    test('moving a target saves the new order', () async {
      await h.goals.load();
      h.api.on('PUT', '/targets/t3', (_) => BodyFixtures.target(id: 't3'));
      h.api.on('PUT', '/targets/t2', (_) => BodyFixtures.target(id: 't2'));
      expect(await h.goals.moveTarget('t3', -1), isTrue);
      final t3 =
          h.api.bodies[h.api.calls.indexOf('PUT /targets/t3')]
              as Map<String, dynamic>;
      final t2 =
          h.api.bodies[h.api.calls.indexOf('PUT /targets/t2')]
              as Map<String, dynamic>;
      expect(t3['sortOrder'], 1);
      expect(t2['sortOrder'], 2);
      expect(h.api.calls, isNot(contains('PUT /targets/t1')));
    });

    test('ending a goal posts the status', () async {
      await h.goals.load();
      h.api.on(
        'POST',
        '/goals/g1/end',
        (_) => BodyFixtures.goal(status: 'done'),
      );
      expect(await h.goals.endGoal('g1', GoalStatus.done), isTrue);
      expect(h.api.bodies[h.api.calls.indexOf('POST /goals/g1/end')], {
        'status': 'done',
      });
    });

    test('a refused save keeps the reason', () async {
      await h.goals.load();
      h.api.fail(
        'POST',
        '/targets',
        const ApiException(
          'At most 12 targets. Remove one first.',
          statusCode: 400,
        ),
      );
      final ok = await h.goals.saveTarget(
        const TargetDraft(
          label: 'Steps',
          kind: TargetKind.metric,
          metricKey: 'steps',
          comparator: TargetComparator.atLeast,
          value: 8000,
          period: TargetPeriod.day,
        ),
      );
      expect(ok, isFalse);
      expect(h.goals.saveError, 'At most 12 targets. Remove one first.');
    });

    test('a 404 on your own goals is an error, never not shared', () async {
      h.api.fail('GET', '/goals', _notFound);
      await h.goals.load();
      expect(h.goals.notShared, isFalse);
      expect(h.goals.error, missingOnServerMessage);
    });

    test('a 404 for someone else is the not shared state', () async {
      h.viewing.view('p2', displayName: 'Sam');
      h.api.fail('GET', '/goals', _notFound);
      await h.goals.load();
      expect(h.goals.notShared, isTrue);
      expect(h.goals.canEdit, isFalse);
      expect(lastQuery('GET /goals'), {'personId': 'p2'});
    });

    test('a failed load is an error', () async {
      h.api.fail('GET', '/targets', _serverDown);
      await h.goals.load();
      expect(h.goals.hasLoaded, isFalse);
      expect(h.goals.error, 'The server had a problem.');
    });
  });

  group('MedicationsProvider', () {
    setUp(() {
      h.api.on(
        'GET',
        '/medications',
        (_) => [
          BodyFixtures.medication(),
          BodyFixtures.medication(
            id: 'm2',
            kind: 'medication',
            brand: null,
            product: 'Atorvastatin',
            active: false,
            stoppedOn: '2026-10-01',
            substances: ['statin'],
          ),
        ],
      );
    });

    test('splits the list into current and stopped', () async {
      await h.medications.load();
      expect(h.medications.current.single.id, 'm1');
      expect(h.medications.stopped.single.product, 'Atorvastatin');
    });

    test('a save posts the draft and reads the list again', () async {
      await h.medications.load();
      h.api.on('POST', '/medications', (_) => BodyFixtures.medication());
      final ok = await h.medications.save(
        const MedicationDraft(
          kind: MedicationKind.supplement,
          product: 'Magnesium',
          ingredients: [
            MedicationIngredient(name: 'Magnesium', amount: 200, unit: 'mg'),
          ],
        ),
      );
      expect(ok, isTrue);
      expect(h.api.calls.where((c) => c == 'GET /medications'), hasLength(2));
    });

    test('a refused save says why', () async {
      await h.medications.load();
      h.api.fail(
        'PUT',
        '/medications/m1',
        const ApiException('Every ingredient needs a name.', statusCode: 400),
      );
      final ok = await h.medications.save(
        const MedicationDraft(kind: MedicationKind.supplement, product: 'x'),
        id: 'm1',
      );
      expect(ok, isFalse);
      expect(h.medications.saveError, 'Every ingredient needs a name.');
    });

    test('remove deletes and drops the item', () async {
      await h.medications.load();
      h.api.on('DELETE', '/medications/m2', (_) => null);
      expect(await h.medications.remove('m2'), isTrue);
      expect(h.medications.items.map((m) => m.id), ['m1']);
    });

    test('loading: nothing until the list answers, then the error', () async {
      final pending = Completer<List<Map<String, dynamic>>>();
      h.api.on('GET', '/medications', (_) => pending.future);
      final load = h.medications.load();
      expect(h.medications.isLoading, isTrue);
      pending.completeError(_serverDown);
      await load;
      expect(h.medications.isLoading, isFalse);
      expect(h.medications.error, 'The server had a problem.');
    });

    test('switching person reloads with their id', () async {
      await h.medications.load();
      h.viewing.view('p2', displayName: 'Sam');
      await pumpEventQueue();
      expect(lastQuery('GET /medications'), {'personId': 'p2'});
      expect(h.medications.canEdit, isFalse);
      expect(await h.medications.remove('m1'), isFalse);
    });
  });
}
