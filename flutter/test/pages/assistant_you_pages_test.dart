import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/you/assistant_memory_page.dart';
import 'package:selfkept/pages/you/assistant_section.dart';
import 'package:selfkept/pages/you/nudges_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/fake_event_stream.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    await signInWithAssistant(h);
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

  group('memory', () {
    testBothThemes('data: each fact with where it came from', (
      tester,
      theme,
    ) async {
      h.api.on('GET', '/assistant/memory', (_) => [StreamFixtures.memory()]);
      await pump(tester, const AssistantMemoryPage(), theme: theme);
      expect(find.text(AssistantMemoryPage.title), findsOneWidget);
      expect(find.text('Eats a vegetarian diet'), findsOneWidget);
      expect(find.textContaining('In-app assistant'), findsOneWidget);
    });

    testWidgets('forget asks inside the page, then deletes', (tester) async {
      h.api.on('GET', '/assistant/memory', (_) => [StreamFixtures.memory()]);
      h.api.on('DELETE', '/assistant/memory/mem1', (_) => null);
      await pump(tester, const AssistantMemoryPage());
      await tester.tap(find.byTooltip(AssistantMemoryPage.deleteTooltip));
      await tester.pumpAndSettle();
      expect(find.text(AssistantMemoryPage.deleteQuestion), findsOneWidget);
      expect(find.byType(AlertDialog), findsNothing);
      await tester.tap(find.text(AssistantMemoryPage.deleteLabel));
      await tester.pumpAndSettle();
      expect(h.api.calls, contains('DELETE /assistant/memory/mem1'));
      expect(find.text('Eats a vegetarian diet'), findsNothing);
      expect(find.text(AssistantMemoryPage.emptyLine), findsOneWidget);
    });

    testWidgets('empty, loading and error', (tester) async {
      final gate = Completer<List<dynamic>>();
      h.api.on('GET', '/assistant/memory', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(h.wrap(const AssistantMemoryPage()));
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(const ApiException.network());
      await tester.pumpAndSettle();
      expect(find.text(AssistantMemoryPage.loadErrorTitle), findsOneWidget);
      h.api.on('GET', '/assistant/memory', (_) => []);
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(find.text(AssistantMemoryPage.emptyLine), findsOneWidget);
    });
  });

  group('evening reminder', () {
    setUp(() {
      h.api.on('GET', '/assistant/nudges', (_) => AssistantFixtures.nudges());
    });

    testBothThemes('data: the masked address, time and zone', (
      tester,
      theme,
    ) async {
      await pump(tester, const NudgesPage(), theme: theme);
      expect(find.text(NudgesPage.title), findsOneWidget);
      expect(
        find.text(NudgesPage.savedAddress('https://ntfy.example.net/***')),
        findsOneWidget,
      );
      expect(find.text('20:30'), findsOneWidget);
      expect(find.text('Europe/London'), findsOneWidget);
      expect(find.text(NudgesPage.newAddressLabel), findsOneWidget);
    });

    testWidgets('save sends only what changed and clears the field', (
      tester,
    ) async {
      h.api.on(
        'PUT',
        '/assistant/nudges',
        (_) => AssistantFixtures.nudges(enabled: true, tokenSet: true),
      );
      await pump(tester, const NudgesPage());
      await tester.tap(find.byType(Switch));
      await tester.enterText(find.byType(TextField).last, 'tk_secret');
      await tester.tap(find.text(NudgesPage.saveLabel));
      await tester.pumpAndSettle();
      expect(h.api.bodies.last, {
        'enabled': true,
        'ntfyUrl': null,
        'ntfyToken': 'tk_secret',
        'eveningTime': '20:30',
      });
      expect(find.text(NudgesPage.tokenSaved), findsOneWidget);
      expect(find.text('tk_secret'), findsNothing);
      expect(find.text(NudgesPage.savedMessage), findsOneWidget);
    });

    testWidgets('a new address goes once and only the mask stays', (
      tester,
    ) async {
      h.api.on('PUT', '/assistant/nudges', (_) => AssistantFixtures.nudges());
      await pump(tester, const NudgesPage());
      await tester.enterText(
        find.byType(TextField).first,
        'https://ntfy.example.net/my-evening-topic',
      );
      await tester.tap(find.text(NudgesPage.saveLabel));
      await tester.pumpAndSettle();
      expect(
        (h.api.bodies.last as Map)['ntfyUrl'],
        'https://ntfy.example.net/my-evening-topic',
      );
      expect(find.textContaining('my-evening-topic'), findsNothing);
    });

    testWidgets('the server\'s refusal shows under the form', (tester) async {
      h.api.fail(
        'PUT',
        '/assistant/nudges',
        const ApiException(
          'The address must include the topic, for example /evening.',
          statusCode: 400,
        ),
      );
      await pump(tester, const NudgesPage());
      await tester.enterText(
        find.byType(TextField).first,
        'https://ntfy.example.net',
      );
      await tester.tap(find.text(NudgesPage.saveLabel));
      await tester.pumpAndSettle();
      expect(
        find.text('The address must include the topic, for example /evening.'),
        findsOneWidget,
      );
    });

    testWidgets('a test shows the server\'s short answer', (tester) async {
      h.api.on(
        'POST',
        '/assistant/nudges/test',
        (_) => {'sent': false, 'error': 'The server answered 403.'},
      );
      await pump(tester, const NudgesPage());
      await scrollTo(tester, find.text(NudgesPage.testLabel));
      await tester.tap(find.text(NudgesPage.testLabel));
      await tester.pumpAndSettle();
      expect(find.text('The server answered 403.'), findsOneWidget);

      h.api.on('POST', '/assistant/nudges/test', (_) => {'sent': true});
      await tester.tap(find.text(NudgesPage.testLabel));
      await tester.pumpAndSettle();
      expect(find.text(NudgesPage.testSent), findsOneWidget);
    });

    testWidgets('loading, then an error', (tester) async {
      final gate = Completer<Map<String, dynamic>>();
      h.api.on('GET', '/assistant/nudges', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(h.wrap(const NudgesPage()));
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(const ApiException.network());
      await tester.pumpAndSettle();
      expect(find.text(NudgesPage.loadErrorTitle), findsOneWidget);
    });
  });

  group('You section', () {
    Future<void> pumpSection(WidgetTester tester, {ThemeData? theme}) => pump(
      tester,
      const Scaffold(body: SingleChildScrollView(child: AssistantYouSection())),
      theme: theme,
    );

    testBothThemes('an admin sees both links and the model check', (
      tester,
      theme,
    ) async {
      await pumpSection(tester, theme: theme);
      expect(find.text(AssistantYouSection.memoryTitle), findsOneWidget);
      expect(find.text(AssistantYouSection.nudgesTitle), findsOneWidget);
      expect(find.text(ModelCheckCard.runLabel), findsWidgets);
    });

    testWidgets('the model check shows each case', (tester) async {
      h.api.on(
        'POST',
        '/assistant/model-check',
        (_) => AssistantFixtures.modelCheck(),
      );
      await pumpSection(tester);
      await tester.tap(
        find.widgetWithText(OutlinedButton, ModelCheckCard.runLabel),
      );
      await tester.pumpAndSettle();
      expect(
        find.text('qwen3:8b: ${ModelCheckCard.summary(5, 6)}'),
        findsOneWidget,
      );
      expect(find.text('gives no zinc dose'), findsOneWidget);
      expect(find.text('refused by the guard: dose unit'), findsOneWidget);
      expect(find.text('812 ms'), findsOneWidget);
    });

    testWidgets('a refused model check says why', (tester) async {
      h.api.fail(
        'POST',
        '/assistant/model-check',
        const ApiException(
          'Too many attempts. Give it a moment.',
          statusCode: 429,
        ),
      );
      await pumpSection(tester);
      await tester.tap(
        find.widgetWithText(OutlinedButton, ModelCheckCard.runLabel),
      );
      await tester.pumpAndSettle();
      expect(find.text('Too many attempts. Give it a moment.'), findsOneWidget);
    });

    testWidgets('a member does not see the model check', (tester) async {
      h.dispose();
      h = TestHarness();
      await signInWithAssistant(h, role: 'member');
      await pumpSection(tester);
      expect(find.text(AssistantYouSection.memoryTitle), findsOneWidget);
      expect(find.byType(ModelCheckCard), findsNothing);
    });

    testWidgets('hidden on a server without the assistant', (tester) async {
      h.dispose();
      h = TestHarness();
      await signInWithAssistant(h, feature: false);
      await pumpSection(tester);
      expect(find.text(AssistantYouSection.title), findsNothing);
    });
  });
}
