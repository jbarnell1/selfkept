import 'dart:async';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/lab.dart';
import 'package:selfkept/pages/lab_marker_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/clinician_summary_button.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on(
      'GET',
      '/labs/markers/lab.apob',
      (_) => Fixtures.labMarkerDetail(),
    );
  });
  tearDown(() => h.dispose());

  Widget page({ThemeData? theme}) =>
      h.wrap(const LabMarkerPage(metricKey: 'lab.apob'), theme: theme);

  testBothThemes('data: value, status, ranges in words, blurb and history', (
    tester,
    theme,
  ) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(page(theme: theme));
    await tester.pumpAndSettle();

    expect(find.text('ApoB'), findsOneWidget);
    expect(find.text('92'), findsWidgets);
    expect(find.text('Above range'), findsOneWidget);
    expect(find.text('Usual range 66 to 133 mg/dL'), findsOneWidget);
    expect(find.text('Often aimed for: below 90 mg/dL'), findsOneWidget);
    expect(
      find.text('ApoB counts the particles that carry cholesterol.'),
      findsOneWidget,
    );
    expect(find.text('What moves it'), findsOneWidget);
    expect(
      find.text('Saturated fat, body weight and some medicines.'),
      findsOneWidget,
    );

    await scrollTo(tester, find.text('History'));
    expect(find.text('October 24, 2026'), findsWidgets);
    expect(find.text('92 mg/dL'), findsOneWidget);
    expect(find.text('78 mg/dL'), findsOneWidget);
    expect(find.text('70 mg/dL'), findsOneWidget);
    expect(find.text('High'), findsOneWidget);
    expect(find.text('Low'), findsOneWidget);

    await scrollTo(tester, find.text('Where this range comes from'));
    expect(find.text('Where this range comes from'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('no source means no link', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/labs/markers/lab.apob',
      (_) => Fixtures.labMarkerDetail(sourceUrl: null),
    );
    await tester.pumpWidget(page());
    await tester.pumpAndSettle();
    expect(find.text('Where this range comes from'), findsNothing);
  });

  testWidgets('a marker with no result says so', (tester) async {
    await usePhoneSize(tester);
    h.api.on(
      'GET',
      '/labs/markers/lab.apob',
      (_) => Fixtures.labMarkerDetail(
        marker: Fixtures.labMarker(
          latestValue: null,
          latestDate: null,
          previousValue: null,
          severity: null,
          position: null,
          tier: null,
        ),
        history: <Map<String, dynamic>>[],
      ),
    );
    await tester.pumpWidget(page());
    await tester.pumpAndSettle();
    expect(find.text('No result yet'), findsOneWidget);
    expect(find.text('History'), findsNothing);
    expect(find.text('Usual range 40 to 90 mg/dL'), findsOneWidget);
  });

  testWidgets('loading then error with a retry', (tester) async {
    await usePhoneSize(tester);
    final completer = Completer<Map<String, dynamic>>();
    h.api.on('GET', '/labs/markers/lab.apob', (_) => completer.future);
    await tester.pumpWidget(page());
    await pumpFrames(tester);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    completer.completeError(
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await tester.pumpAndSettle();
    expect(find.text('This marker did not load.'), findsOneWidget);
    expect(find.text('The server had a problem.'), findsOneWidget);

    h.api.on(
      'GET',
      '/labs/markers/lab.apob',
      (_) => Fixtures.labMarkerDetail(),
    );
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('This marker did not load.'), findsNothing);
    expect(find.text('Usual range 66 to 133 mg/dL'), findsOneWidget);
  });

  group('range words', () {
    test('population', () {
      expect(
        LabMarkerPage.populationWords(
          const LabRange(low: 66, high: 133),
          'mg/dL',
        ),
        'Usual range 66 to 133 mg/dL',
      );
      expect(
        LabMarkerPage.populationWords(const LabRange(high: 100), 'mg/dL'),
        'Usual range up to 100 mg/dL',
      );
      expect(
        LabMarkerPage.populationWords(const LabRange(low: 40), 'mg/dL'),
        'Usual range from 40 mg/dL',
      );
      expect(LabMarkerPage.populationWords(null, 'mg/dL'), isNull);
      expect(
        LabMarkerPage.populationWords(const LabRange(high: 5), ''),
        'Usual range up to 5',
      );
    });

    test('optimal', () {
      expect(
        LabMarkerPage.optimalWords(const LabRange(high: 90), 'mg/dL'),
        'Often aimed for: below 90 mg/dL',
      );
      expect(
        LabMarkerPage.optimalWords(const LabRange(low: 50), 'ng/mL'),
        'Often aimed for: above 50 ng/mL',
      );
      expect(
        LabMarkerPage.optimalWords(const LabRange(low: 1, high: 2.5), 'x'),
        'Often aimed for: 1 to 2.5 x',
      );
      expect(LabMarkerPage.optimalWords(null, 'x'), isNull);
    });
  });

  group('clinician summary button', () {
    Widget button(Future<void> Function(Uint8List bytes, String name) save) =>
        h.wrap(Scaffold(body: ClinicianSummaryButton(save: save)));

    testWidgets('fetches the last 90 days and hands the bytes over', (
      tester,
    ) async {
      h.api.onBytes(
        '/labs/summary.pdf',
        (_) => Uint8List.fromList([37, 80, 68, 70]),
      );
      Uint8List? saved;
      String? name;
      await tester.pumpWidget(
        button((bytes, fileName) async {
          saved = bytes;
          name = fileName;
        }),
      );
      await tester.tap(find.text('Clinician summary'));
      await tester.pumpAndSettle();

      expect(saved, [37, 80, 68, 70]);
      expect(name, 'summary.pdf');
      final query = h.api.bodies.last as Map<String, dynamic>;
      final from = DateTime.parse(query['from'] as String);
      final to = DateTime.parse(query['to'] as String);
      expect(to.difference(from).inDays, 90);
      expect(find.text('The summary did not download.'), findsNothing);
    });

    testWidgets('a failure to save says so', (tester) async {
      h.api.onBytes('/labs/summary.pdf', (_) => Uint8List(4));
      await tester.pumpWidget(
        button((bytes, fileName) async => throw UnsupportedError('no')),
      );
      await tester.tap(find.text('Clinician summary'));
      await tester.pumpAndSettle();
      expect(find.text('The summary did not download.'), findsOneWidget);
    });
  });
}
