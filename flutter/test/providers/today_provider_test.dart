import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/insight.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
  });
  tearDown(() => h.dispose());

  test('load reads the sentence, readiness and two cards', () async {
    h.api.on('GET', '/today', (_) => Fixtures.today());
    expect(h.today.hasLoaded, isFalse);
    await h.today.load();
    expect(h.today.hasLoaded, isTrue);
    expect(h.today.error, isNull);
    expect(h.today.today!.sentence, startsWith('You slept'));
    expect(h.today.today!.sleepMinutes, 460);
    expect(h.today.cards.map((c) => c.id), ['i1', 'i2']);
  });

  test('a failed load keeps the message and any earlier data', () async {
    h.api.on('GET', '/today', (_) => Fixtures.today());
    await h.today.load();
    h.api.fail(
      'GET',
      '/today',
      const ApiException('The server could not be reached.', isNetwork: true),
    );
    await h.today.load();
    expect(h.today.error, 'The server could not be reached.');
    expect(h.today.today, isNotNull);
    expect(h.today.hasLoaded, isTrue);
  });

  test('isLoading is true while the request is in flight', () async {
    final completer = Completer<Map<String, dynamic>>();
    h.api.on('GET', '/today', (_) => completer.future);
    final future = h.today.load();
    expect(h.today.isLoading, isTrue);
    completer.complete(Fixtures.today());
    await future;
    expect(h.today.isLoading, isFalse);
  });

  test('a kept check-in reloads Today', () async {
    h.api.on('GET', '/today', (_) => Fixtures.today());
    h.api.on('POST', '/checkins', (_) => Fixtures.checkin(id: 'c-new'));
    await h.today.load();
    final before = h.api.calls.where((c) => c == 'GET /today').length;
    await h.checkins.create(mood: 5, note: 'walked');
    await Future<void>.delayed(Duration.zero);
    final after = h.api.calls.where((c) => c == 'GET /today').length;
    expect(after, before + 1);
  });

  test('a refused check-in does not reload Today', () async {
    h.api.on('GET', '/today', (_) => Fixtures.today());
    h.api.fail(
      'POST',
      '/checkins',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await h.today.load();
    final before = h.api.calls.where((c) => c == 'GET /today').length;
    await h.checkins.create(mood: 5);
    await Future<void>.delayed(Duration.zero);
    expect(h.api.calls.where((c) => c == 'GET /today').length, before);
  });

  test(
    'cards reflect reactions and dismissals made through insights',
    () async {
      h.api.on('GET', '/today', (_) => Fixtures.today());
      h.api.on(
        'POST',
        '/insights/i1/reaction',
        (_) => Fixtures.insight(id: 'i1', reaction: 'helpful'),
      );
      h.api.on(
        'POST',
        '/insights/i2/dismiss',
        (_) => Fixtures.insight(id: 'i2', dismissedAt: '2026-10-29T12:00:00Z'),
      );
      await h.today.load();
      var notified = 0;
      h.today.addListener(() => notified += 1);

      await h.insights.react(h.today.cards.first, InsightReaction.helpful);
      expect(h.today.cards.first.reaction, InsightReaction.helpful);

      await h.insights.dismiss(h.today.cards.last);
      expect(h.today.cards.map((c) => c.id), ['i1']);
      expect(notified, greaterThan(0));
      // The server copy was not refetched; the merge is local.
      expect(h.api.calls.where((c) => c == 'GET /today').length, 1);
    },
  );

  test('signing out forgets Today', () async {
    h.api.on('GET', '/today', (_) => Fixtures.today());
    h.api.on('POST', '/auth/logout', (_) => null);
    await h.today.load();
    await h.auth.logout();
    expect(h.today.today, isNull);
    expect(h.today.hasLoaded, isFalse);
  });
}
