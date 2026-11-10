import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/assistant/assistant_chat_page.dart';
import 'package:selfkept/pages/assistant/assistant_checkin_page.dart';
import 'package:selfkept/pages/assistant/playbook_page.dart';
import 'package:selfkept/providers/assistant_chat_provider.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/assistant/chat_view.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/fake_event_stream.dart';
import '../helpers/pump.dart';

/// A stream error after an awaited fake call is only delivered by the real
/// event loop, so give it a moment there before settling the frames.
Future<void> settleStream(WidgetTester tester) async {
  await tester.runAsync(
    () => Future<void>.delayed(const Duration(milliseconds: 20)),
  );
  await tester.pumpAndSettle();
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
    await signInWithAssistant(h);
    h.api.on('GET', '/assistant/status', (_) => AssistantFixtures.status());
    h.api.on(
      'GET',
      '/assistant/conversations/c1',
      (_) => AssistantFixtures.conversation(),
    );
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
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

  group('chat', () {
    testBothThemes('data: messages, saved chip with Undo, composer', (
      tester,
      theme,
    ) async {
      await pump(
        tester,
        const AssistantChatPage(conversationId: 'c1'),
        theme: theme,
      );
      expect(find.text('Long run this morning'), findsOneWidget);
      expect(find.text('Had salmon for lunch'), findsOneWidget);
      expect(find.text('Saved to today\'s log.'), findsOneWidget);
      expect(find.text('Food: High protein'), findsOneWidget);
      expect(find.text(CaptureChips.undoLabel), findsOneWidget);
      expect(find.byTooltip(ChatComposer.sendTooltip), findsOneWidget);
    });

    testWidgets('a reply streams in, then Undo takes the entry back', (
      tester,
    ) async {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(messages: []),
      );
      h.api.on(
        'DELETE',
        '/assistant/messages/m-user/captures',
        (_) => {'messageId': 'm-user', 'removedEntries': 1},
      );
      await pump(tester, const AssistantChatPage(conversationId: 'c1'));
      h.streams.answer(200);
      await tester.enterText(find.byType(TextField), 'Had two coffees');
      await tester.tap(find.byTooltip(ChatComposer.sendTooltip));
      await pumpFrames(tester, frames: 2);
      expect(find.text('Had two coffees'), findsOneWidget);
      expect(find.text(MessageBubble.thinking), findsOneWidget);

      h.streams.frame(
        'capture',
        StreamFixtures.capture(
          entries: [
            StreamFixtures.entry(kind: 'drink', text: null, tags: ['caffeine']),
          ],
        ),
      );
      h.streams.frame('delta', {'text': 'Saved to today\'s log.'});
      await pumpFrames(tester, frames: 2);
      expect(find.text('Drink: Caffeine'), findsOneWidget);
      expect(find.text('Saved to today\'s log.'), findsOneWidget);
      h.streams.frame('delta', {'text': ' Two is plenty.'});
      await pumpFrames(tester, frames: 2);
      expect(
        find.text('Saved to today\'s log. Two is plenty.'),
        findsOneWidget,
      );
      h.streams.frame(
        'done',
        StreamFixtures.done(text: 'Saved to today\'s log. Two is plenty.'),
      );
      await h.streams.close();
      await tester.pumpAndSettle();
      expect(find.text(MessageBubble.thinking), findsNothing);

      await tester.tap(find.text(CaptureChips.undoLabel));
      await tester.pumpAndSettle();
      expect(find.text(CaptureChips.undoneLine), findsOneWidget);
      expect(find.text('Drink: Caffeine'), findsNothing);
    });

    testBothThemes('a red-flag reply is shown in full with the care label', (
      tester,
      theme,
    ) async {
      const care =
          'This sounds like it could need urgent care, and it is not something I can judge from here. '
          'Call your local emergency number now or have someone take you to an emergency department.';
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(
          messages: [
            AssistantFixtures.message(content: 'chest pain'),
            AssistantFixtures.message(
              id: 'm2',
              role: 'assistant',
              content: care,
              origin: 'guard',
            ),
          ],
        ),
      );
      await pump(
        tester,
        const AssistantChatPage(conversationId: 'c1'),
        theme: theme,
      );
      expect(find.text(MessageBubble.safetyLabel), findsOneWidget);
      expect(find.text(care), findsOneWidget);
    });

    testWidgets('a dropped reply keeps its words and offers Try again', (
      tester,
    ) async {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(messages: []),
      );
      await pump(tester, const AssistantChatPage(conversationId: 'c1'));
      h.streams.answer(200);
      await tester.enterText(find.byType(TextField), 'hello');
      await tester.tap(find.byTooltip(ChatComposer.sendTooltip));
      await pumpFrames(tester, frames: 2);
      h.streams
        ..frame('capture', StreamFixtures.capture())
        ..frame('delta', {'text': 'Half a'});
      await pumpFrames(tester, frames: 2);
      h.streams.drop();
      await settleStream(tester);
      expect(find.text('Half a'), findsOneWidget);
      expect(find.text(MessageBubble.droppedLine), findsOneWidget);
      expect(find.text(MessageBubble.retryLabel), findsOneWidget);
    });

    testWidgets('a refused message comes back to the field with the reason', (
      tester,
    ) async {
      await pump(tester, const AssistantChatPage(conversationId: 'c1'));
      h.streams.answer(400, body: '{"detail":"Say something first."}');
      await tester.enterText(find.byType(TextField), 'x');
      await tester.tap(find.byTooltip(ChatComposer.sendTooltip));
      await settleStream(tester);
      expect(find.text('Say something first.'), findsOneWidget);
      expect(
        tester.widget<TextField>(find.byType(TextField)).controller!.text,
        'x',
      );
    });

    testWidgets('the home model note shows above the chat when quiet', (
      tester,
    ) async {
      h.api.on(
        'GET',
        '/assistant/status',
        (_) => AssistantFixtures.status(reachable: false),
      );
      await pump(tester, const AssistantChatPage(conversationId: 'c1'));
      expect(find.text(ModelQuietNote.text), findsOneWidget);
    });

    testWidgets('loading, then an error with Try again', (tester) async {
      final gate = Completer<Map<String, dynamic>>();
      h.api.on('GET', '/assistant/conversations/c1', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(
        h.wrap(const AssistantChatPage(conversationId: 'c1')),
      );
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(
        const ApiException('No such conversation.', statusCode: 404),
      );
      await tester.pumpAndSettle();
      expect(find.text(ChatView.loadErrorTitle), findsOneWidget);
      expect(find.text('No such conversation.'), findsOneWidget);
    });

    testWidgets('an old check-in opens to read only', (tester) async {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(kind: 'checkin'),
      );
      await pump(tester, const AssistantChatPage(conversationId: 'c1'));
      expect(find.text(AssistantChatProvider.readOnlyNote), findsOneWidget);
      expect(find.byType(TextField), findsNothing);
    });
  });

  group('check-in', () {
    setUp(() {
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(),
      );
      h.api.on('POST', '/assistant/checkin', (_) => AssistantFixtures.turn());
    });

    testBothThemes('opens with the first question and its quick replies', (
      tester,
      theme,
    ) async {
      await pump(tester, const AssistantCheckinPage(), theme: theme);
      expect(find.text(AssistantCheckinPage.title), findsOneWidget);
      expect(
        find.text('Let\'s close the day. How was your eating today?'),
        findsOneWidget,
      );
      expect(find.text('None today'), findsOneWidget);
      expect(find.text('Skip'), findsOneWidget);
      expect(find.text(AssistantCheckinPage.leaveLabel), findsOneWidget);
    });

    testWidgets('a quick reply answers, and mood offers its words', (
      tester,
    ) async {
      await pump(tester, const AssistantCheckinPage());
      h.api.on(
        'POST',
        '/assistant/checkin',
        (_) => AssistantFixtures.turn(
          topic: 'mood',
          reply: 'And how was your mood today?',
          remaining: ['mood'],
        ),
      );
      await tester.tap(find.text('Skip'));
      await tester.pumpAndSettle();
      expect(h.api.bodies.last, {'answer': 'skip'});
      expect(find.text('And how was your mood today?'), findsOneWidget);
      expect(find.text('Very low'), findsOneWidget);
      expect(find.text('Great'), findsOneWidget);
    });

    testWidgets('Leave for now keeps what was said and can pick up again', (
      tester,
    ) async {
      await pump(tester, const AssistantCheckinPage());
      h.api.on(
        'POST',
        '/assistant/checkin',
        (_) => AssistantFixtures.turn(
          topic: null,
          reply:
              'No problem. I have kept everything you told me, and today is marked partial.',
          remaining: [],
        ),
      );
      await tester.tap(find.text(AssistantCheckinPage.leaveLabel));
      await tester.pumpAndSettle();
      expect(h.api.bodies.last, {'leave': true});
      expect(find.text(AssistantCheckinPage.pausedLine), findsOneWidget);
      expect(find.text(AssistantCheckinPage.resumeLabel), findsOneWidget);
      expect(find.byType(TextField), findsNothing);
    });

    testBothThemes('a finished check-in says so', (tester, theme) async {
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(
          status: 'done',
          conversationId: 'ck1',
          done: ['eating', 'drinks', 'mood'],
        ),
      );
      h.api.on(
        'GET',
        '/assistant/conversations/ck1',
        (_) => AssistantFixtures.conversation(
          id: 'ck1',
          kind: 'checkin',
          messages: [
            AssistantFixtures.message(
              id: 'a9',
              role: 'assistant',
              content: 'That\'s the day. Everything you told me is saved.',
              origin: 'template',
            ),
          ],
        ),
      );
      await pump(tester, const AssistantCheckinPage(), theme: theme);
      expect(find.text(AssistantCheckinPage.finishedTitle), findsOneWidget);
      expect(find.text(AssistantCheckinPage.backLabel), findsOneWidget);
      expect(find.byType(TextField), findsNothing);
      expect(h.api.calls.where((c) => c == 'POST /assistant/checkin'), isEmpty);
    });

    testWidgets('a red flag answer is shown prominently', (tester) async {
      await pump(tester, const AssistantCheckinPage());
      h.api.on(
        'POST',
        '/assistant/checkin',
        (_) => AssistantFixtures.turn(
          topic: null,
          reply: 'Call your local emergency number now.',
          redFlag: true,
          remaining: [],
        ),
      );
      await tester.enterText(find.byType(TextField), 'chest pain');
      await tester.tap(find.byTooltip(ChatComposer.sendTooltip));
      await tester.pumpAndSettle();
      expect(find.text(MessageBubble.safetyLabel), findsOneWidget);
      expect(
        find.text('Call your local emergency number now.'),
        findsOneWidget,
      );
    });
  });

  group('playbook', () {
    testBothThemes('data: entry, why, caution and a source link', (
      tester,
      theme,
    ) async {
      h.api.on(
        'GET',
        '/assistant/playbook',
        (_) => [
          AssistantFixtures.playbookEntry(
            cautions: [
              'You have medicines on your list. Ask a pharmacist before adding a supplement.',
            ],
          ),
          AssistantFixtures.playbookEntry(sourceUrl: '')
            ..['key'] = 'alcohol_nights'
            ..['title'] = 'Set drinking nights beside dry ones'
            ..['source'] = 'the person\'s own record',
        ],
      );
      await pump(tester, const PlaybookPage(), theme: theme);
      expect(find.text(PlaybookPage.title), findsOneWidget);
      expect(find.text('Aim for seven hours'), findsOneWidget);
      expect(find.text('Last night was under 7 h.'), findsWidgets);
      expect(
        find.textContaining('Ask a pharmacist before adding a supplement.'),
        findsOneWidget,
      );
      await tester.tap(find.text('Watson 2015, Sleep 38(6):843-844'));
      await tester.pumpAndSettle();
      expect(opened.single, Uri.parse('https://doi.org/10.5665/sleep.4716'));

      await scrollTo(tester, find.text('the person\'s own record'));
      await tester.tap(find.text('the person\'s own record'));
      await tester.pumpAndSettle();
      expect(opened, hasLength(1));
    });

    testWidgets('empty: says nothing fits today', (tester) async {
      h.api.on('GET', '/assistant/playbook', (_) => []);
      await pump(tester, const PlaybookPage());
      expect(find.text(PlaybookPage.emptyLine), findsOneWidget);
    });

    testWidgets('error and loading', (tester) async {
      final gate = Completer<List<dynamic>>();
      h.api.on('GET', '/assistant/playbook', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(h.wrap(const PlaybookPage()));
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(const ApiException.network());
      await tester.pumpAndSettle();
      expect(find.text(PlaybookPage.loadErrorTitle), findsOneWidget);
    });
  });
}
