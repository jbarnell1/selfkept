import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/insight.dart';
import 'package:selfkept/providers/insights_provider.dart';
import 'package:selfkept/services/insights_service.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  late InsightsProvider insights;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    insights = InsightsProvider(
      service: InsightsService(h.api),
      auth: h.auth,
      clock: () => DateTime.utc(2026, 10, 29, 12),
    );
  });

  tearDown(() {
    insights.dispose();
    h.dispose();
  });

  List<Map<String, dynamic>> archive() => [
    Fixtures.insight(id: 'i1', day: '2026-10-29'),
    Fixtures.insight(
      id: 'i2',
      day: '2026-10-29',
      kind: 'sleep_good_week',
      severity: 'notice',
    ),
    Fixtures.insight(
      id: 'i3',
      day: '2026-10-27',
      kind: 'spo2_low',
      severity: 'clinician',
    ),
  ];

  test('load asks for 90 days, keeps newest first and groups by day', () async {
    h.api.on('GET', '/insights', (_) => archive());
    await insights.load();
    expect(insights.hasLoaded, isTrue);
    expect(insights.error, isNull);
    final query = h.api.bodies[h.api.calls.indexOf('GET /insights')] as Map;
    expect(query['from'], '2026-07-31');
    expect(query['to'], '2026-10-29');
    expect(query['includeDismissed'], isFalse);
    expect(insights.insights.map((i) => i.id), ['i1', 'i2', 'i3']);
    final days = insights.byDay;
    expect(days, hasLength(2));
    expect(days.first.day, DateTime(2026, 10, 29));
    expect(days.first.insights, hasLength(2));
    expect(days.last.insights.single.id, 'i3');
  });

  test('severity filter narrows the visible list without a request', () async {
    h.api.on('GET', '/insights', (_) => archive());
    await insights.load();
    final before = h.api.calls.length;
    insights.setSeverity(InsightSeverity.clinician);
    expect(insights.visible.map((i) => i.id), ['i3']);
    insights.setSeverity(null);
    expect(insights.visible, hasLength(3));
    expect(h.api.calls.length, before);
  });

  test('include dismissed reloads with the flag', () async {
    h.api.on('GET', '/insights', (_) => archive());
    await insights.load();
    await insights.setIncludeDismissed(true);
    final last = h.api.bodies.last as Map;
    expect(last['includeDismissed'], isTrue);
    expect(insights.includeDismissed, isTrue);
  });

  test('a load failure keeps the error and does not mark loaded', () async {
    h.api.fail(
      'GET',
      '/insights',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await insights.load();
    expect(insights.hasLoaded, isFalse);
    expect(insights.error, 'The server had a problem.');
  });

  test('react is optimistic and keeps the server copy', () async {
    h.api.on('GET', '/insights', (_) => archive());
    await insights.load();
    h.api.on(
      'POST',
      '/insights/i1/reaction',
      (data) => Fixtures.insight(
        id: 'i1',
        reaction: (data as Map)['reaction'] as String,
      ),
    );
    final target = insights.insights.first;
    final future = insights.react(target, InsightReaction.helpful);
    // Before the server answers the card already shows the reaction.
    expect(insights.known('i1')!.reaction, InsightReaction.helpful);
    expect(await future, isTrue);
    expect(insights.insights.first.reaction, InsightReaction.helpful);
    final body =
        h.api.bodies[h.api.calls.indexOf('POST /insights/i1/reaction')];
    expect(body, {'reaction': 'helpful'});
  });

  test('a refused reaction rolls back and explains', () async {
    h.api.on('GET', '/insights', (_) => archive());
    await insights.load();
    h.api.fail(
      'POST',
      '/insights/i1/reaction',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    final ok = await insights.react(
      insights.insights.first,
      InsightReaction.wrong,
    );
    expect(ok, isFalse);
    expect(insights.insights.first.reaction, isNull);
    expect(insights.known('i1')!.reaction, isNull);
    expect(insights.error, 'The server had a problem.');
  });

  test('dismiss hides the card at once and rolls back on refusal', () async {
    h.api.on('GET', '/insights', (_) => archive());
    await insights.load();
    h.api.on(
      'POST',
      '/insights/i2/dismiss',
      (_) => Fixtures.insight(id: 'i2', dismissedAt: '2026-10-29T12:00:00Z'),
    );
    final i2 = insights.insights[1];
    final future = insights.dismiss(i2);
    expect(insights.visible.map((i) => i.id), ['i1', 'i3']);
    expect(await future, isTrue);
    expect(insights.known('i2')!.isDismissed, isTrue);

    h.api.fail(
      'POST',
      '/insights/i1/dismiss',
      const ApiException('The server could not be reached.', isNetwork: true),
    );
    expect(await insights.dismiss(insights.insights.first), isFalse);
    expect(insights.known('i1')!.isDismissed, isFalse);
    expect(insights.visible.map((i) => i.id), ['i1', 'i3']);
  });

  test('a card not in the archive can still be reacted to', () async {
    h.api.on(
      'POST',
      '/insights/x9/reaction',
      (_) => Fixtures.insight(id: 'x9', reaction: 'knew'),
    );
    final card = Insight.fromJson(Fixtures.insight(id: 'x9'));
    expect(await insights.react(card, InsightReaction.knew), isTrue);
    expect(insights.known('x9')!.reaction, InsightReaction.knew);
    expect(insights.insights, isEmpty);
  });

  test('recompute posts and reports acceptance', () async {
    h.api.on('POST', '/insights/recompute', (_) => null);
    expect(await insights.recompute(), isTrue);
    expect(h.api.calls, contains('POST /insights/recompute'));
    expect(insights.isRecomputing, isFalse);

    h.api.fail(
      'POST',
      '/insights/recompute',
      const ApiException(
        'You do not have permission to do that.',
        statusCode: 403,
      ),
    );
    expect(await insights.recompute(), isFalse);
    expect(insights.error, 'You do not have permission to do that.');
  });

  test('signing out clears the archive', () async {
    h.api.on('GET', '/insights', (_) => archive());
    h.api.on('POST', '/auth/logout', (_) => null);
    await insights.load();
    await h.auth.logout();
    expect(insights.insights, isEmpty);
    expect(insights.hasLoaded, isFalse);
    expect(insights.known('i1'), isNull);
  });
}
