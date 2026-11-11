import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/pages/assistant/assistant_chat_page.dart';
import 'package:selfkept/pages/you/assistant_section.dart';
import 'package:selfkept/pages/you/cloud_lookup_page.dart';
import 'package:selfkept/pages/you/sent_to_cloud_page.dart';
import 'package:selfkept/utils/api_error.dart';
import 'package:selfkept/widgets/assistant/chat_view.dart';
import 'package:selfkept/widgets/assistant/lookup_sheet.dart';
import 'package:selfkept/widgets/assistant/lookup_widgets.dart';
import 'package:selfkept/widgets/status_views.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/fake_event_stream.dart';
import '../helpers/pump.dart';

/// A stream answered after an awaited fake call is only delivered by the real
/// event loop, so give it a moment there before settling the frames.
Future<void> settleStream(WidgetTester tester) async {
  await tester.runAsync(
    () => Future<void>.delayed(const Duration(milliseconds: 20)),
  );
  await tester.pumpAndSettle();
}

bool _lookupCall(String call) => call.contains('/assistant/lookup');

void main() {
  late TestHarness h;
  final opened = <Uri>[];

  Future<void> start({bool lookup = true}) async {
    opened.clear();
    h = TestHarness(
      openUrl: (uri) async {
        opened.add(uri);
        return true;
      },
    );
    await signInWithAssistant(h, lookup: lookup);
    h.api.on('GET', '/assistant/status', (_) => AssistantFixtures.status());
    h.api.on('GET', '/checkins', (_) => <Map<String, dynamic>>[]);
    h.api.on(
      'GET',
      '/assistant/conversations/c1',
      (_) => AssistantFixtures.conversation(messages: []),
    );
    h.api.on('GET', '/assistant/lookup', (_) => LookupFixtures.status());
    h.api.on(
      'POST',
      '/assistant/lookups/draft',
      (body) => LookupFixtures.draft(mode: (body! as Map)['mode'] as String),
    );
    h.api.on('GET', '/assistant/lookups', (_) => [LookupFixtures.record()]);
  }

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

  /// Opens the chat and gets one reply whose done event carries the offer.
  Future<void> chatWithReply(
    WidgetTester tester, {
    ThemeData? theme,
    bool offered = true,
  }) async {
    await pump(
      tester,
      const AssistantChatPage(conversationId: 'c1'),
      theme: theme,
    );
    h.streams.answer(
      200,
      body:
          'event: capture\ndata: ${jsonEncode(StreamFixtures.capture())}\n\n'
          'event: done\ndata: ${jsonEncode(LookupFixtures.offeredDone(offered: offered))}\n\n',
    );
    await tester.enterText(
      find.byType(TextField),
      'Does magnesium help with sleep?',
    );
    await tester.tap(find.byTooltip(ChatComposer.sendTooltip));
    await settleStream(tester);
    expect(find.text('Magnesium is a common supplement.'), findsOneWidget);
  }

  Future<void> openSheet(WidgetTester tester) async {
    await tester.tap(find.text(LookupOfferLine.label));
    await tester.pumpAndSettle();
    expect(find.byType(LookupSheet), findsOneWidget);
  }

  Finder sheetField() => find.descendant(
    of: find.byType(LookupSheet),
    matching: find.byType(TextField),
  );

  group('the offer', () {
    testBothThemes('shows under a reply with the flag and the offer', (
      tester,
      theme,
    ) async {
      await start();
      await chatWithReply(tester, theme: theme);
      expect(find.text(LookupOfferLine.label), findsOneWidget);
      expect(h.api.calls.where(_lookupCall), isEmpty);
    });

    testWidgets('absent when the reply was not offered one', (tester) async {
      await start();
      await chatWithReply(tester, offered: false);
      expect(find.text(LookupOfferLine.label), findsNothing);
    });

    testWidgets('tapping it opens the sheet and sends nothing', (tester) async {
      await start();
      await chatWithReply(tester);
      final before = h.streams.requests.length;
      await openSheet(tester);
      expect(h.streams.requests, hasLength(before));
      expect(h.api.calls, contains('POST /assistant/lookups/draft'));
      expect(h.api.bodies.last, {'messageId': 'm-user', 'mode': 'anonymized'});
    });
  });

  group('the lookup sheet', () {
    testBothThemes(
      'band outside the scroll, anonymized first, send in the error colour',
      (tester, theme) async {
        await start();
        await chatWithReply(tester, theme: theme);
        await openSheet(tester);
        expect(find.text(LookupSheet.warning('Anthropic')), findsOneWidget);
        expect(find.text(LookupSheet.detailsLine), findsNothing);
        expect(
          find.ancestor(
            of: find.byType(LookupWarningBand),
            matching: find.byType(Scrollable),
          ),
          findsNothing,
        );
        final segmented = tester.widget<SegmentedButton<Object?>>(
          find.byWidgetPredicate((w) => w is SegmentedButton),
        );
        expect(segmented.selected.single.toString(), 'LookupMode.anonymized');
        expect(find.text('your name'), findsOneWidget);
        expect(find.text('2 dates'), findsOneWidget);
        expect(find.text('3 numbers rounded'), findsOneWidget);
        expect(find.text(LookupSheet.accountLine('Anthropic')), findsOneWidget);
        expect(find.text(LookupSheet.scrubberLine), findsOneWidget);
        expect(
          tester.widget<TextField>(sheetField()).controller!.text,
          'Does magnesium glycinate help with sleep in adults in their 40s?',
        );
        final send = tester.widget<ButtonStyleButton>(
          find.byKey(const ValueKey('lookup-send')),
        );
        final background = send.style!.backgroundColor!.resolve({});
        expect(background, theme.colorScheme.error);
        expect(background, isNot(theme.colorScheme.primary));
        expect(find.text(LookupSheet.sendLabel('Anthropic')), findsOneWidget);
        expect(find.text(LookupSheet.cancelLabel), findsOneWidget);
      },
    );

    testWidgets('With my details fetches a new draft and adds the band line', (
      tester,
    ) async {
      await start();
      await chatWithReply(tester);
      await openSheet(tester);
      await tester.tap(find.text('With my details'));
      await tester.pumpAndSettle();
      expect(h.api.bodies.last, {'messageId': 'm-user', 'mode': 'detailed'});
      expect(find.text(LookupSheet.detailsLine), findsOneWidget);
      expect(find.text('details from your record added'), findsOneWidget);
      expect(
        tester.widget<TextField>(sheetField()).controller!.text,
        contains('About me'),
      );
    });

    testWidgets('send: waiting, then the answer lands in the chat', (
      tester,
    ) async {
      await start();
      await chatWithReply(tester);
      await openSheet(tester);
      await tester.enterText(sheetField(), 'Does magnesium help sleep?');
      await tester.pump();
      h.streams.answer(200);
      await tester.tap(find.text(LookupSheet.sendLabel('Anthropic')));
      await pumpFrames(tester, frames: 2);
      expect(h.streams.requests.last.json, {
        'messageId': 'm-user',
        'mode': 'anonymized',
        'text': 'Does magnesium help sleep?',
        'acknowledged': true,
      });
      h.streams.frame('sent', {'lookupId': 'lk1'});
      await pumpFrames(tester, frames: 2);
      expect(find.text(LookupSheet.waitingLine('Anthropic')), findsOneWidget);
      expect(find.text(LookupSheet.closeNote), findsOneWidget);
      h.streams.frame('waiting', {});
      await pumpFrames(tester, frames: 2);
      expect(find.text(LookupSheet.waitingLine('Anthropic')), findsOneWidget);
      h.streams.frame('done', LookupFixtures.done());
      await h.streams.close();
      await tester.pumpAndSettle();
      expect(find.byType(LookupSheet), findsNothing);
      expect(find.byType(CloudAnswerBubble), findsOneWidget);
      expect(find.text(CloudAnswerBubble.fixedLine), findsOneWidget);
      expect(
        find.text(
          'Your sleep has been short this week, so this may be worth a try.',
        ),
        findsOneWidget,
      );
      expect(find.text(LookupOfferLine.label), findsNothing);
    });

    testWidgets('failed keeps the sheet open with the reason, then retries', (
      tester,
    ) async {
      await start();
      await chatWithReply(tester);
      await openSheet(tester);
      h.streams.answer(
        200,
        body:
            'event: sent\ndata: {"lookupId":"lk1"}\n\n'
            'event: failed\ndata: {"lookupId":"lk1","reason":"unreachable","text":"The cloud model could not be reached."}\n\n',
      );
      await tester.tap(find.text(LookupSheet.sendLabel('Anthropic')));
      await settleStream(tester);
      expect(find.byType(LookupSheet), findsOneWidget);
      expect(
        find.text('The cloud model could not be reached.'),
        findsOneWidget,
      );
      h.streams.answer(
        200,
        body:
            'event: sent\ndata: {"lookupId":"lk2"}\n\n'
            'event: done\ndata: ${jsonEncode(LookupFixtures.done())}\n\n',
      );
      final sent = h.streams.requests.length;
      await tester.tap(find.text(LookupSheet.sendLabel('Anthropic')));
      await settleStream(tester);
      expect(h.streams.requests, hasLength(sent + 1));
      expect(find.byType(LookupSheet), findsNothing);
      expect(find.byType(CloudAnswerBubble), findsOneWidget);
    });

    testWidgets('findings show what was found and nothing is added', (
      tester,
    ) async {
      await start();
      await chatWithReply(tester);
      await openSheet(tester);
      h.streams.answer(
        422,
        body: jsonEncode({
          'title': 'The text holds a name.',
          'findings': [
            {'kind': 'name', 'count': 1},
          ],
        }),
      );
      await tester.tap(find.text(LookupSheet.sendLabel('Anthropic')));
      await settleStream(tester);
      expect(find.byType(LookupSheet), findsOneWidget);
      expect(
        find.text(
          'This still contains a name, so it was not sent. Take it out, then '
          'send again.',
        ),
        findsOneWidget,
      );
      expect(find.byType(CloudAnswerBubble), findsNothing);
    });

    testWidgets('a red flag says it is never sent', (tester) async {
      await start();
      await chatWithReply(tester);
      h.api.fail(
        'POST',
        '/assistant/lookups/draft',
        const ApiException('Red flag.', statusCode: 422),
      );
      await openSheet(tester);
      expect(
        find.textContaining('never sent to a cloud model'),
        findsOneWidget,
      );
      expect(find.byKey(const ValueKey('lookup-send')), findsOneWidget);
      final send = tester.widget<ButtonStyleButton>(
        find.byKey(const ValueKey('lookup-send')),
      );
      expect(send.onPressed, isNull);
    });

    testWidgets('Cancel closes it and nothing is sent', (tester) async {
      await start();
      await chatWithReply(tester);
      final before = h.streams.requests.length;
      await openSheet(tester);
      await tester.tap(find.text(LookupSheet.cancelLabel));
      await tester.pumpAndSettle();
      expect(find.byType(LookupSheet), findsNothing);
      expect(h.streams.requests, hasLength(before));
      expect(find.text(LookupOfferLine.label), findsOneWidget);
    });
  });

  group('the cloud answer', () {
    Future<void> chatWithAnswer(
      WidgetTester tester, {
      ThemeData? theme,
      String? lookupId = 'lk1',
    }) async {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(
          messages: [
            AssistantFixtures.message(content: 'Does magnesium help?'),
            LookupFixtures.answer(lookupId: lookupId),
          ],
        ),
      );
      await pump(
        tester,
        const AssistantChatPage(conversationId: 'c1'),
        theme: theme,
      );
    }

    testBothThemes('set apart, with sources, What was sent and the line', (
      tester,
      theme,
    ) async {
      await start();
      await chatWithAnswer(tester, theme: theme);
      expect(find.text(CloudAnswerBubble.label), findsOneWidget);
      expect(find.text('Anthropic'), findsOneWidget);
      expect(find.text(CloudAnswerBubble.fixedLine), findsOneWidget);
      final box = tester.widget<DecoratedBox>(
        find.byKey(const ValueKey('cloud-answer')),
      );
      final border = (box.decoration as BoxDecoration).border! as Border;
      expect(border.top.color, theme.colorScheme.outline);

      await tester.tap(find.text('Magnesium and sleep, a review'));
      await tester.pumpAndSettle();
      expect(opened, [Uri.parse('https://example.test/magnesium')]);
      expect(find.text('ftp://example.test/plain'), findsOneWidget);

      await tester.tap(find.text(CloudAnswerBubble.sentLabel));
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      expect(
        find.text(
          'Does magnesium glycinate help with sleep in adults in their 40s?',
        ),
        findsOneWidget,
      );
      expect(find.textContaining('Anonymized, Anthropic'), findsOneWidget);
    });

    testWidgets('What was sent says so when the record is gone', (
      tester,
    ) async {
      await start();
      h.api.on('GET', '/assistant/lookups', (_) => <Map<String, dynamic>>[]);
      await chatWithAnswer(tester);
      await tester.tap(find.text(CloudAnswerBubble.sentLabel));
      await tester.pumpAndSettle();
      expect(find.text(WhatWasSentDialog.goneLine), findsOneWidget);
    });

    testWidgets('no What was sent row once the lookup was deleted', (
      tester,
    ) async {
      await start();
      await chatWithAnswer(tester, lookupId: null);
      expect(find.text(CloudAnswerBubble.label), findsOneWidget);
      expect(find.text(CloudAnswerBubble.sentLabel), findsNothing);
    });
  });

  group('Cloud lookup settings', () {
    testBothThemes('cannot be turned on without the tick', (
      tester,
      theme,
    ) async {
      await start();
      h.api.on(
        'PUT',
        '/assistant/lookup',
        (body) =>
            LookupFixtures.status(enabled: (body! as Map)['enabled'] as bool),
      );
      await pump(tester, const CloudLookupPage(), theme: theme);
      expect(find.text(CloudLookupPage.title), findsOneWidget);
      expect(find.text('Anthropic'), findsOneWidget);
      expect(find.text('claude-opus-5-5'), findsOneWidget);
      expect(tester.widget<Switch>(find.byType(Switch)).value, isFalse);

      await scrollTo(tester, find.byType(Switch));
      await tester.tap(find.byType(Switch));
      await tester.pumpAndSettle();
      expect(h.api.calls, isNot(contains('PUT /assistant/lookup')));
      expect(tester.widget<Switch>(find.byType(Switch)).value, isFalse);

      await tester.tap(find.byType(Checkbox));
      await tester.pumpAndSettle();
      await tester.tap(find.byType(Switch));
      await tester.pumpAndSettle();
      expect(h.api.bodies.last, {'enabled': true, 'acknowledged': true});
      expect(tester.widget<Switch>(find.byType(Switch)).value, isTrue);
      expect(find.byType(Checkbox), findsNothing);

      await tester.tap(find.byType(Switch));
      await tester.pumpAndSettle();
      expect(h.api.bodies.last, {'enabled': false, 'acknowledged': false});
      expect(tester.widget<Switch>(find.byType(Switch)).value, isFalse);
      expect(tester.widget<Checkbox>(find.byType(Checkbox)).value, isFalse);
    });

    testWidgets('links to Sent to the cloud', (tester) async {
      await start();
      await pump(tester, const CloudLookupPage());
      await scrollTo(tester, find.text(CloudLookupPage.sentTitle));
      expect(find.text(CloudLookupPage.sentTitle), findsOneWidget);
    });

    testWidgets('a server that says it is not available', (tester) async {
      await start();
      h.api.on(
        'GET',
        '/assistant/lookup',
        (_) => LookupFixtures.status(available: false),
      );
      await pump(tester, const CloudLookupPage());
      expect(find.text(CloudLookupPage.unavailableTitle), findsOneWidget);
      expect(find.byType(Switch), findsNothing);
    });

    testWidgets('loading, then an error with Try again', (tester) async {
      await start();
      final gate = Completer<Map<String, dynamic>>();
      h.api.on('GET', '/assistant/lookup', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(h.wrap(const CloudLookupPage()));
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(const ApiException.network());
      await tester.pumpAndSettle();
      expect(find.text(CloudLookupPage.loadErrorTitle), findsOneWidget);
    });
  });

  group('Sent to the cloud', () {
    testBothThemes('lists what went, and deletes after an in-app confirm', (
      tester,
      theme,
    ) async {
      await start();
      h.api.on('DELETE', '/assistant/lookups/lk1', (_) => null);
      await pump(tester, const SentToCloudPage(), theme: theme);
      expect(find.text(SentToCloudPage.title), findsOneWidget);
      expect(
        find.text(
          'Does magnesium glycinate help with sleep in adults in their 40s?',
        ),
        findsOneWidget,
      );
      expect(
        find.textContaining('Anonymized, Anthropic claude-opus-5-5'),
        findsOneWidget,
      );
      await tester.tap(find.byTooltip(SentToCloudPage.deleteTooltip));
      await tester.pumpAndSettle();
      expect(find.text(SentToCloudPage.deleteQuestion), findsOneWidget);
      expect(find.byType(AlertDialog), findsNothing);
      expect(h.api.calls, isNot(contains('DELETE /assistant/lookups/lk1')));
      await tester.tap(find.text(SentToCloudPage.deleteLabel));
      await tester.pumpAndSettle();
      expect(h.api.calls, contains('DELETE /assistant/lookups/lk1'));
      expect(find.text(SentToCloudPage.emptyLine), findsOneWidget);
    });

    testWidgets('Keep leaves it alone', (tester) async {
      await start();
      await pump(tester, const SentToCloudPage());
      await tester.tap(find.byTooltip(SentToCloudPage.deleteTooltip));
      await tester.pumpAndSettle();
      await tester.tap(find.text(SentToCloudPage.keepLabel));
      await tester.pumpAndSettle();
      expect(find.text(SentToCloudPage.deleteQuestion), findsNothing);
      expect(h.api.calls.where((c) => c.startsWith('DELETE')), isEmpty);
    });

    testWidgets('empty, loading and error', (tester) async {
      await start();
      final gate = Completer<List<dynamic>>();
      h.api.on('GET', '/assistant/lookups', (_) => gate.future);
      await usePhoneSize(tester);
      await tester.pumpWidget(h.wrap(const SentToCloudPage()));
      await pumpFrames(tester);
      expect(find.byType(LoadingView), findsOneWidget);
      gate.completeError(const ApiException.network());
      await tester.pumpAndSettle();
      expect(find.text(SentToCloudPage.loadErrorTitle), findsOneWidget);
      h.api.on('GET', '/assistant/lookups', (_) => <Map<String, dynamic>>[]);
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(find.text(SentToCloudPage.emptyLine), findsOneWidget);
    });
  });

  group('You section', () {
    testBothThemes('the Cloud lookup row with the flag', (tester, theme) async {
      await start();
      await pump(
        tester,
        const Scaffold(
          body: SingleChildScrollView(child: AssistantYouSection()),
        ),
        theme: theme,
      );
      expect(find.text(AssistantYouSection.lookupTitle), findsOneWidget);
    });
  });

  group('without assistant.lookup', () {
    testWidgets('nothing lookup-related renders and no lookup route is asked', (
      tester,
    ) async {
      await start(lookup: false);
      // A live reply that carries the offer anyway.
      await chatWithReply(tester);
      expect(find.text(LookupOfferLine.label), findsNothing);

      // A cloud answer kept in a conversation from before.
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(
          messages: [
            AssistantFixtures.message(content: 'Does magnesium help?'),
            LookupFixtures.answer(),
          ],
        ),
      );
      await tester.pumpWidget(const SizedBox());
      await pump(tester, const AssistantChatPage(conversationId: 'c1'));
      expect(find.text(CloudAnswerBubble.label), findsOneWidget);
      expect(find.text(CloudAnswerBubble.sentLabel), findsNothing);
      expect(find.text('Anthropic'), findsNothing);

      await tester.pumpWidget(const SizedBox());
      await pump(
        tester,
        const Scaffold(
          body: SingleChildScrollView(child: AssistantYouSection()),
        ),
      );
      expect(find.text(AssistantYouSection.lookupTitle), findsNothing);

      await tester.pumpWidget(const SizedBox());
      await pump(tester, const CloudLookupPage());
      expect(find.text(CloudLookupPage.unavailableTitle), findsOneWidget);
      expect(find.byType(Switch), findsNothing);

      await tester.pumpWidget(const SizedBox());
      await pump(tester, const SentToCloudPage());
      expect(find.text(CloudLookupPage.unavailableLine), findsOneWidget);

      expect(h.api.calls.where(_lookupCall), isEmpty);
      expect(
        h.streams.requests.where((r) => r.url.path.contains('lookup')),
        isEmpty,
      );
    });
  });
}
