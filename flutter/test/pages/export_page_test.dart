import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/enums.dart';
import 'package:selfkept/models/export.dart';
import 'package:selfkept/pages/export_page.dart';
import 'package:selfkept/theme/app_theme.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/pump.dart';

Map<String, dynamic> _bundle(String text) => {
  'text': text,
  'characters': text.length,
  'approximateTokens': (text.length / 4).ceil(),
  'included': ['sleep'],
  'from': '2026-08-01',
  'to': '2026-10-29',
};

void main() {
  late TestHarness h;
  final saved = <(Uint8List, String, String)>[];
  String? copied;

  setUp(() {
    saved.clear();
    copied = null;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, (call) async {
          if (call.method == 'Clipboard.setData') {
            copied = (call.arguments as Map)['text'] as String?;
          }
          return null;
        });
    h = TestHarness();
    h.api.on(
      'POST',
      '/export/bundle',
      (_) => _bundle('# Health summary\nday -3 | 6123'),
    );
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, null);
  });

  Future<void> pumpPage(WidgetTester tester, {ThemeData? theme}) async {
    tester.view.physicalSize = const Size(900, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      h.wrap(
        ExportPage(save: (b, n, m) async => saved.add((b, n, m))),
        theme: theme,
      ),
    );
    await tester.pumpAndSettle();
  }

  Map<String, dynamic> lastBody() => h.api.bodies.last as Map<String, dynamic>;

  test(
    'BundleOptions.toJson sends the chosen categories in order and redacts by default',
    () {
      final json = BundleOptions.defaults
          .copyWith(categories: {ShareCategory.labs, ShareCategory.sleep})
          .toJson(export: false);
      expect(json['categories'], ['sleep', 'labs']);
      expect(json['keepDates'], isFalse);
      expect(json['includeNotes'], isFalse);
      expect(json['includeName'], isFalse);
      expect(json['days'], 90);
      expect(json['export'], isFalse);
    },
  );

  test('PrivacyBundle reads camelCase and PascalCase', () {
    final camel = PrivacyBundle.fromJson(_bundle('abc'));
    final pascal = PrivacyBundle.fromJson({
      'Text': 'abc',
      'Characters': 3,
      'ApproximateTokens': 1,
      'Included': ['labs'],
    });
    expect(camel.text, 'abc');
    expect(pascal.characters, 3);
    expect(pascal.included, ['labs']);
  });

  testWidgets('copy and save stay off until there is a preview', (
    tester,
  ) async {
    await pumpPage(tester);
    final copy = find.widgetWithText(OutlinedButton, 'Copy');
    expect(tester.widget<OutlinedButton>(copy).onPressed, isNull);
    expect(find.byKey(const Key('bundle-preview')), findsNothing);
  });

  testWidgets(
    'preview posts export false with the redacted defaults and shows the text',
    (tester) async {
      await pumpPage(tester);
      await tester.tap(find.text('Preview'));
      await tester.pumpAndSettle();

      expect(h.api.calls.last, 'POST /export/bundle');
      expect(lastBody()['export'], isFalse);
      expect(lastBody()['includeName'], isFalse);
      expect(find.byKey(const Key('bundle-preview')), findsOneWidget);
      expect(find.textContaining('day -3 | 6123'), findsOneWidget);
      expect(
        find.textContaining('This is exactly what Copy and Save give you'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'copy asks again with export true and puts the text on the clipboard',
    (tester) async {
      await pumpPage(tester);
      await tester.tap(find.text('Preview'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Copy'));
      await tester.pumpAndSettle();

      expect(lastBody()['export'], isTrue);
      expect(copied, '# Health summary\nday -3 | 6123');
      expect(find.text('Copied.'), findsOneWidget);
    },
  );

  testWidgets('save writes a Markdown file with the bundle text', (
    tester,
  ) async {
    await pumpPage(tester);
    await tester.tap(find.text('Preview'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    expect(saved, hasLength(1));
    expect(saved.single.$2, startsWith('health-summary-'));
    expect(saved.single.$2, endsWith('.md'));
    expect(saved.single.$3, 'text/markdown');
    expect(utf8.decode(saved.single.$1), '# Health summary\nday -3 | 6123');
  });

  testWidgets(
    'changing an option drops the stale preview, and notes show a warning',
    (tester) async {
      await pumpPage(tester);
      await tester.tap(find.text('Preview'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Include check-in notes'));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('bundle-preview')), findsNothing);
      expect(find.text(ExportPage.notesWarning), findsOneWidget);
    },
  );

  testWidgets('with no categories chosen, preview is off', (tester) async {
    await pumpPage(tester);
    for (final c in BundleOptions.offered) {
      await tester.tap(find.text(ExportPage.categoryLabel(c)));
      await tester.pump();
    }
    final preview = find.widgetWithText(FilledButton, 'Preview');
    expect(tester.widget<FilledButton>(preview).onPressed, isNull);
  });

  testWidgets('download everything saves the JSON the server returns', (
    tester,
  ) async {
    h.api.onBytes(
      '/export/full',
      (_) => Uint8List.fromList(utf8.encode('{"profile":{}}')),
    );
    await pumpPage(tester);
    await tester.tap(find.text('Download everything (JSON)'));
    await tester.pumpAndSettle();

    expect(saved.single.$2, endsWith('.json'));
    expect(saved.single.$3, 'application/json');
    expect(utf8.decode(saved.single.$1), '{"profile":{}}');
  });

  testWidgets('a failed preview shows the server message', (tester) async {
    h.api.fail(
      'POST',
      '/export/bundle',
      const ApiException('Server busy.', statusCode: 503),
    );
    await pumpPage(tester);
    await tester.tap(find.text('Preview'));
    await tester.pumpAndSettle();
    expect(find.text('Server busy.'), findsOneWidget);
  });

  testWidgets('renders in dark', (tester) async {
    await pumpPage(tester, theme: AppTheme.dark);
    expect(find.text('Summary for another model'), findsOneWidget);
  });
}
