import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/assistant.dart';
import 'package:selfkept/models/assistant_lookup.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/assistant_fixtures.dart';

Map<String, dynamic> _as(Map<String, dynamic> json, bool pascal) =>
    pascal ? Map<String, dynamic>.from(pascalCase(json) as Map) : json;

void main() {
  for (final pascal in [false, true]) {
    final casing = pascal ? 'PascalCase' : 'camelCase';

    group('lookup models ($casing)', () {
      test('status', () {
        final s = LookupStatus.fromJson(
          _as(LookupFixtures.status(localModel: true), pascal),
        );
        expect(s.available, isTrue);
        expect(s.enabled, isFalse);
        expect(s.provider, 'Anthropic');
        expect(s.model, 'claude-opus-5-5');
        expect(s.localModel, isTrue);
      });

      test('draft with its changes', () {
        final d = LookupDraft.fromJson(
          _as(LookupFixtures.draft(reworded: true), pascal),
        );
        expect(d.mode, LookupMode.anonymized);
        expect(d.text, contains('magnesium'));
        expect(d.reworded, isTrue);
        expect(d.provider, 'Anthropic');
        expect(d.changes.map((c) => c.label), [
          'your name',
          '2 dates',
          '3 numbers rounded',
        ]);
        final detailed = LookupDraft.fromJson(
          _as(LookupFixtures.draft(mode: 'detailed'), pascal),
        );
        expect(detailed.mode, LookupMode.detailed);
        expect(detailed.changes.last.label, 'details from your record added');
      });

      test('history row', () {
        final r = LookupRecord.listFromJson([
          _as(
            LookupFixtures.record(status: 'failed', mode: 'detailed'),
            pascal,
          ),
        ]).single;
        expect(r.id, 'lk1');
        expect(r.conversationId, 'c1');
        expect(r.mode, LookupMode.detailed);
        expect(r.status, LookupState.failed);
        expect(r.status.displayName, 'Not answered');
        expect(r.provider, 'Anthropic');
        expect(r.sentAt, DateTime.utc(2026, 11, 10, 3, 10));
      });

      test('a cloud answer carries sources and its lookup', () {
        final m = AssistantMessage.fromJson(
          _as(LookupFixtures.answer(), pascal),
        );
        expect(m.origin, MessageOrigin.cloud);
        expect(m.origin.displayName, 'Cloud model');
        expect(m.lookupId, 'lk1');
        expect(m.sources, hasLength(2));
        expect(m.sources.first.label, 'Magnesium and sleep, a review');
        expect(
          m.sources.first.link,
          Uri.parse('https://example.test/magnesium'),
        );
        expect(m.sources.last.link, isNull);
        expect(m.sources.last.label, 'ftp://example.test/plain');
        final gone = AssistantMessage.fromJson(
          _as(LookupFixtures.answer(lookupId: null), pascal),
        );
        expect(gone.lookupId, isNull);
      });

      test('an ordinary message has no sources and no lookup', () {
        final m = AssistantMessage.fromJson(
          _as(AssistantFixtures.message(), pascal),
        );
        expect(m.sources, isEmpty);
        expect(m.lookupId, isNull);
      });

      test('the chat done event carries the offer', () {
        final e = DoneEvent.fromJson(_as(LookupFixtures.offeredDone(), pascal));
        expect(e.lookupOffered, isTrue);
        expect(e.lookup!.reason, 'question');
        final none = DoneEvent.fromJson(
          _as(<String, dynamic>{
            'conversationId': 'c1',
            'messageId': 'm',
            'userMessageId': 'u',
            'origin': 'model',
            'text': 'x',
            'lookup': null,
          }, pascal),
        );
        expect(none.lookup, isNull);
        expect(none.lookupOffered, isFalse);
      });

      test('stream events', () {
        final done = LookupDoneEvent.fromJson(
          _as(LookupFixtures.done(), pascal),
        );
        expect(done.lookupId, 'lk1');
        expect(done.message.origin, MessageOrigin.cloud);
        expect(done.applied!.origin, MessageOrigin.model);
        final bare = LookupDoneEvent.fromJson(
          _as(LookupFixtures.done(withApplied: false), pascal),
        );
        expect(bare.applied, isNull);
        final failed = LookupFailedEvent.fromJson(
          _as(<String, dynamic>{
            'lookupId': 'lk1',
            'reason': 'cut_off',
            'text': 'The answer was cut off.',
          }, pascal),
        );
        expect(failed.reason, LookupFailure.cutOff);
        expect(failed.text, 'The answer was cut off.');
        expect(
          LookupSentEvent.fromJson(_as({'lookupId': 'lk1'}, pascal)).lookupId,
          'lk1',
        );
      });
    });
  }

  group('enums', () {
    test('modes, kinds, failures', () {
      expect(LookupMode.fromString('DETAILED'), LookupMode.detailed);
      expect(LookupMode.fromString(null), LookupMode.anonymized);
      expect(LookupMode.detailed.toJson(), 'detailed');
      expect(LookupMode.detailed.displayName, 'With my details');
      expect(LookupChangeKind.fromString('phone'), LookupChangeKind.phone);
      expect(LookupChangeKind.fromString('mystery'), LookupChangeKind.other);
      expect(LookupChangeKind.name.toJson(), 'name');
      for (final f in LookupFailure.values) {
        expect(LookupFailure.fromString(f.wireValue), f);
      }
      expect(
        LookupFailure.fromString('search_failed').wireValue,
        'search_failed',
      );
      expect(
        LookupFailure.fromString('rateLimited'),
        LookupFailure.rateLimited,
      );
      expect(LookupFailure.fromString('???'), LookupFailure.unknown);
    });

    test('change words, one and many', () {
      String label(String kind, int count) => LookupChange(
        kind: LookupChangeKind.fromString(kind),
        count: count,
      ).label;
      expect(label('name', 1), 'your name');
      expect(label('name', 2), '2 names');
      expect(label('email', 1), 'an email address');
      expect(label('phone', 3), '3 phone numbers');
      expect(label('link', 1), 'a web address');
      expect(label('date', 1), 'a date');
      expect(label('age', 1), 'your age, as a decade');
      expect(label('number', 1), 'a number rounded');
      expect(
        const LookupChange(kind: LookupChangeKind.name).foundLabel,
        'a name',
      );
    });
  });

  group('LookupRefused.from', () {
    test('maps each status on each route', () {
      LookupRefusalKind? kind(int status, LookupRoute route, {Map? body}) =>
          LookupRefused.from(
            ApiException.fromResponse(status, body),
            route,
          )?.kind;
      expect(kind(404, LookupRoute.status), LookupRefusalKind.serverOff);
      expect(kind(404, LookupRoute.delete), LookupRefusalKind.missing);
      expect(kind(409, LookupRoute.draft), LookupRefusalKind.personOff);
      expect(kind(422, LookupRoute.draft), LookupRefusalKind.redFlag);
      expect(
        kind(422, LookupRoute.settings),
        LookupRefusalKind.notAcknowledged,
      );
      expect(kind(422, LookupRoute.send), LookupRefusalKind.notAcknowledged);
      expect(
        kind(
          422,
          LookupRoute.send,
          body: {
            'detail': 'The text holds a name.',
            'Findings': [
              {'Kind': 'name', 'Count': 1},
            ],
          },
        ),
        LookupRefusalKind.findings,
      );
      expect(kind(500, LookupRoute.send), isNull);
      expect(
        LookupRefused.from(const ApiException.network(), LookupRoute.send),
        isNull,
      );
      expect(LookupRefused.from(StateError('x'), LookupRoute.send), isNull);
    });
  });
}
