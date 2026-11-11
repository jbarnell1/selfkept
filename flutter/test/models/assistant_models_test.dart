import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/assistant.dart';
import 'package:selfkept/models/connect.dart';
import 'package:selfkept/models/journal.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/fake_event_stream.dart';

Map<String, dynamic> _as(Map<String, dynamic> json, bool pascal) =>
    pascal ? Map<String, dynamic>.from(pascalCase(json) as Map) : json;

void main() {
  for (final pascal in [false, true]) {
    final casing = pascal ? 'PascalCase' : 'camelCase';

    group('assistant models ($casing)', () {
      test('status', () {
        final s = AssistantStatus.fromJson(
          _as(AssistantFixtures.status(), pascal),
        );
        expect(s.enabled, isTrue);
        expect(s.model, 'qwen3:8b');
        expect(s.modelAnswering, isTrue);
        final off = AssistantStatus.fromJson(
          _as(
            AssistantFixtures.status(enabled: false, reachable: false),
            pascal,
          ),
        );
        expect(off.modelAnswering, isFalse);
        expect(off.model, isNull);
      });

      test('conversation with messages and captures', () {
        final c = Conversation.fromJson(
          _as(AssistantFixtures.conversation(), pascal),
        );
        expect(c.id, 'c1');
        expect(c.summary.kind, ConversationKind.chat);
        expect(c.summary.title, 'Long run this morning');
        expect(c.summary.updatedAt, DateTime.utc(2026, 11, 10, 8, 5));
        expect(c.messages, hasLength(2));
        final user = c.messages.first;
        expect(user.role, MessageRole.user);
        expect(user.origin, MessageOrigin.code);
        expect(user.captures.single.type, CaptureType.journal);
        expect(user.captures.single.kind, 'food');
        expect(user.captures.single.tags, ['high_protein']);
        expect(user.capturesUndone, isFalse);
        expect(c.messages.last.origin, MessageOrigin.model);
      });

      test('a check-in summary keeps its day', () {
        final s = ConversationSummary.fromJson(
          _as(
            AssistantFixtures.summary(kind: 'checkin', day: '2026-11-10'),
            pascal,
          ),
        );
        expect(s.kind, ConversationKind.checkin);
        expect(s.day, DateTime(2026, 11, 10));
      });

      test('stream events', () {
        final capture = CaptureEvent.fromJson(
          _as(
            StreamFixtures.capture(
              entries: [StreamFixtures.entry()],
              remembered: [StreamFixtures.memory()],
            ),
            pascal,
          ),
        );
        expect(capture.messageId, 'm-user');
        expect(capture.entries.single.kind, JournalKind.food);
        expect(capture.remembered.single.type, MemoryType.preference);
        expect(capture.refs.map((r) => r.type), [
          CaptureType.journal,
          CaptureType.memory,
        ]);
        expect(capture.refs.last.text, 'Eats a vegetarian diet');

        final done = DoneEvent.fromJson(
          _as(StreamFixtures.done(origin: 'guard'), pascal),
        );
        expect(done.origin, MessageOrigin.guard);
        expect(done.userMessageId, 'm-user');
        final replace = ReplaceEvent.fromJson(
          _as({'text': 'Instead.', 'reason': 'guard'}, pascal),
        );
        expect(replace.text, 'Instead.');
        expect(replace.reason, 'guard');
      });

      test('check-in turn and today', () {
        final turn = CheckinTurn.fromJson(
          _as(
            AssistantFixtures.turn(
              topic: 'mood',
              mood: 5,
              userMessageId: 'u1',
              captured: [StreamFixtures.entry()],
              done: ['eating'],
              remaining: ['mood'],
            ),
            pascal,
          ),
        );
        expect(turn.topic, CheckinTopic.mood);
        expect(turn.mood, 5);
        expect(turn.userMessageId, 'u1');
        expect(turn.captured, hasLength(1));
        expect(turn.done, [CheckinTopic.eating]);
        expect(turn.remaining, [CheckinTopic.mood]);
        expect(turn.redFlag, isFalse);

        final noMood = CheckinTurn.fromJson(
          _as(AssistantFixtures.turn(), pascal),
        );
        expect(noMood.mood, isNull);

        final today = CheckinToday.fromJson(
          _as(
            AssistantFixtures.checkinToday(
              status: 'partial',
              conversationId: 'ck1',
              done: ['eating'],
            ),
            pascal,
          ),
        );
        expect(today.status, CheckinStatus.partial);
        expect(today.conversationId, 'ck1');
        expect(today.missing, [
          CheckinTopic.eating,
          CheckinTopic.drinks,
          CheckinTopic.mood,
        ]);
        expect(today.done, [CheckinTopic.eating]);
      });

      test('brief, playbook, nudges and model check', () {
        final brief = MorningBrief.fromJson(
          _as(AssistantFixtures.brief(), pascal),
        );
        expect(brief.items, hasLength(2));
        expect(brief.items.first.kind, 'sleep');
        expect(brief.isEmpty, isFalse);

        final entry = PlaybookEntry.fromJson(
          _as(
            AssistantFixtures.playbookEntry(cautions: ['Ask first.']),
            pascal,
          ),
        );
        expect(entry.key, 'sleep_seven_hours');
        expect(
          entry.sourceLink,
          Uri.parse('https://doi.org/10.5665/sleep.4716'),
        );
        expect(entry.cautions, ['Ask first.']);

        final nudges = NudgeSettings.fromJson(
          _as(AssistantFixtures.nudges(tokenSet: true), pascal),
        );
        expect(nudges.ntfyUrlMasked, 'https://ntfy.example.net/***');
        expect(nudges.tokenSet, isTrue);
        expect(nudges.eveningTime, '20:30');

        final check = ModelCheck.fromJson(
          _as(AssistantFixtures.modelCheck(), pascal),
        );
        expect(check.passedCount, 5);
        expect(check.cases.last.passed, isFalse);
        expect(check.cases.first.latencyMs, 812);

        final test = NudgeTestResult.fromJson(
          _as({'sent': false, 'error': 'The server answered 403.'}, pascal),
        );
        expect(test.error, 'The server answered 403.');
      });
    });
  }

  test('enums read their wire values', () {
    expect(CheckinStatus.fromString('not_started'), CheckinStatus.notStarted);
    expect(CheckinStatus.notStarted.toJson(), 'not_started');
    expect(CheckinTopic.fromString('nope'), isNull);
    expect(MessageOrigin.fromString('guard'), MessageOrigin.guard);
    expect(CaptureType.fromString('mood'), CaptureType.mood);
  });

  test('only an https source is a link', () {
    expect(const PlaybookEntry(key: 'k', sourceUrl: '').sourceLink, isNull);
    expect(
      const PlaybookEntry(key: 'k', sourceUrl: 'http://example.org').sourceLink,
      isNull,
    );
    expect(
      const PlaybookEntry(
        key: 'k',
        sourceUrl: 'javascript:alert(1)',
      ).sourceLink,
      isNull,
    );
  });

  test('the server says which replies answer a red flag', () {
    for (final key in ['redFlag', 'RedFlag']) {
      final message = AssistantMessage.fromJson({
        'id': 'm1',
        'role': 'assistant',
        'content': 'Call your local emergency number.',
        'origin': 'guard',
        key: true,
      });
      expect(message.redFlag, isTrue);
      final done = DoneEvent.fromJson({
        'conversationId': 'c1',
        'messageId': 'm1',
        'userMessageId': 'u1',
        'origin': 'guard',
        'text': 'Call your local emergency number.',
        key: true,
      });
      expect(done.redFlag, isTrue);
    }
    // A refused model answer is origin guard too, and is not a red flag.
    final refused = AssistantMessage.fromJson({
      'id': 'm2',
      'role': 'assistant',
      'content': 'Here is what your record says.',
      'origin': 'guard',
    });
    expect(refused.redFlag, isFalse);
  });

  test('the update body keeps null to mean "keep what is stored"', () {
    expect(const NudgeUpdate(enabled: true, ntfyToken: '').toJson(), {
      'enabled': true,
      'ntfyUrl': null,
      'ntfyToken': '',
      'eveningTime': null,
    });
  });
}
