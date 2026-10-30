import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/setup.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  Future<void> signIn({
    String role = 'admin',
    List<String> plan = const [],
    bool done = false,
  }) async {
    h.api.on(
      'POST',
      '/auth/login',
      (_) =>
          Fixtures.authResponse(role: role, setupPlan: plan, setupDone: done),
    );
    await h.auth.login(email: 'a@b', password: 'x');
  }

  /// Answers PUT /me/setup like the server: the account with the new answers.
  void answerSetup() {
    h.api.on('PUT', '/me/setup', (data) {
      final body = data! as Map<String, dynamic>;
      return Fixtures.user(
        setupPlan: [for (final p in body['plan'] as List) p as String],
        setupDone: body['completed'] as bool,
      );
    });
  }

  setUp(() => h = TestHarness());
  tearDown(() => h.dispose());

  group('SetupProvider', () {
    test('seed starts from the suggestion the first time', () async {
      await signIn();
      h.setup.seed(suggested: [SetupItem.healthConnect]);
      expect(h.setup.chosen, [SetupItem.healthConnect]);
      h.setup.seed(suggested: [SetupItem.appleHealth]);
      expect(h.setup.chosen, [SetupItem.healthConnect], reason: 'seeds once');
    });

    test('seed prefers what the account already saved', () async {
      await signIn(plan: ['labs', 'oura']);
      h.setup.seed(suggested: [SetupItem.healthConnect]);
      expect(h.setup.chosen, [SetupItem.oura, SetupItem.labs]);
    });

    test('toggle adds and removes; chosen keeps the question order', () async {
      await signIn();
      h.setup.seed();
      h.setup.toggle(SetupItem.assistant);
      h.setup.toggle(SetupItem.appleHealth);
      expect(h.setup.chosen, [SetupItem.appleHealth, SetupItem.assistant]);
      h.setup.toggle(SetupItem.assistant);
      expect(h.setup.isChosen(SetupItem.assistant), isFalse);
    });

    test(
      'saveAnswers sends the draft unfinished and updates the account',
      () async {
        await signIn();
        answerSetup();
        h.setup.seed();
        h.setup.toggle(SetupItem.oura);
        expect(await h.setup.saveAnswers(), isTrue);
        final i = h.api.calls.indexOf('PUT /me/setup');
        expect(h.api.bodies[i], {
          'plan': ['oura'],
          'completed': false,
        });
        expect(h.auth.user!.setupPlan, [SetupItem.oura]);
        expect(h.auth.user!.setupPending, isTrue);
      },
    );

    test('finish sends the saved answers, not a stale draft', () async {
      await signIn(plan: ['labs', 'assistant']);
      answerSetup();
      expect(await h.setup.finish(), isTrue);
      final i = h.api.calls.lastIndexOf('PUT /me/setup');
      expect(h.api.bodies[i], {
        'plan': ['labs', 'assistant'],
        'completed': true,
      });
      expect(h.auth.user!.setupPending, isFalse);
    });

    test('skip finishes with no answers', () async {
      await signIn(plan: ['oura']);
      answerSetup();
      h.setup.seed();
      expect(await h.setup.skip(), isTrue);
      expect(h.api.bodies.last, {'plan': <String>[], 'completed': true});
      expect(h.setup.chosen, isEmpty);
      expect(h.auth.user!.setupPending, isFalse);
    });

    test(
      'a failure is kept for the page and the account is unchanged',
      () async {
        await signIn();
        h.api.fail(
          'PUT',
          '/me/setup',
          const ApiException('The server had a problem.', statusCode: 500),
        );
        h.setup.seed();
        h.setup.toggle(SetupItem.labs);
        expect(await h.setup.saveAnswers(), isFalse);
        expect(h.setup.error, 'The server had a problem.');
        expect(h.setup.saving, isFalse);
        expect(h.auth.user!.setupPlan, isEmpty);
      },
    );

    test('signing out forgets the draft', () async {
      await signIn();
      h.setup.seed();
      h.setup.toggle(SetupItem.labs);
      h.api.on('POST', '/auth/logout', (_) => null);
      await h.auth.logout();
      expect(h.setup.chosen, isEmpty);
    });
  });

  group('OuraAppProvider', () {
    test('a member never asks for the application', () async {
      await signIn(role: 'member');
      await h.ouraApp.load();
      expect(h.ouraApp.app, isNull);
      expect(h.api.calls, isNot(contains('GET /integrations/oura')));
    });

    test('the admin loads, saves and clears it', () async {
      await signIn();
      h.api.on('GET', '/integrations/oura', (_) => Fixtures.ouraApp());
      h.api.on(
        'PUT',
        '/integrations/oura',
        (_) => Fixtures.ouraApp(configured: true, source: 'app', clientId: 'a'),
      );
      h.api.on('DELETE', '/integrations/oura', (_) => Fixtures.ouraApp());

      await h.ouraApp.load();
      expect(h.ouraApp.app!.configured, isFalse);

      expect(await h.ouraApp.save(clientId: 'a', clientSecret: 'b'), isTrue);
      expect(h.ouraApp.app!.configured, isTrue);
      expect(h.ouraApp.app!.clientId, 'a');
      expect(h.api.bodies.last, {'clientId': 'a', 'clientSecret': 'b'});

      expect(await h.ouraApp.clear(), isTrue);
      expect(h.ouraApp.app!.configured, isFalse);
    });

    test('a refused save keeps the message and the old state', () async {
      await signIn();
      h.api.on('GET', '/integrations/oura', (_) => Fixtures.ouraApp());
      h.api.fail(
        'PUT',
        '/integrations/oura',
        const ApiException('The client id is one word.', statusCode: 400),
      );
      await h.ouraApp.load();
      expect(await h.ouraApp.save(clientId: 'a b', clientSecret: 'c'), isFalse);
      expect(h.ouraApp.error, 'The client id is one word.');
      expect(h.ouraApp.app!.configured, isFalse);
      expect(h.ouraApp.saving, isFalse);
    });
  });
}
