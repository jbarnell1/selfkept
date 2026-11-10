import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/lab_review.dart';
import 'package:selfkept/pages/lab_entry_page.dart';
import 'package:selfkept/pages/lab_marker_page.dart';
import 'package:selfkept/pages/labs/draw_conditions_page.dart';
import 'package:selfkept/pages/labs/lab_review_page.dart';
import 'package:selfkept/pages/labs/lab_timeline_page.dart';
import 'package:selfkept/pages/labs/related_readings.dart';
import 'package:selfkept/services/ocr/ocr.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';
import '../helpers/systems_fixtures.dart';

class _NoScanner implements TextScanner {
  const _NoScanner();

  @override
  bool get available => false;

  @override
  Future<List<String>> linesFromImage(Uint8List jpegBytes) async => const [];
}

void main() {
  late TestHarness h;
  final opened = <Uri>[];

  setUp(() async {
    opened.clear();
    h = TestHarness(
      openUrl: (uri) async {
        opened.add(uri);
        return true;
      },
    );
    await signInWithFeatures(h);
  });
  tearDown(() => h.dispose());

  Future<void> pump(
    WidgetTester tester,
    Widget page, {
    ThemeData? theme,
  }) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(page, theme: theme));
    await tester.pumpAndSettle();
  }

  /// [page] pushed over a launcher, so popping can be seen.
  Future<void> pushed(WidgetTester tester, Widget page) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(
        Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () => Navigator.of(
                  context,
                ).push(MaterialPageRoute<void>(builder: (_) => page)),
                child: const Text('launch'),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('launch'));
    await tester.pumpAndSettle();
  }

  group('lab review', () {
    testBothThemes('verdicts, variation, conditions and trials', (
      tester,
      theme,
    ) async {
      h.api.on(
        'GET',
        '/labs/reports/r2/review',
        (_) => SystemsFixtures.labReview(),
      );
      await pump(tester, const LabReviewPage(reportId: 'r2'), theme: theme);
      expect(find.text('Panel of Dec 12, 2026'), findsOneWidget);
      expect(find.text('Against the panel of Aug 14, 2026.'), findsOneWidget);
      expect(find.text('1 working, 1 cannot tell'), findsOneWidget);
      expect(
        find.text('Fasting: Not fasting this time, fasting before.'),
        findsOneWidget,
      );
      expect(
        find.text('Time of day: Afternoon this time, morning before.'),
        findsOneWidget,
      );
      await scrollTo(tester, find.text('GGT'));
      expect(find.text('Working'), findsOneWidget);
      expect(find.text('Down 12 U/L (30 percent) from 40 U/L'), findsOneWidget);
      expect(
        find.text('Beyond the usual test-to-test variation of 24.7 percent.'),
        findsOneWidget,
      );
      await scrollTo(tester, find.text('Cannot tell'));
      expect(
        find.text(labReasonText('draw_conditions_differ')),
        findsOneWidget,
      );
      expect(find.text(LabReviewPage.beyondUnknown), findsOneWidget);
      expect(
        find.text('After a usual meal triglycerides read higher.'),
        findsOneWidget,
      );
      await scrollTo(tester, find.text('Oats instead of a bagel'));
      expect(find.text('GGT: Working'), findsOneWidget);
      expect(find.text(LabReviewPage.sharedCreditLine), findsOneWidget);
    });

    testWidgets('the variation source opens in the browser', (tester) async {
      h.api.on(
        'GET',
        '/labs/reports/r2/review',
        (_) => SystemsFixtures.labReview(),
      );
      await pump(tester, const LabReviewPage(reportId: 'r2'));
      final source = find.textContaining('Carobene 2017');
      await scrollTo(tester, source);
      await tester.tap(source);
      await tester.pumpAndSettle();
      expect(opened.single.host, 'pubmed.ncbi.nlm.nih.gov');
    });

    testWidgets('loading, failed and not shared', (tester) async {
      final gate = Completer<Map<String, dynamic>>();
      h.api.on('GET', '/labs/reports/r2/review', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(h.wrap(const LabReviewPage(reportId: 'r2')));
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(const ApiException('Down', statusCode: 500));
      await tester.pumpAndSettle();
      expect(find.text(LabReviewPage.loadErrorTitle), findsOneWidget);

      h.api.fail(
        'GET',
        '/labs/reports/r9/review',
        const ApiException('Not found', statusCode: 404),
      );
      await tester.pumpWidget(h.wrap(const SizedBox.shrink()));
      await pump(tester, const LabReviewPage(reportId: 'r9'));
      expect(find.text(NotSharedView.title), findsOneWidget);
    });

    testWidgets('your own panel: change how it was drawn', (tester) async {
      h.api.on(
        'GET',
        '/labs/reports/r2/review',
        (_) => SystemsFixtures.labReview(),
      );
      h.api.on(
        'PUT',
        '/labs/reports/r2/conditions',
        (_) => {
          ...Fixtures.labReport(id: 'r2'),
          'conditions': SystemsFixtures.conditions(defaulted: false),
        },
      );
      await pump(tester, const LabReviewPage(reportId: 'r2'));
      await tester.tap(find.text(LabReviewPage.editConditionsLabel));
      await tester.pumpAndSettle();
      expect(find.byType(DrawConditionsPage), findsOneWidget);
      await scrollTo(tester, find.byKey(const ValueKey('unwell-yes')));
      await tester.tap(find.byKey(const ValueKey('unwell-yes')));
      await scrollTo(tester, find.text(DrawConditionsPage.saveLabel));
      await tester.tap(find.text(DrawConditionsPage.saveLabel));
      await tester.pumpAndSettle();
      expect(lastQuery(h.api, 'PUT /labs/reports/r2/conditions'), {
        'fasting': 'no',
        'drawTime': '15:40',
        'unwell': true,
      });
      expect(
        h.api.calls.where((c) => c == 'GET /labs/reports/r2/review').length,
        2,
      );
    });

    testWidgets('someone else panel has no edit', (tester) async {
      h.api.on(
        'GET',
        '/labs/reports/r2/review',
        (_) => {...SystemsFixtures.labReview(), 'personId': 'p2'},
      );
      await pump(tester, const LabReviewPage(reportId: 'r2'));
      expect(find.text(LabReviewPage.editConditionsLabel), findsNothing);
    });
  });

  group('draw conditions', () {
    testBothThemes('a Rythm pre-fill from the server opens filled in', (
      tester,
      theme,
    ) async {
      await pump(
        tester,
        DrawConditionsPage(
          reportId: 'r1',
          initial: DrawConditions.fromJson(SystemsFixtures.conditions()),
          providerLabel: 'Rythm',
        ),
        theme: theme,
      );
      expect(
        find.text(DrawConditionsPage.prefillNote('Rythm')),
        findsOneWidget,
      );
      expect(
        tester
            .widget<ChoiceChip>(find.byKey(const ValueKey('fasting-yes')))
            .selected,
        isTrue,
      );
      expect(
        tester
            .widget<TextField>(find.byKey(const ValueKey('fasting-hours')))
            .controller
            ?.text,
        '8',
      );
      expect(
        tester
            .widget<ChoiceChip>(find.byKey(const ValueKey('period-morning')))
            .selected,
        isTrue,
      );
    });

    testWidgets('saving untouched keeps the pre-fill and sends nothing', (
      tester,
    ) async {
      await pushed(
        tester,
        DrawConditionsPage(
          reportId: 'r1',
          initial: DrawConditions.fromJson(SystemsFixtures.conditions()),
        ),
      );
      await scrollTo(tester, find.text(DrawConditionsPage.saveLabel));
      await tester.tap(find.text(DrawConditionsPage.saveLabel));
      await tester.pumpAndSettle();
      expect(find.byType(DrawConditionsPage), findsNothing);
      expect(h.api.calls, isNot(contains('PUT /labs/reports/r1/conditions')));
    });

    testWidgets('not fasting turns off the hours; a refusal shows', (
      tester,
    ) async {
      h.api.fail(
        'PUT',
        '/labs/reports/r1/conditions',
        const ApiException(
          'fastingHours must be between 0 and 72.',
          statusCode: 400,
        ),
      );
      await pump(tester, const DrawConditionsPage(reportId: 'r1'));
      await tester.tap(find.byKey(const ValueKey('fasting-no')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<TextField>(find.byKey(const ValueKey('fasting-hours')))
            .enabled,
        isFalse,
      );
      await tester.tap(find.byKey(const ValueKey('fasting-yes')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const ValueKey('fasting-hours')), '90');
      await scrollTo(tester, find.text(DrawConditionsPage.saveLabel));
      await tester.tap(find.text(DrawConditionsPage.saveLabel));
      await tester.pumpAndSettle();
      expect(lastQuery(h.api, 'PUT /labs/reports/r1/conditions'), {
        'fasting': 'yes',
        'fastingHours': 90,
      });
      expect(
        find.text('fastingHours must be between 0 and 72.'),
        findsOneWidget,
      );
    });

    testWidgets('keeping a Rythm panel asks how it was drawn, pre-filled by '
        'the server', (tester) async {
      h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
      h.api.on('GET', '/labs/reports', (_) => [Fixtures.labReport()]);
      h.api.on(
        'POST',
        '/labs/reports',
        (_) => {
          ...Fixtures.labReport(),
          'conditions': SystemsFixtures.conditions(),
        },
      );
      await pushed(tester, const LabEntryPage(scanner: _NoScanner()));
      await tester.ensureVisible(find.byKey(const ValueKey('field-lab.apob')));
      await tester.enterText(
        find.byKey(const ValueKey('field-lab.apob')),
        '92',
      );
      await tester.tap(find.text(LabEntryPage.keepLabel));
      await tester.pumpAndSettle();
      final body = lastQuery(h.api, 'POST /labs/reports')!;
      expect(body.containsKey('conditions'), isFalse);
      expect(find.byType(DrawConditionsPage), findsOneWidget);
      expect(
        find.text(DrawConditionsPage.prefillNote('Rythm')),
        findsOneWidget,
      );
      await scrollTo(tester, find.text(DrawConditionsPage.skipLabel));
      await tester.tap(find.text(DrawConditionsPage.skipLabel));
      await tester.pumpAndSettle();
      expect(find.byType(DrawConditionsPage), findsNothing);
      expect(find.byType(LabEntryPage), findsNothing);
      expect(find.text('Kept.'), findsOneWidget);
    });

    testWidgets('an older server skips the question', (tester) async {
      h.api.on(
        'GET',
        '/system/version',
        (_) => {
          ...Fixtures.version(),
          'features': ['labs.reports'],
        },
      );
      await h.startup.run();
      h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
      h.api.on('GET', '/labs/reports', (_) => [Fixtures.labReport()]);
      h.api.on('POST', '/labs/reports', (_) => Fixtures.labReport());
      await pushed(tester, const LabEntryPage(scanner: _NoScanner()));
      await tester.ensureVisible(find.byKey(const ValueKey('field-lab.apob')));
      await tester.enterText(
        find.byKey(const ValueKey('field-lab.apob')),
        '92',
      );
      await tester.tap(find.text(LabEntryPage.keepLabel));
      await tester.pumpAndSettle();
      expect(find.byType(DrawConditionsPage), findsNothing);
      expect(find.text('Kept.'), findsOneWidget);
    });
  });

  group('timeline', () {
    testBothThemes('panels with how they were drawn', (tester, theme) async {
      h.api.on('GET', '/labs/timeline', (_) => SystemsFixtures.timeline());
      await pump(tester, const LabTimelinePage(), theme: theme);
      expect(find.text(LabTimelinePage.title), findsOneWidget);
      expect(find.text('Aug 14, 2026'), findsOneWidget);
      expect(find.text('Rythm · 30 results'), findsOneWidget);
      expect(find.text('Fasting, morning'), findsOneWidget);
      expect(find.text('Taking: creatine'), findsOneWidget);
      expect(find.text('Weight 84.0 kg on Aug 12'), findsOneWidget);
    });

    testWidgets('weight not shared, and an empty timeline', (tester) async {
      h.api.on(
        'GET',
        '/labs/timeline',
        (_) => SystemsFixtures.timeline(weightShared: false),
      );
      await pump(tester, const LabTimelinePage());
      expect(find.text(LabTimelinePage.weightNotShared), findsOneWidget);
      expect(find.textContaining('Weight 84'), findsNothing);
    });

    testWidgets('someone else not sharing labs', (tester) async {
      h.api.fail(
        'GET',
        '/labs/timeline',
        const ApiException('Not found', statusCode: 404),
      );
      h.viewing.view('p2', displayName: 'Charles');
      await pump(tester, const LabTimelinePage());
      expect(lastQuery(h.api, 'GET /labs/timeline'), {'personId': 'p2'});
      expect(find.text(NotSharedView.detailFor('Charles')), findsOneWidget);
    });
  });

  group('related readings', () {
    testBothThemes('on the marker page, with the source by name', (
      tester,
      theme,
    ) async {
      h.api.on(
        'GET',
        '/labs/markers/lab.ferritin',
        (_) => Fixtures.labMarkerDetail(
          marker: Fixtures.labMarker(
            metricKey: 'lab.ferritin',
            displayName: 'Ferritin',
          ),
        ),
      );
      h.api.on(
        'GET',
        '/labs/markers/lab.ferritin/related',
        (_) => SystemsFixtures.related(),
      );
      await pump(
        tester,
        const LabMarkerPage(metricKey: 'lab.ferritin'),
        theme: theme,
      );
      await scrollTo(tester, find.text(RelatedReadingsSection.title));
      expect(find.text('hs-CRP'), findsOneWidget);
      expect(find.text('4.2 mg/L on Oct 24, 2026'), findsOneWidget);
      expect(find.text('Ferritin rises with inflammation.'), findsOneWidget);
      expect(find.text(RelatedReadingsSection.notSharedLine), findsOneWidget);
      await scrollTo(tester, find.text('WHO 2020'));
      await tester.tap(find.text('WHO 2020'));
      await tester.pumpAndSettle();
      expect(opened.single.toString(), 'https://example.test/who');
    });

    testWidgets('a 404 leaves the section out', (tester) async {
      h.api.on(
        'GET',
        '/labs/markers/lab.apob',
        (_) => Fixtures.labMarkerDetail(),
      );
      h.api.fail(
        'GET',
        '/labs/markers/lab.apob/related',
        const ApiException('Not found', statusCode: 404),
      );
      await pump(tester, const LabMarkerPage(metricKey: 'lab.apob'));
      expect(find.text(RelatedReadingsSection.title), findsNothing);
    });
  });
}
