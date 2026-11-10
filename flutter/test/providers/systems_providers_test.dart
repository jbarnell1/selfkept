import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/context.dart';
import 'package:selfkept/models/trial.dart';
import 'package:selfkept/providers/auth_provider.dart';
import 'package:selfkept/providers/context_provider.dart';
import 'package:selfkept/providers/systems_provider.dart';
import 'package:selfkept/providers/trials_provider.dart';
import 'package:selfkept/providers/viewed_person.dart';
import 'package:selfkept/services/context_service.dart';
import 'package:selfkept/services/insights_service.dart';
import 'package:selfkept/services/review_service.dart';
import 'package:selfkept/services/systems_service.dart';
import 'package:selfkept/services/trials_service.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/systems_fixtures.dart';

const _notShared = ApiException(
  'No such person, or they have not shared this with you.',
  statusCode: 404,
);

void main() {
  late FakeApiClient api;
  late AuthProvider auth;
  late ViewedPerson viewing;

  setUp(() async {
    api = FakeApiClient();
    api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    auth = AuthProvider(api);
    await auth.login(email: 'a@b', password: 'x');
    viewing = ViewedPerson(auth: auth);
  });

  tearDown(() {
    viewing.dispose();
    auth.dispose();
  });

  group('SystemsProvider', () {
    late SystemsProvider provider;

    setUp(() {
      provider = SystemsProvider(
        service: SystemsService(api),
        auth: auth,
        viewing: viewing,
      );
    });
    tearDown(() => provider.dispose());

    test('loads your own systems without a personId', () async {
      api.on('GET', '/systems', (_) => SystemsFixtures.systems());
      await provider.load();
      expect(provider.hasLoaded, isTrue);
      expect(provider.data?.systems, hasLength(2));
      expect(lastQuery(api, 'GET /systems'), isEmpty);
      expect(provider.summary('HEART')?.name, 'Heart and fitness');
    });

    test('a 404 for someone else is not shared, not an error', () async {
      api.on('GET', '/systems', (_) => SystemsFixtures.systems());
      await provider.load();
      api.fail('GET', '/systems', _notShared);
      viewing.view('p2', displayName: 'Charles');
      await pumpEventQueue();
      expect(lastQuery(api, 'GET /systems'), {'personId': 'p2'});
      expect(provider.notShared, isTrue);
      expect(provider.error, isNull);
      expect(provider.data, isNull);
      expect(provider.viewedName, 'Charles');
    });

    test('other failures are errors', () async {
      api.fail('GET', '/systems', const ApiException('Down', statusCode: 500));
      await provider.load();
      expect(provider.notShared, isFalse);
      expect(provider.error, 'Down');
    });

    test('a detail loads by key and a 404 marks it not shared', () async {
      api.on('GET', '/systems/heart', (_) => SystemsFixtures.system());
      await provider.loadSystem('Heart');
      expect(provider.detail('heart')?.readings, hasLength(1));
      api.fail('GET', '/systems/cycle', _notShared);
      await provider.loadSystem('cycle');
      expect(provider.detailNotShared('cycle'), isTrue);
      expect(provider.detailError('cycle'), isNull);
    });

    test('sign-out forgets the record', () async {
      api.on('GET', '/systems', (_) => SystemsFixtures.systems());
      api.on('POST', '/auth/logout', (_) => null);
      await provider.load();
      await auth.logout();
      expect(provider.data, isNull);
      expect(provider.hasLoaded, isFalse);
    });
  });

  group('ContextProvider', () {
    late ContextProvider provider;

    setUp(() {
      provider = ContextProvider(
        service: ContextService(api),
        auth: auth,
        viewing: viewing,
      );
      api.on('GET', '/context', (_) => SystemsFixtures.context());
    });
    tearDown(() => provider.dispose());

    test(
      'adds, ends on the server day and removes, reloading each time',
      () async {
        api.on('POST', '/context', (_) => SystemsFixtures.event(id: 'e2'));
        api.on('PATCH', '/context/e1', (_) => SystemsFixtures.event());
        api.on('DELETE', '/context/e1', (_) => null);
        await provider.load();

        final added = await provider.add(
          const ContextDraft(kind: ContextKind.travel, note: 'Lisbon'),
        );
        expect(added?.id, 'e2');
        expect(lastQuery(api, 'POST /context'), {
          'kind': 'travel',
          'note': 'Lisbon',
        });

        await provider.end(provider.data!.events.single);
        expect(lastQuery(api, 'PATCH /context/e1'), {'endedOn': '2026-11-10'});

        expect(await provider.remove('e1'), isTrue);
        expect(api.calls.where((c) => c == 'GET /context').length, 4);
      },
    );

    test('a refused write keeps the server words', () async {
      api.fail(
        'POST',
        '/context',
        const ApiException(
          'startedOn cannot be more than 60 days ahead.',
          statusCode: 400,
        ),
      );
      await provider.load();
      final saved = await provider.add(
        const ContextDraft(kind: ContextKind.illness),
      );
      expect(saved, isNull);
      expect(
        provider.actionError,
        'startedOn cannot be more than 60 days ahead.',
      );
    });

    test('writes are blocked while viewing someone else', () async {
      viewing.view('p2', displayName: 'Charles');
      await provider.load();
      expect(provider.canEdit, isFalse);
      expect(
        await provider.add(const ContextDraft(kind: ContextKind.illness)),
        isNull,
      );
      expect(await provider.remove('e1'), isFalse);
      expect(await provider.reopen('e1'), isNull);
      expect(
        api.calls.where((c) => !c.startsWith('GET') && !c.contains('auth')),
        isEmpty,
      );
    });

    test('not shared for someone else', () async {
      api.fail('GET', '/context', _notShared);
      viewing.view('p2');
      await provider.load();
      expect(provider.notShared, isTrue);
    });
  });

  group('TrialsProvider', () {
    late TrialsProvider provider;

    setUp(() {
      provider = TrialsProvider(
        service: TrialsService(api),
        insights: InsightsService(api),
        now: () => DateTime(2026, 11, 10),
        auth: auth,
        viewing: viewing,
      );
      api.on(
        'GET',
        '/trials',
        (_) => [
          SystemsFixtures.trial(),
          SystemsFixtures.trial(id: 'tr2', title: 'No coffee', status: 'done'),
        ],
      );
    });
    tearDown(() => provider.dispose());

    test('splits active and ended and finds the card naming a trial', () async {
      api.on(
        'GET',
        '/insights',
        (_) => [
          SystemsFixtures.insight(
            kind: 'trial_ready',
            severity: 'notice',
            text: 'The trial you started on Aug 16 has reached its read date.',
            evidence: {'trialId': 'tr1', 'reportId': 'r2'},
          ),
          SystemsFixtures.insight(id: 'i2'),
        ],
      );
      await provider.load();
      final state = provider.data!;
      expect(state.active.single.id, 'tr1');
      expect(state.ended.single.status, TrialStatus.done);
      expect(state.cards['tr1']?.text, contains('read date'));
      expect(state.cards, hasLength(1));
      expect(lastQuery(api, 'GET /trials'), {'includeEnded': true});
    });

    test('without the cards the trials still show', () async {
      api.fail('GET', '/insights', _notShared);
      await provider.load();
      expect(provider.data?.trials, hasLength(2));
      expect(provider.data?.cards, isEmpty);
    });

    test('finishing sends the status and the words', () async {
      api.on('GET', '/insights', (_) => <Map<String, dynamic>>[]);
      api.on(
        'PATCH',
        '/trials/tr1',
        (_) => SystemsFixtures.trial(status: 'done'),
      );
      await provider.load();
      await provider.finish(
        provider.data!.active.single,
        TrialStatus.done,
        outcome: ' LDL came down ',
      );
      expect(lastQuery(api, 'PATCH /trials/tr1'), {
        'status': 'done',
        'outcome': 'LDL came down',
      });
    });

    test('writes are blocked while viewing someone else', () async {
      api.on('GET', '/insights', (_) => <Map<String, dynamic>>[]);
      viewing.view('p2');
      await provider.load();
      final created = await provider.create(
        TrialDraft(title: 'x', readOn: DateTime(2027)),
      );
      expect(created, isNull);
      expect(await provider.remove('tr1'), isFalse);
      expect(api.calls, isNot(contains('POST /trials')));
      expect(api.calls, isNot(contains('DELETE /trials/tr1')));
      expect(lastQuery(api, 'GET /trials')?['personId'], 'p2');
    });
  });

  group('WeeklyReviewProvider', () {
    late WeeklyReviewProvider provider;

    setUp(() {
      provider = WeeklyReviewProvider(
        service: ReviewService(api),
        auth: auth,
        viewing: viewing,
      );
    });
    tearDown(() => provider.dispose());

    test('steps back a week and forward only up to today', () async {
      api.on('GET', '/review/weekly', (q) {
        final weekOf = (q as Map?)?['weekOf'] as String?;
        return weekOf == '2026-10-26'
            ? SystemsFixtures.review(
                weekOf: '2026-10-26',
                weekEnd: '2026-11-01',
              )
            : SystemsFixtures.review();
      });
      await provider.load();
      expect(lastQuery(api, 'GET /review/weekly'), isEmpty);
      expect(provider.canGoForward(DateTime(2026, 11, 10)), isTrue);
      expect(provider.canGoForward(DateTime(2026, 11, 8)), isFalse);

      await provider.previousWeek();
      expect(lastQuery(api, 'GET /review/weekly'), {'weekOf': '2026-10-26'});
      expect(provider.data?.weekOf, DateTime(2026, 10, 26));

      await provider.nextWeek();
      expect(lastQuery(api, 'GET /review/weekly'), {'weekOf': '2026-11-02'});
    });

    test('nothing shared at all is a 404', () async {
      api.fail('GET', '/review/weekly', _notShared);
      viewing.view('p2');
      await provider.load();
      expect(provider.notShared, isTrue);
    });
  });
}
