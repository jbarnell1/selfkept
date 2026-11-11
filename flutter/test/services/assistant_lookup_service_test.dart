import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/assistant_lookup.dart';
import 'package:selfkept/services/assistant_service.dart';
import 'package:selfkept/services/sse/event_stream_client.dart';
import 'package:selfkept/services/sse/sse_parser.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/assistant_fixtures.dart';
import '../helpers/fake_api_client.dart';
import '../helpers/fake_event_stream.dart';

void main() {
  late FakeApiClient api;
  late FakeEventStreamTransport streams;
  late AssistantService service;

  setUp(() {
    api = FakeApiClient();
    streams = FakeEventStreamTransport();
    service = AssistantService(
      api,
      EventStreamClient(
        transport: streams,
        serverUrl: () => 'http://fake.test',
        accessToken: () async => 'token',
        refresh: () async => false,
      ),
    );
  });

  Matcher refused(LookupRefusalKind kind) =>
      throwsA(isA<LookupRefused>().having((e) => e.kind, 'kind', kind));

  group('GET /assistant/lookup', () {
    test('reads the status', () async {
      api.on('GET', '/assistant/lookup', (_) => LookupFixtures.status());
      final s = await service.lookupStatus();
      expect(s.provider, 'Anthropic');
      expect(api.calls, ['GET /assistant/lookup']);
    });

    test('404 is the server switch off', () async {
      api.fail(
        'GET',
        '/assistant/lookup',
        const ApiException('Not found.', statusCode: 404),
      );
      await expectLater(
        service.lookupStatus(),
        refused(LookupRefusalKind.serverOff),
      );
    });

    test('a network failure stays an ApiException', () async {
      api.fail('GET', '/assistant/lookup', const ApiException.network());
      await expectLater(
        service.lookupStatus(),
        throwsA(isA<ApiException>().having((e) => e.isNetwork, 'net', isTrue)),
      );
    });
  });

  group('PUT /assistant/lookup', () {
    test('turning on sends the acknowledgement', () async {
      api.on(
        'PUT',
        '/assistant/lookup',
        (_) => LookupFixtures.status(enabled: true),
      );
      final s = await service.saveLookup(enabled: true, acknowledged: true);
      expect(s.enabled, isTrue);
      expect(api.bodies.single, {'enabled': true, 'acknowledged': true});
    });

    test('turning off needs no acknowledgement', () async {
      api.on('PUT', '/assistant/lookup', (_) => LookupFixtures.status());
      await service.saveLookup(enabled: false);
      expect(api.bodies.single, {'enabled': false, 'acknowledged': false});
    });

    test('422 is a missing acknowledgement', () async {
      api.fail(
        'PUT',
        '/assistant/lookup',
        const ApiException('Acknowledge first.', statusCode: 422),
      );
      await expectLater(
        service.saveLookup(enabled: true),
        refused(LookupRefusalKind.notAcknowledged),
      );
    });
  });

  group('POST /assistant/lookups/draft', () {
    test('sends the message and mode, reads the draft', () async {
      api.on(
        'POST',
        '/assistant/lookups/draft',
        (_) => LookupFixtures.draft(mode: 'detailed'),
      );
      final d = await service.lookupDraft('m-user', LookupMode.detailed);
      expect(d.mode, LookupMode.detailed);
      expect(api.bodies.single, {'messageId': 'm-user', 'mode': 'detailed'});
    });

    test(
      '409 is the person switch off, 422 a red flag, 404 server off',
      () async {
        api.fail(
          'POST',
          '/assistant/lookups/draft',
          const ApiException('Off.', statusCode: 409),
        );
        await expectLater(
          service.lookupDraft('m', LookupMode.anonymized),
          refused(LookupRefusalKind.personOff),
        );
        api.fail(
          'POST',
          '/assistant/lookups/draft',
          const ApiException('Red flag.', statusCode: 422),
        );
        await expectLater(
          service.lookupDraft('m', LookupMode.anonymized),
          refused(LookupRefusalKind.redFlag),
        );
        api.fail(
          'POST',
          '/assistant/lookups/draft',
          const ApiException('Not found.', statusCode: 404),
        );
        await expectLater(
          service.lookupDraft('m', LookupMode.anonymized),
          refused(LookupRefusalKind.serverOff),
        );
      },
    );
  });

  group('POST /assistant/lookups', () {
    test(
      'posts the text as shown with the acknowledgement and streams',
      () async {
        streams.answer(
          200,
          body:
              'event: sent\ndata: {"lookupId":"lk1"}\n\n'
              'event: waiting\ndata: {}\n\n'
              'event: mystery\ndata: {}\n\n'
              'event: done\ndata: ${jsonEncode(LookupFixtures.done())}\n\n',
        );
        final events = await service
            .sendLookup(
              messageId: 'm-user',
              mode: LookupMode.anonymized,
              text: 'Does it help?',
            )
            .toList();
        expect(events, [
          isA<LookupSentEvent>(),
          isA<LookupWaitingEvent>(),
          isA<LookupDoneEvent>(),
        ]);
        final request = streams.requests.single;
        expect(request.url.path, '/api/assistant/lookups');
        expect(request.json, {
          'messageId': 'm-user',
          'mode': 'anonymized',
          'text': 'Does it help?',
          'acknowledged': true,
        });
      },
    );

    test('a failed event keeps the reason and the text', () async {
      streams.answer(
        200,
        body:
            'event: sent\ndata: {"lookupId":"lk1"}\n\n'
            'event: failed\ndata: {"lookupId":"lk1","reason":"rate_limited","text":"Busy."}\n\n',
      );
      final events = await service
          .sendLookup(messageId: 'm', mode: LookupMode.anonymized, text: 'x')
          .toList();
      expect(
        events.last,
        isA<LookupFailedEvent>()
            .having((e) => e.reason, 'reason', LookupFailure.rateLimited)
            .having((e) => e.text, 'text', 'Busy.'),
      );
    });

    test('422 with findings is a typed refusal with what was found', () async {
      streams.answer(
        422,
        body: jsonEncode({
          'title': 'The text holds a name.',
          'status': 422,
          'findings': [
            {'kind': 'name', 'count': 1},
          ],
        }),
      );
      await expectLater(
        service
            .sendLookup(messageId: 'm', mode: LookupMode.anonymized, text: 'x')
            .toList(),
        throwsA(
          isA<LookupRefused>()
              .having((e) => e.kind, 'kind', LookupRefusalKind.findings)
              .having(
                (e) => e.findings.single.kind,
                'finding',
                LookupChangeKind.name,
              ),
        ),
      );
    });

    test('422 without findings, 409 and 404', () async {
      streams
        ..answer(422, body: '{"detail":"Acknowledge."}')
        ..answer(409, body: '{"detail":"Off."}')
        ..answer(404, body: '');
      Stream<LookupStreamEvent> go() => service.sendLookup(
        messageId: 'm',
        mode: LookupMode.anonymized,
        text: 'x',
      );
      await expectLater(
        go().toList(),
        refused(LookupRefusalKind.notAcknowledged),
      );
      await expectLater(go().toList(), refused(LookupRefusalKind.personOff));
      await expectLater(go().toList(), refused(LookupRefusalKind.serverOff));
    });

    test('lookupEventFrom ignores bad and unknown frames', () {
      expect(
        AssistantService.lookupEventFrom(const SseEvent('waiting', '')),
        isA<LookupWaitingEvent>(),
      );
      expect(
        AssistantService.lookupEventFrom(const SseEvent('done', 'nope')),
        isNull,
      );
      expect(
        AssistantService.lookupEventFrom(const SseEvent('delta', '{}')),
        isNull,
      );
    });
  });

  group('history', () {
    test('GET /assistant/lookups lists newest first as sent', () async {
      api.on(
        'GET',
        '/assistant/lookups',
        (_) => [
          LookupFixtures.record(),
          LookupFixtures.record(id: 'lk0', status: 'failed'),
        ],
      );
      final list = await service.lookups();
      expect(list.map((r) => r.id), ['lk1', 'lk0']);
    });

    test('DELETE /assistant/lookups/{id}, and 404 is already gone', () async {
      api.on('DELETE', '/assistant/lookups/lk1', (_) => null);
      await service.deleteLookup('lk1');
      expect(api.calls, ['DELETE /assistant/lookups/lk1']);
      await expectLater(
        service.deleteLookup('other'),
        refused(LookupRefusalKind.missing),
      );
    });

    test('a server error on the list stays an ApiException', () async {
      api.fail(
        'GET',
        '/assistant/lookups',
        const ApiException('The server had a problem.', statusCode: 500),
      );
      await expectLater(service.lookups(), throwsA(isA<ApiException>()));
    });
  });
}
