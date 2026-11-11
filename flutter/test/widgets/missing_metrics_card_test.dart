import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/collectors/health_collector.dart';
import 'package:selfkept/models/metric.dart';
import 'package:selfkept/models/source.dart';
import 'package:selfkept/pages/sources_page.dart';
import 'package:selfkept/widgets/missing_metrics_card.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/fake_health_collector.dart';
import '../helpers/pump.dart';

const String _kinds = 'food and drink, lean mass, bone mass and body water';

const Set<MetricKey> _new016 = {
  MetricKey.energyIntakeKcal,
  MetricKey.proteinG,
  MetricKey.carbsG,
  MetricKey.fatG,
  MetricKey.leanMassKg,
  MetricKey.boneMassKg,
  MetricKey.bodyWaterKg,
};

MetricPermissions _access({Set<MetricKey> missing = const {}}) =>
    MetricPermissions(
      granted: MetricKey.collectable.difference(missing),
      missing: missing,
    );

void main() {
  group('MissingMetricsCard', () {
    testBothThemes('says what is new and offers Allow and Not now', (
      tester,
      theme,
    ) async {
      var allowed = 0;
      var later = 0;
      await tester.pumpWidget(
        MaterialApp(
          theme: theme,
          home: Scaffold(
            body: MissingMetricsCard(
              kinds: _kinds,
              storeName: 'Health Connect',
              onAllow: () => allowed++,
              onNotNow: () => later++,
            ),
          ),
        ),
      );

      expect(find.text('New things this phone can read'), findsOneWidget);
      expect(find.text('Not allowed yet: $_kinds.'), findsOneWidget);
      expect(
        find.text(
          'It only reads what Health Connect already holds, and keeps it on your own server.',
        ),
        findsOneWidget,
      );
      await tester.tap(find.text('Allow'));
      await tester.tap(find.text('Not now'));
      expect(allowed, 1);
      expect(later, 1);
      expect(tester.takeException(), isNull);
    });

    testWidgets('while the sheet is up both buttons wait', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: MissingMetricsCard(
              kinds: _kinds,
              storeName: 'Health Connect',
              busy: true,
              onAllow: () {},
              onNotNow: () {},
            ),
          ),
        ),
      );
      final allow = tester.widget<FilledButton>(find.byType(FilledButton));
      final notNow = tester.widget<TextButton>(find.byType(TextButton));
      expect(allow.onPressed, isNull);
      expect(notNow.onPressed, isNull);
    });
  });

  group('on a phone that connected on an earlier build', () {
    late FakeHealthCollector collector;
    late TestHarness h;

    setUp(() async {
      collector = FakeHealthCollector(kind: SourceKind.healthConnect);
      h = TestHarness(collector: collector);
      h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
      await h.auth.login(email: 'a@b', password: 'x');
      h.api.on('GET', '/oura', (_) => Fixtures.ouraStatus(configured: false));
      h.api.on('GET', '/sources', (_) => <Map<String, dynamic>>[]);
      h.api.on('POST', '/ingest/batch', (_) => Fixtures.ingestResult());
      await h.outbox.writeState('collector.u1.sourceId', 'src-phone');
    });
    tearDown(() async {
      h.dispose();
      await collector.close();
    });

    testWidgets('Today: Not now hides the card', (tester) async {
      collector.metricAccess = _access(missing: _new016);
      await h.collector.init(collect: false);
      await tester.pumpWidget(
        h.wrap(const Scaffold(body: MissingMetricsTodayCard())),
      );
      expect(find.text('Not allowed yet: $_kinds.'), findsOneWidget);
      expect(find.textContaining('Health Connect'), findsOneWidget);

      await tester.tap(find.text('Not now'));
      await tester.pumpAndSettle();

      expect(find.text(MissingMetricsCard.title), findsNothing);
      expect(collector.requestCount, 0);
    });

    testWidgets('Today: Allow asks and the card goes once all is allowed', (
      tester,
    ) async {
      collector.metricAccess = _access(missing: _new016);
      collector.metricAccessAfterRequest = _access();
      await h.collector.init(collect: false);
      await tester.pumpWidget(
        h.wrap(const Scaffold(body: MissingMetricsTodayCard())),
      );

      await tester.tap(find.text('Allow'));
      await tester.pumpAndSettle();

      expect(collector.requested, MetricKey.collectable);
      expect(find.text(MissingMetricsCard.title), findsNothing);
    });

    testWidgets('Today: nothing missing, no card', (tester) async {
      collector.metricAccess = _access();
      await h.collector.init(collect: false);
      await tester.pumpWidget(
        h.wrap(const Scaffold(body: MissingMetricsTodayCard())),
      );
      expect(find.text(MissingMetricsCard.title), findsNothing);
    });

    testBothThemes('Sources: the row stays after Not now and asks again', (
      tester,
      theme,
    ) async {
      await usePhoneSize(tester);
      collector.metricAccess = _access(missing: _new016);
      await h.collector.init(collect: false);
      await h.collector.notNowForMissingMetrics();
      await tester.pumpWidget(h.wrap(const SourcesPage(), theme: theme));
      await tester.pumpAndSettle();

      final line = find.text(SourcesPage.missingMetricsLine(_kinds));
      expect(line, findsOneWidget);
      await tester.ensureVisible(find.widgetWithText(TextButton, 'Allow'));
      await tester.tap(find.widgetWithText(TextButton, 'Allow'));
      await tester.pumpAndSettle();

      expect(collector.requestCount, 1);
      // Declined again: the row stays and says where else to allow it.
      expect(line, findsOneWidget);
      expect(
        find.text(SourcesPage.missingMetricsHint('Health Connect')),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets('Sources: nothing missing, no row', (tester) async {
      await usePhoneSize(tester);
      collector.metricAccess = _access();
      await h.collector.init(collect: false);
      await tester.pumpWidget(h.wrap(const SourcesPage()));
      await tester.pumpAndSettle();
      expect(find.textContaining('not allowed yet'), findsNothing);
    });
  });
}
