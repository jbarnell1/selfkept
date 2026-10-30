import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/lab.dart';
import 'package:selfkept/pages/lab_entry_page.dart';
import 'package:selfkept/services/ocr/ocr.dart';
import 'package:selfkept/theme/tokens.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/pump.dart';

class _Scanner implements TextScanner {
  @override
  final bool available;

  const _Scanner({required this.available});

  @override
  Future<List<String>> linesFromImage(Uint8List jpegBytes) async => const [];
}

Finder _field(String key) => find.byKey(ValueKey('field-$key'));

Future<void> _type(WidgetTester tester, String key, String text) async {
  await tester.ensureVisible(_field(key));
  await tester.pumpAndSettle();
  await tester.enterText(_field(key), text);
  await tester.pump();
}

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    h.api.on('POST', '/auth/login', (_) => Fixtures.authResponse());
    await h.auth.login(email: 'a@b', password: 'x');
    h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    h.api.on('GET', '/labs/reports', (_) => [Fixtures.labReport()]);
    h.api.on('POST', '/labs/reports', (_) => Fixtures.labReport());
  });
  tearDown(() => h.dispose());

  /// The entry page pushed over a launcher page, so leaving can be seen.
  Future<void> open(
    WidgetTester tester, {
    TextScanner scanner = const _Scanner(available: false),
    ParseResult? prefill,
    ThemeData? theme,
  }) async {
    await usePhoneSize(tester);
    await tester.pumpWidget(
      h.wrap(
        Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) =>
                        LabEntryPage(scanner: scanner, prefill: prefill),
                  ),
                ),
                child: const Text('launch'),
              ),
            ),
          ),
        ),
        theme: theme,
      ),
    );
    await tester.tap(find.text('launch'));
    await tester.pumpAndSettle();
  }

  Map<String, dynamic> lastReportBody() {
    final i = h.api.calls.lastIndexOf('POST /labs/reports');
    return h.api.bodies[i] as Map<String, dynamic>;
  }

  testBothThemes('without a scanner the sheet opens straight away', (
    tester,
    theme,
  ) async {
    await open(tester, theme: theme);
    expect(find.text('Take a photo'), findsNothing);
    expect(find.text('Collected on'), findsOneWidget);
    expect(find.text('Provider'), findsOneWidget);
    expect(find.text('Keep these results'), findsOneWidget);
    expect(find.text('Heart'), findsOneWidget);
    expect(find.text('ApoB'), findsOneWidget);
    expect(find.text('Testosterone'), findsOneWidget);
    expect(find.text('Last time 92 mg/dL'), findsOneWidget);
    // Ratios are computed by the server and not typed.
    expect(find.text('TC/HDL ratio'), findsNothing);
    // A marker that does not apply to this person is not offered.
    expect(find.text('Estradiol'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('on desktop the default scanner is off and the sheet opens', (
    tester,
  ) async {
    debugDefaultTargetPlatformOverride = TargetPlatform.linux;
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const LabEntryPage()));
    await tester.pumpAndSettle();
    expect(createTextScanner().available, isFalse);
    expect(find.text('Take a photo'), findsNothing);
    expect(find.text('Keep these results'), findsOneWidget);
    debugDefaultTargetPlatformOverride = null;
  });

  testWidgets('on a phone the default scanner is on and offers the photo', (
    tester,
  ) async {
    debugDefaultTargetPlatformOverride = TargetPlatform.iOS;
    await usePhoneSize(tester);
    await tester.pumpWidget(h.wrap(const LabEntryPage()));
    await tester.pumpAndSettle();
    expect(createTextScanner().available, isTrue);
    expect(find.text('Take a photo'), findsOneWidget);
    expect(find.text('Keep these results'), findsNothing);
    debugDefaultTargetPlatformOverride = null;
  });

  testWidgets('typing two values posts them, then leaves with Kept.', (
    tester,
  ) async {
    await open(tester);
    await _type(tester, 'lab.apob', '1,180');
    await _type(tester, 'lab.glucose', '92');
    await tester.tap(find.text('Keep these results'));
    await tester.pumpAndSettle();

    expect(h.api.calls, contains('POST /labs/reports'));
    final body = lastReportBody();
    expect(body['provider'], 'manual');
    expect(body['collectedOn'], matches(r'^\d{4}-\d{2}-\d{2}$'));
    expect(body.containsKey('image'), isFalse);
    final results = (body['results'] as List).cast<Map<String, dynamic>>();
    expect(results, hasLength(2));
    final byKey = {for (final r in results) r['metricKey']: r};
    expect(byKey['lab.apob']!['value'], 1180);
    expect(byKey['lab.apob']!['unit'], 'mg/dL');
    expect(byKey['lab.glucose']!['value'], 92);
    expect(
      results.any((r) => (r['metricKey'] as String).contains('ratio')),
      isFalse,
    );

    // Back on the launcher, told it was kept, and the panels reloaded.
    expect(find.text('launch'), findsOneWidget);
    expect(find.text('Kept.'), findsOneWidget);
    expect(h.api.calls.where((c) => c == 'GET /labs/markers').length, 2);
  });

  testWidgets('letters in a field are refused and nothing is posted', (
    tester,
  ) async {
    await open(tester);
    await _type(tester, 'lab.apob', 'abc');
    await _type(tester, 'lab.glucose', '92');
    await tester.tap(find.text('Keep these results'));
    await tester.pumpAndSettle();
    expect(find.text('Enter a number.'), findsOneWidget);
    expect(h.api.calls, isNot(contains('POST /labs/reports')));
    expect(find.text('Keep these results'), findsOneWidget);
  });

  testWidgets('an empty sheet asks for at least one value', (tester) async {
    await open(tester);
    await tester.tap(find.text('Keep these results'));
    await tester.pumpAndSettle();
    expect(find.text('Add at least one value.'), findsOneWidget);
    expect(h.api.calls, isNot(contains('POST /labs/reports')));
  });

  testWidgets('Not on this panel disables the row and drops its value', (
    tester,
  ) async {
    await open(tester);
    await _type(tester, 'lab.apob', '92');
    await _type(tester, 'lab.glucose', '88');
    final apobRow = find.byKey(const ValueKey('row-lab.apob'));
    await tester.tap(
      find.descendant(of: apobRow, matching: find.byType(IconButton)),
    );
    await tester.pump();
    expect(
      tester
          .widget<TextField>(
            find.descendant(
              of: _field('lab.apob'),
              matching: find.byType(TextField),
            ),
          )
          .enabled,
      isFalse,
    );
    await tester.tap(find.text('Keep these results'));
    await tester.pumpAndSettle();
    final results = (lastReportBody()['results'] as List)
        .cast<Map<String, dynamic>>();
    expect([for (final r in results) r['metricKey']], ['lab.glucose']);
  });

  testWidgets('a failed save keeps the sheet and says why', (tester) async {
    h.api.fail(
      'POST',
      '/labs/reports',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await open(tester);
    await _type(tester, 'lab.apob', '92');
    await tester.tap(find.text('Keep these results'));
    await tester.pumpAndSettle();
    expect(find.text('The server had a problem.'), findsOneWidget);
    expect(find.text('Keep these results'), findsOneWidget);
    expect(find.text('launch'), findsNothing);
    expect(find.text('Kept.'), findsNothing);
  });

  testWidgets('a scanner offers the photo choices and typing instead', (
    tester,
  ) async {
    await open(tester, scanner: const _Scanner(available: true));
    expect(find.text('Take a photo'), findsOneWidget);
    expect(find.text('Choose a screenshot'), findsOneWidget);
    expect(find.text('Keep these results'), findsNothing);
    await tester.tap(find.text('Type them in instead'));
    await tester.pumpAndSettle();
    expect(find.text('Take a photo'), findsNothing);
    expect(find.text('Keep these results'), findsOneWidget);
  });

  testWidgets('prefill: best candidate, doubts marked, scan details sent', (
    tester,
  ) async {
    const result = ParseResult(
      candidates: [
        LabCandidate(
          metricKey: 'lab.apob',
          value: 92,
          unit: 'mg/dL',
          flag: 'high',
          rawText: 'ApoB 92 mg/dL',
          confidence: 1.0,
          line: 0,
        ),
        LabCandidate(
          metricKey: 'lab.apob',
          value: 9,
          rawText: 'ApoB 9',
          confidence: 0.4,
          line: 5,
        ),
        LabCandidate(
          metricKey: 'lab.hscrp',
          value: 4.2,
          unit: 'mg/L',
          rawText: 'hs-CRP 4.2',
          confidence: 0.5,
          line: 2,
        ),
      ],
    );
    await open(tester, prefill: result);
    expect(
      tester
          .widget<TextField>(
            find.descendant(
              of: _field('lab.apob'),
              matching: find.byType(TextField),
            ),
          )
          .controller!
          .text,
      '92',
    );
    expect(
      tester
          .widget<TextField>(
            find.descendant(
              of: _field('lab.hscrp'),
              matching: find.byType(TextField),
            ),
          )
          .controller!
          .text,
      '4.2',
    );
    // Only the doubtful one is marked, with an amber one pixel border.
    expect(find.text('Check this one'), findsOneWidget);
    final decoration = tester
        .widget<TextField>(
          find.descendant(
            of: _field('lab.hscrp'),
            matching: find.byType(TextField),
          ),
        )
        .decoration!;
    final border = decoration.enabledBorder! as OutlineInputBorder;
    expect(border.borderSide.width, 1);
    expect(border.borderSide.color, AppColors.light.attention);

    await tester.tap(find.text('Keep these results'));
    await tester.pumpAndSettle();
    final body = lastReportBody();
    expect(body['provider'], 'rythm');
    final results = (body['results'] as List).cast<Map<String, dynamic>>();
    expect(results, hasLength(2));
    final apob = results.firstWhere((r) => r['metricKey'] == 'lab.apob');
    expect(apob['flag'], 'high');
    expect(apob['rawText'], 'ApoB 92 mg/dL');
    final crp = results.firstWhere((r) => r['metricKey'] == 'lab.hscrp');
    expect(crp['unit'], 'mg/L');
    expect(crp.containsKey('flag'), isFalse);
  });

  testWidgets('markers failing to load shows a retry', (tester) async {
    h.api.fail(
      'GET',
      '/labs/markers',
      const ApiException('The server had a problem.', statusCode: 500),
    );
    await open(tester);
    expect(find.text('Labs did not load.'), findsOneWidget);
    h.api.on('GET', '/labs/markers', (_) => Fixtures.labMarkers());
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.text('Collected on'), findsOneWidget);
  });

  test('parseValue strips thousands commas and refuses the rest', () {
    expect(LabEntryPage.parseValue('1,180'), 1180);
    expect(LabEntryPage.parseValue(' 4.5 '), 4.5);
    expect(LabEntryPage.parseValue(''), isNull);
    expect(LabEntryPage.parseValue('abc'), isNull);
    expect(LabEntryPage.parseValue('-3'), isNull);
    expect(LabEntryPage.parseValue('NaN'), isNull);
  });
}
