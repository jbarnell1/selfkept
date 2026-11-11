import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/assistant.dart';
import 'package:selfkept/models/assistant_lookup.dart';
import 'package:selfkept/providers/assistant_chat_provider.dart';
import 'package:selfkept/providers/assistant_lookup_providers.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/fake_event_stream.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;

  setUp(() async {
    h = TestHarness();
    await signInWithAssistant(h, lookup: true);
    h.api.on(
      'POST',
      '/assistant/lookups/draft',
      (body) => LookupFixtures.draft(mode: (body! as Map)['mode'] as String),
    );
  });
  tearDown(() => h.dispose());

  LookupSheetProvider sheet() =>
      LookupSheetProvider(service: h.assistant.service, messageId: 'm-user');

  group('LookupSheetProvider', () {
    test('starts with the anonymized draft and nothing sent', () async {
      final p = sheet();
      expect(p.phase, LookupPhase.drafting);
      await p.start();
      expect(p.phase, LookupPhase.ready);
      expect(p.mode, LookupMode.anonymized);
      expect(p.provider, 'Anthropic');
      expect(p.text, contains('in their 40s'));
      expect(p.draftVersion, 1);
      expect(p.canSend, isTrue);
      expect(h.api.bodies.last, {'messageId': 'm-user', 'mode': 'anonymized'});
      expect(h.streams.requests, isEmpty);
      p.dispose();
    });

    test('switching mode fetches a new draft and replaces the text', () async {
      final p = sheet();
      await p.start();
      p.edit('my own words');
      await p.setMode(LookupMode.detailed);
      expect(p.mode, LookupMode.detailed);
      expect(p.text, contains('About me'));
      expect(p.draftVersion, 2);
      expect(p.draft!.changes.last.kind, LookupChangeKind.record);
      expect(h.api.bodies.last, {'messageId': 'm-user', 'mode': 'detailed'});
      expect(h.streams.requests, isEmpty);
      p.dispose();
    });

    test('a failed mode switch keeps the old draft and says why', () async {
      final p = sheet();
      await p.start();
      h.api.fail(
        'POST',
        '/assistant/lookups/draft',
        const ApiException.network(),
      );
      await p.setMode(LookupMode.detailed);
      expect(p.mode, LookupMode.anonymized);
      expect(p.phase, LookupPhase.ready);
      expect(p.draftError, isNotNull);
      expect(p.text, contains('in their 40s'));
      p.dispose();
    });

    test(
      'a red flag, a switched-off person and an error on the first draft',
      () async {
        for (final (status, kind) in [
          (422, LookupRefusalKind.redFlag),
          (409, LookupRefusalKind.personOff),
          (404, LookupRefusalKind.serverOff),
        ]) {
          h.api.fail(
            'POST',
            '/assistant/lookups/draft',
            ApiException('No.', statusCode: status),
          );
          final p = sheet();
          await p.start();
          expect(p.phase, LookupPhase.unavailable);
          expect(p.refusal!.kind, kind);
          expect(LookupSheetProvider.refusalLine(p.refusal!), isNotEmpty);
          expect(p.canSend, isFalse);
          p.dispose();
        }
        h.api.fail(
          'POST',
          '/assistant/lookups/draft',
          const ApiException.network(),
        );
        final p = sheet();
        await p.start();
        expect(p.phase, LookupPhase.unavailable);
        expect(p.refusal, isNull);
        expect(p.draftError, 'The server could not be reached.');
        h.api.on(
          'POST',
          '/assistant/lookups/draft',
          (_) => LookupFixtures.draft(),
        );
        await p.retryDraft();
        expect(p.phase, LookupPhase.ready);
        p.dispose();
      },
    );

    test(
      'send: sending, sent and waiting, then done with the answer',
      () async {
        final p = sheet();
        await p.start();
        p.edit('Does magnesium help with sleep?');
        h.streams.answer(200);
        final result = p.send();
        expect(p.phase, LookupPhase.sending);
        expect(p.isBusy, isTrue);
        expect(p.canEdit, isFalse);
        await pumpEventQueue();
        expect(h.streams.requests.single.json, {
          'messageId': 'm-user',
          'mode': 'anonymized',
          'text': 'Does magnesium help with sleep?',
          'acknowledged': true,
        });
        p.edit('changed while sending');
        expect(p.text, 'Does magnesium help with sleep?');
        h.streams.frame('sent', {'lookupId': 'lk1'});
        await pumpEventQueue();
        expect(p.phase, LookupPhase.waiting);
        h.streams.frame('waiting', {});
        await pumpEventQueue();
        expect(p.phase, LookupPhase.waiting);
        h.streams.frame('done', LookupFixtures.done());
        await h.streams.close();
        final done = await result;
        expect(done, isNotNull);
        expect(done!.message.origin, MessageOrigin.cloud);
        expect(p.phase, LookupPhase.done);
        expect(p.result, same(done));
        p.dispose();
      },
    );

    test(
      'failed keeps the sheet open with the reason; a retry is a fresh send',
      () async {
        final p = sheet();
        await p.start();
        h.streams.answer(
          200,
          body:
              'event: sent\ndata: {"lookupId":"lk1"}\n\n'
              'event: failed\ndata: ${jsonEncode({'lookupId': 'lk1', 'reason': 'refused', 'text': 'The cloud model declined to answer.'})}\n\n',
        );
        expect(await p.send(), isNull);
        expect(p.phase, LookupPhase.ready);
        expect(p.failure, 'The cloud model declined to answer.');
        expect(p.failureReason, LookupFailure.refused);
        expect(p.canSend, isTrue);

        h.streams.answer(
          200,
          body:
              'event: sent\ndata: {"lookupId":"lk2"}\n\n'
              'event: done\ndata: ${jsonEncode(LookupFixtures.done())}\n\n',
        );
        expect(await p.send(), isNotNull);
        expect(h.streams.requests, hasLength(2));
        expect(h.streams.requests.last.json['acknowledged'], isTrue);
        expect(p.failure, isNull);
        p.dispose();
      },
    );

    test('findings: shown, nothing stored, edit clears them', () async {
      final p = sheet();
      await p.start();
      h.streams.answer(
        422,
        body: jsonEncode({
          'title': 'The text holds a name.',
          'findings': [
            {'kind': 'name', 'count': 1},
          ],
        }),
      );
      expect(await p.send(), isNull);
      expect(p.phase, LookupPhase.ready);
      expect(p.findings.single.kind, LookupChangeKind.name);
      expect(p.failure, isNull);
      p.edit('Does magnesium help with sleep?');
      expect(p.findings, isEmpty);
      p.dispose();
    });

    test('a switched-off person on send says so', () async {
      final p = sheet();
      await p.start();
      h.streams.answer(409, body: '{"detail":"Off."}');
      expect(await p.send(), isNull);
      expect(
        p.draftError,
        LookupSheetProvider.refusalLine(
          const LookupRefused(LookupRefusalKind.personOff),
        ),
      );
      p.dispose();
    });

    test('a stream that ends without an answer is a dropped send', () async {
      final p = sheet();
      await p.start();
      h.streams.answer(200, body: 'event: sent\ndata: {"lookupId":"lk1"}\n\n');
      expect(await p.send(), isNull);
      expect(p.failure, LookupSheetProvider.droppedLine);
      expect(p.phase, LookupPhase.ready);
      p.dispose();
    });

    test('empty or overlong text cannot be sent', () async {
      final p = sheet();
      await p.start();
      p.edit('   ');
      expect(p.canSend, isFalse);
      p.edit('x' * (LookupSheetProvider.maxLength + 1));
      expect(p.canSend, isFalse);
      expect(await p.send(), isNull);
      expect(h.streams.requests, isEmpty);
      p.dispose();
    });

    test('closing while waiting stops reading', () async {
      final p = sheet();
      await p.start();
      h.streams.answer(200);
      final result = p.send();
      await pumpEventQueue();
      h.streams.frame('sent', {'lookupId': 'lk1'});
      await pumpEventQueue();
      p.dispose();
      expect(await result, isNull);
      expect(h.streams.cancelled, isTrue);
    });
  });

  group('AssistantLookupProvider', () {
    test('status, then turning on with the tick and off without', () async {
      final l = h.assistant.lookups;
      h.api.on('GET', '/assistant/lookup', (_) => LookupFixtures.status());
      await l.ensureStatus();
      await l.ensureStatus();
      expect(
        h.api.calls.where((c) => c == 'GET /assistant/lookup'),
        hasLength(1),
      );
      expect(l.providerName, 'Anthropic');
      h.api.on(
        'PUT',
        '/assistant/lookup',
        (body) =>
            LookupFixtures.status(enabled: (body! as Map)['enabled'] as bool),
      );
      expect(await l.setEnabled(true, acknowledged: true), isTrue);
      expect(l.status!.enabled, isTrue);
      expect(h.api.bodies.last, {'enabled': true, 'acknowledged': true});
      expect(await l.setEnabled(false), isTrue);
      expect(h.api.bodies.last, {'enabled': false, 'acknowledged': false});
    });

    test('a refused turn-on says to tick first', () async {
      final l = h.assistant.lookups;
      h.api.fail(
        'PUT',
        '/assistant/lookup',
        const ApiException('No.', statusCode: 422),
      );
      expect(await l.setEnabled(true), isFalse);
      expect(l.saveError, AssistantLookupProvider.acknowledgeFirst);
    });

    test('a 404 status is a refusal, not an error', () async {
      final l = h.assistant.lookups;
      h.api.fail(
        'GET',
        '/assistant/lookup',
        const ApiException('Not found.', statusCode: 404),
      );
      await l.loadStatus();
      expect(l.statusRefusal!.kind, LookupRefusalKind.serverOff);
      expect(l.statusError, isNull);
    });

    test('history, find one sent, delete, and one already gone', () async {
      final l = h.assistant.lookups;
      h.api.on(
        'GET',
        '/assistant/lookups',
        (_) => [LookupFixtures.record(), LookupFixtures.record(id: 'lk0')],
      );
      await l.loadHistory();
      expect(l.history, hasLength(2));
      expect((await l.findSent('lk0'))!.id, 'lk0');
      expect(await l.findSent('nope'), isNull);
      h.api.on('DELETE', '/assistant/lookups/lk1', (_) => null);
      expect(await l.delete('lk1'), isTrue);
      expect(l.history.map((r) => r.id), ['lk0']);
      // No handler: the fake answers 404, which means already gone.
      expect(await l.delete('lk0'), isTrue);
      expect(l.history, isEmpty);
      h.api.fail(
        'DELETE',
        '/assistant/lookups/lk9',
        const ApiException.network(),
      );
      expect(await l.delete('lk9'), isFalse);
      expect(l.deleteError, isNotNull);
    });

    test('signing out forgets everything', () async {
      final l = h.assistant.lookups;
      h.api.on('GET', '/assistant/lookup', (_) => LookupFixtures.status());
      await l.loadStatus();
      h.api.on('POST', '/auth/logout', (_) => null);
      await h.auth.logout();
      expect(l.status, isNull);
      expect(l.history, isEmpty);
    });
  });

  group('chat and the offer', () {
    setUp(() {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(messages: []),
      );
    });

    AssistantChatProvider chat() => AssistantChatProvider(
      conversationId: 'c1',
      service: h.assistant.service,
      changes: h.recordChanges,
    );

    test('a done event with the offer marks the reply and its question', () async {
      final p = chat();
      await p.load();
      h.streams.answer(
        200,
        body:
            'event: capture\ndata: ${jsonEncode(StreamFixtures.capture())}\n\n'
            'event: done\ndata: ${jsonEncode(LookupFixtures.offeredDone())}\n\n',
      );
      expect(await p.send('Does magnesium help with sleep?'), isTrue);
      final reply = p.items.last;
      expect(reply.lookupOffered, isTrue);
      expect(reply.questionMessageId, 'm-user');
      p.dispose();
    });

    test('no offer without the lookup object', () async {
      final p = chat();
      await p.load();
      h.streams.answer(
        200,
        body: 'event: done\ndata: ${jsonEncode(StreamFixtures.done())}\n\n',
      );
      await p.send('hello');
      expect(p.items.last.lookupOffered, isFalse);
      p.dispose();
    });

    test('the answer and the applied note are added; the offer goes', () async {
      final p = chat();
      await p.load();
      h.streams.answer(
        200,
        body:
            'event: done\ndata: ${jsonEncode(LookupFixtures.offeredDone())}\n\n',
      );
      await p.send('Does magnesium help with sleep?');
      final reply = p.items.last;
      var notified = 0;
      p.addListener(() => notified += 1);
      p.addLookupAnswer(
        LookupDoneEvent.fromJson(LookupFixtures.done()),
        from: reply,
        provider: 'Anthropic',
      );
      expect(reply.lookupOffered, isFalse);
      final cloud = p.items[p.items.length - 2];
      expect(cloud.isCloud, isTrue);
      expect(cloud.provider, 'Anthropic');
      expect(cloud.lookupId, 'lk1');
      expect(cloud.sources, hasLength(2));
      expect(p.items.last.origin, MessageOrigin.model);
      expect(p.items.last.isCloud, isFalse);
      expect(notified, 1);
      p.dispose();
    });

    test('a reloaded cloud answer keeps its sources and lookup', () async {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(
          messages: [AssistantFixtures.message(), LookupFixtures.answer()],
        ),
      );
      final p = chat();
      await p.load();
      expect(p.items.last.isCloud, isTrue);
      expect(p.items.last.lookupId, 'lk1');
      expect(p.items.last.sources.first.url, 'https://example.test/magnesium');
      p.dispose();
    });
  });
}
