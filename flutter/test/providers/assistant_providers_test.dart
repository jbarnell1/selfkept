import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/assistant.dart';
import 'package:selfkept/providers/assistant_chat_provider.dart';
import 'package:selfkept/providers/assistant_chat_session.dart';
import 'package:selfkept/providers/assistant_checkin_provider.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/fake_event_stream.dart';
import '../helpers/pump.dart';

void main() {
  late TestHarness h;
  var changes = 0;
  var moods = 0;

  setUp(() async {
    h = TestHarness();
    await signInWithAssistant(h);
    changes = 0;
    moods = 0;
    h.recordChanges.addListener(() => changes += 1);
  });
  tearDown(() => h.dispose());

  AssistantChatProvider chat() => AssistantChatProvider(
    conversationId: 'c1',
    service: h.assistant.service,
    changes: h.recordChanges,
    onMoodChanged: () => moods += 1,
  );

  group('AssistantChatProvider', () {
    setUp(() {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(messages: []),
      );
    });

    test('loads the conversation and its saved items', () async {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(),
      );
      final p = chat();
      await p.load();
      expect(p.items, hasLength(2));
      expect(p.items.first.captures.single.kind, 'food');
      expect(p.title, 'Long run this morning');
      expect(p.canSend, isTrue);
      p.dispose();
    });

    test('a check-in conversation is read only', () async {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(kind: 'checkin', messages: []),
      );
      final p = chat();
      await p.load();
      expect(p.readOnly, isTrue);
      expect(p.canSend, isFalse);
      expect(await p.send('hello'), isFalse);
      p.dispose();
    });

    test('streams: capture, deltas, done', () async {
      final p = chat();
      await p.load();
      h.streams.answer(200);
      final sent = p.send('  Had salmon for lunch  ');
      await pumpEventQueue();
      expect(p.isBusy, isTrue);
      expect(p.canSend, isFalse);
      expect(h.streams.requests.single.json, {'text': 'Had salmon for lunch'});
      final reply = p.items.last;
      expect(reply.state, ChatItemState.streaming);

      h.streams.frame(
        'capture',
        StreamFixtures.capture(entries: [StreamFixtures.entry()]),
      );
      await pumpEventQueue();
      expect(p.items.first.messageId, 'm-user');
      expect(p.items.first.captures.single.text, 'Salmon and rice');
      expect(changes, 1);

      h.streams.frame('delta', {'text': 'Saved to today\'s log.'});
      await pumpEventQueue();
      expect(reply.text, 'Saved to today\'s log.');
      h.streams.frame('delta', {'text': ' Nice choice.'});
      await pumpEventQueue();
      expect(reply.text, 'Saved to today\'s log. Nice choice.');

      h.streams.frame(
        'done',
        StreamFixtures.done(text: 'Saved to today\'s log. Nice choice.'),
      );
      await h.streams.close();
      expect(await sent, isTrue);
      expect(reply.state, ChatItemState.done);
      expect(reply.origin, MessageOrigin.model);
      expect(reply.messageId, 'm-reply');
      expect(reply.safety, isFalse);
      expect(p.isBusy, isFalse);
      p.dispose();
    });

    test('replace swaps what was shown', () async {
      final p = chat();
      await p.load();
      h.streams.answer(200);
      final sent = p.send('question');
      await pumpEventQueue();
      h.streams
        ..frame('capture', StreamFixtures.capture())
        ..frame('delta', {'text': 'You probably have a cold.'});
      await pumpEventQueue();
      expect(p.items.last.text, 'You probably have a cold.');
      h.streams
        ..frame('replace', {
          'text': 'I held that answer back because it did not pass my checks.',
          'reason': 'guard',
        })
        ..frame(
          'done',
          StreamFixtures.done(
            text: 'I held that answer back because it did not pass my checks.',
            origin: 'guard',
          ),
        );
      await h.streams.close();
      await sent;
      expect(p.items.last.text, startsWith('I held that answer back'));
      expect(p.items.last.safety, isFalse);
      expect(changes, 0);
      p.dispose();
    });

    test('a red-flag reply is marked as safety', () async {
      final p = chat();
      await p.load();
      h.streams.answer(200);
      final sent = p.send('I have chest pain');
      await pumpEventQueue();
      streamReply(
        h.streams,
        deltas: ['Call your local emergency number now.'],
        origin: 'guard',
      );
      await h.streams.close();
      await sent;
      expect(p.items.last.safety, isTrue);
      p.dispose();
    });

    test('undo takes the saved items back and tells the app', () async {
      h.api.on(
        'DELETE',
        '/assistant/messages/m-user/captures',
        (_) => {'messageId': 'm-user', 'removedEntries': 1},
      );
      final p = chat();
      await p.load();
      h.streams.answer(200);
      final sent = p.send('Had salmon');
      await pumpEventQueue();
      streamReply(h.streams, entries: [StreamFixtures.entry()]);
      await h.streams.close();
      await sent;
      changes = 0;
      expect(await p.undo(p.items.first), isTrue);
      expect(p.items.first.capturesUndone, isTrue);
      expect(changes, 1);
      expect(
        h.api.calls,
        contains('DELETE /assistant/messages/m-user/captures'),
      );
      expect(await p.undo(p.items.first), isFalse);
      p.dispose();
    });

    test('undo of a mood capture reloads the mood check-ins', () async {
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(
          messages: [
            AssistantFixtures.message(
              captures: [
                {'type': 'mood', 'id': 'k1', 'kind': 'mood', 'tags': []},
              ],
            ),
          ],
        ),
      );
      h.api.on('DELETE', '/assistant/messages/m1/captures', (_) => {});
      final p = chat();
      await p.load();
      await p.undo(p.items.first);
      expect(moods, 1);
      p.dispose();
    });

    test('a failed undo says why and keeps the items', () async {
      h.api.fail(
        'DELETE',
        '/assistant/messages/m1/captures',
        const ApiException('No such message.', statusCode: 404),
      );
      h.api.on(
        'GET',
        '/assistant/conversations/c1',
        (_) => AssistantFixtures.conversation(),
      );
      final p = chat();
      await p.load();
      expect(await p.undo(p.items.first), isFalse);
      expect(p.undoError, 'No such message.');
      expect(p.items.first.capturesUndone, isFalse);
      p.dispose();
    });

    test('a refusal before the stream removes the turn and says why', () async {
      final p = chat();
      await p.load();
      h.streams.answer(
        400,
        body: '{"title":"Validation failed","detail":"Say something first."}',
      );
      expect(await p.send('x'), isFalse);
      expect(p.items, isEmpty);
      expect(p.sendError, 'Say something first.');
      expect(p.canSend, isTrue);
      p.dispose();
    });

    test('a dropped stream keeps what arrived and can try again', () async {
      h.api.on(
        'DELETE',
        '/assistant/messages/m-user/captures',
        (_) => {'messageId': 'm-user', 'removedEntries': 1},
      );
      final p = chat();
      await p.load();
      h.streams.answer(200);
      final sent = p.send('Had salmon');
      await pumpEventQueue();
      h.streams
        ..frame(
          'capture',
          StreamFixtures.capture(entries: [StreamFixtures.entry()]),
        )
        ..frame('delta', {'text': 'Saved.'});
      await pumpEventQueue();
      h.streams.drop();
      expect(await sent, isTrue);
      final reply = p.items.last;
      expect(reply.state, ChatItemState.dropped);
      expect(reply.text, 'Saved.');
      expect(p.isBusy, isFalse);

      h.streams.answer(200);
      final again = p.retry(reply);
      await pumpEventQueue();
      expect(
        h.api.calls,
        contains('DELETE /assistant/messages/m-user/captures'),
      );
      expect(h.streams.requests, hasLength(2));
      expect(p.items, hasLength(2));
      streamReply(h.streams, deltas: ['Noted.']);
      await h.streams.close();
      expect(await again, isTrue);
      expect(p.items.last.text, 'Noted.');
      expect(p.items.last.state, ChatItemState.done);
      p.dispose();
    });

    test('a stream that ends without done counts as dropped', () async {
      final p = chat();
      await p.load();
      h.streams.answer(200);
      final sent = p.send('hello');
      await pumpEventQueue();
      h.streams.frame('capture', StreamFixtures.capture());
      await h.streams.close();
      await sent;
      expect(p.items.last.state, ChatItemState.dropped);
      p.dispose();
    });

    test('disposing stops reading the reply', () async {
      final p = chat();
      await p.load();
      h.streams.answer(200);
      final sent = p.send('hello');
      await pumpEventQueue();
      h.streams.frame('capture', StreamFixtures.capture());
      await pumpEventQueue();
      p.dispose();
      expect(await sent, isFalse);
      expect(h.streams.cancelled, isTrue);
    });
  });

  group('AssistantCheckinProvider', () {
    AssistantCheckinProvider checkin() => AssistantCheckinProvider(
      service: h.assistant.service,
      changes: h.recordChanges,
      onMoodChanged: () => moods += 1,
    );

    test('opening a new check-in starts it', () async {
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(),
      );
      h.api.on('POST', '/assistant/checkin', (_) => AssistantFixtures.turn());
      final p = checkin();
      await p.load();
      expect(h.api.bodies.last, <String, dynamic>{});
      expect(p.items.single.text, startsWith('Let\'s close the day.'));
      expect(p.topic, CheckinTopic.eating);
      expect(p.quickReplies.map((q) => q.text), ['none today', 'skip']);
      expect(p.canSend, isTrue);
      p.dispose();
    });

    test('an answer saves, moves on and offers mood replies', () async {
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(
          status: 'partial',
          conversationId: 'ck1',
          topic: 'eating',
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
              id: 'q1',
              role: 'assistant',
              content: 'How was your eating today?',
              origin: 'template',
            ),
          ],
        ),
      );
      final p = checkin();
      await p.load();
      expect(p.items, hasLength(1));
      expect(h.api.calls.where((c) => c == 'POST /assistant/checkin'), isEmpty);

      h.api.on(
        'POST',
        '/assistant/checkin',
        (_) => AssistantFixtures.turn(
          topic: 'mood',
          reply: 'Good balance today. And how was your mood today?',
          captured: [StreamFixtures.entry(id: 'j9')],
          userMessageId: 'u1',
          done: ['eating'],
          remaining: ['mood'],
        ),
      );
      changes = 0;
      expect(await p.send('salmon, rice and greens'), isTrue);
      expect(h.api.bodies.last, {'answer': 'salmon, rice and greens'});
      expect(p.items, hasLength(3));
      expect(p.items[1].messageId, 'u1');
      expect(p.items[1].captures.single.id, 'j9');
      expect(p.items.last.text, contains('mood'));
      expect(p.quickReplies.map((q) => q.label), [
        'Great',
        'Good',
        'Okay',
        'Meh',
        'Low',
        'Very low',
        'Skip',
      ]);
      expect(changes, 1);

      h.api.on(
        'POST',
        '/assistant/checkin',
        (_) => AssistantFixtures.turn(
          status: 'done',
          topic: null,
          reply: 'That\'s the day. Everything you told me is saved.',
          mood: 5,
          userMessageId: 'u2',
          done: ['eating', 'mood'],
          remaining: [],
        ),
      );
      await p.send('good');
      expect(p.items[3].captures.single.type, CaptureType.mood);
      expect(p.items[3].captures.single.text, 'Good');
      expect(moods, 1);
      expect(p.isFinished, isTrue);
      expect(p.canSend, isFalse);
      expect(p.quickReplies, isEmpty);
      p.dispose();
    });

    test('leave keeps everything and resume picks it up', () async {
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(),
      );
      h.api.on('POST', '/assistant/checkin', (_) => AssistantFixtures.turn());
      final p = checkin();
      await p.load();
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
      expect(await p.leave(), isTrue);
      expect(h.api.bodies.last, {'leave': true});
      expect(p.isPaused, isTrue);
      expect(p.status, CheckinStatus.partial);
      h.api.on('POST', '/assistant/checkin', (_) => AssistantFixtures.turn());
      await p.resume();
      expect(h.api.bodies.last, <String, dynamic>{});
      expect(p.isPaused, isFalse);
      p.dispose();
    });

    test('a red flag answer is marked and stops the check-in', () async {
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(),
      );
      h.api.on('POST', '/assistant/checkin', (_) => AssistantFixtures.turn());
      final p = checkin();
      await p.load();
      h.api.on(
        'POST',
        '/assistant/checkin',
        (_) => AssistantFixtures.turn(
          topic: null,
          reply: 'Call your local emergency number now.',
          redFlag: true,
          userMessageId: 'u1',
          remaining: [],
        ),
      );
      await p.send('crushing chest pain');
      expect(p.items.last.safety, isTrue);
      expect(p.redFlag, isTrue);
      expect(p.isPaused, isTrue);
      p.dispose();
    });

    test('a failed answer is taken back off the screen', () async {
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(),
      );
      h.api.on('POST', '/assistant/checkin', (_) => AssistantFixtures.turn());
      final p = checkin();
      await p.load();
      h.api.fail('POST', '/assistant/checkin', const ApiException.network());
      expect(await p.send('pasta'), isFalse);
      expect(p.items, hasLength(1));
      expect(p.sendError, isNotNull);
      p.dispose();
    });
  });

  group('AssistantTodayProvider', () {
    test('loads the brief and the check-in and reloads on a change', () async {
      h.api.on('GET', '/assistant/brief', (_) => AssistantFixtures.brief());
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(),
      );
      final today = h.assistant.today;
      await today.load();
      expect(today.brief!.items, hasLength(2));
      expect(today.checkin!.status, CheckinStatus.notStarted);
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(status: 'partial'),
      );
      h.recordChanges.changed(Object());
      await pumpEventQueue();
      expect(today.checkin!.status, CheckinStatus.partial);
    });

    test('a failure is kept apart for each card', () async {
      h.api.fail(
        'GET',
        '/assistant/brief',
        const ApiException(
          'No person profile for this account.',
          statusCode: 404,
        ),
      );
      h.api.on(
        'GET',
        '/assistant/checkin/today',
        (_) => AssistantFixtures.checkinToday(),
      );
      final today = h.assistant.today;
      await today.load();
      expect(today.brief, isNull);
      expect(today.briefError, isNotNull);
      expect(today.checkin, isNotNull);
    });
  });

  group('AssistantProvider', () {
    test('status and list; create and delete', () async {
      h.api.on(
        'GET',
        '/assistant/status',
        (_) => AssistantFixtures.status(reachable: false),
      );
      h.api.on(
        'GET',
        '/assistant/conversations',
        (_) => [AssistantFixtures.summary()],
      );
      h.api.on(
        'POST',
        '/assistant/conversations',
        (_) => AssistantFixtures.summary(id: 'c2', title: 'New conversation'),
      );
      h.api.on('DELETE', '/assistant/conversations/c1', (_) => null);
      final a = h.assistant.assistant;
      await a.load();
      expect(a.modelQuiet, isTrue);
      expect(a.conversations, hasLength(1));
      final created = await a.create();
      expect(created!.id, 'c2');
      expect(a.conversations.first.id, 'c2');
      expect(await a.delete('c1'), isTrue);
      expect(a.conversations.map((c) => c.id), ['c2']);
    });

    test('a failed status does not claim the model is quiet', () async {
      h.api.on('GET', '/assistant/conversations', (_) => []);
      final a = h.assistant.assistant;
      await a.load();
      expect(a.status, isNull);
      expect(a.modelQuiet, isFalse);
      expect(a.hasLoaded, isTrue);
    });
  });

  test('the day log reloads when the assistant saves something', () async {
    h.api.on('GET', '/journal', (_) => <Map<String, dynamic>>[]);
    h.api.on('GET', '/journal/tags', (_) => <Map<String, dynamic>>[]);
    await h.journal.load();
    final before = h.api.calls.where((c) => c == 'GET /journal').length;
    h.recordChanges.changed(Object());
    await pumpEventQueue();
    expect(h.api.calls.where((c) => c == 'GET /journal').length, before + 1);
  });
}
