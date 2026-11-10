import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/models/assistant.dart';
import 'package:selfkept/services/assistant_service.dart';
import 'package:selfkept/services/sse/event_stream_client.dart';
import 'package:selfkept/services/sse/sse_parser.dart';
import 'package:selfkept/utils/api_error.dart';

import '../helpers/fake_api_client.dart';
import '../helpers/fake_event_stream.dart';

void main() {
  group('SseParser', () {
    test('a frame split anywhere still comes out whole', () {
      const text = 'event: delta\ndata: {"text":"Hello."}\n\n';
      for (var cut = 1; cut < text.length; cut++) {
        final parser = SseParser();
        final events = [
          ...parser.add(text.substring(0, cut)),
          ...parser.add(text.substring(cut)),
        ];
        expect(events, hasLength(1), reason: 'cut at $cut');
        expect(events.single.event, 'delta');
        expect(events.single.data, '{"text":"Hello."}');
      }
    });

    test('multi-line data is joined with line breaks', () {
      final events = SseParser().add(
        'event: note\ndata: one\ndata:two\ndata:  three\n\n',
      );
      expect(events.single.data, 'one\ntwo\n three');
    });

    test('CR LF and bare CR end lines, also across chunks', () {
      final parser = SseParser();
      final events = [
        ...parser.add('event: a\r'),
        ...parser.add('\ndata: 1\r\n\r'),
        ...parser.add('\nevent: b\rdata: 2\r\r'),
      ];
      expect(events.map((e) => '${e.event}=${e.data}'), ['a=1', 'b=2']);
    });

    test('comments, ids and empty events are skipped; no event is message', () {
      final events = SseParser().add(
        ': keep-alive\n\nid: 7\nretry: 100\ndata: x\n\nevent: empty\n\n',
      );
      expect(events, hasLength(1));
      expect(events.single.event, 'message');
      expect(events.single.data, 'x');
    });

    test('an event cut off before its blank line is dropped', () {
      final parser = SseParser();
      expect(parser.add('event: done\ndata: {}'), isEmpty);
      expect(parser.add('\n'), isEmpty);
    });

    test('UTF-8 split across byte chunks decodes', () async {
      final bytes = utf8.encode('event: delta\ndata: {"text":"café"}\n\n');
      final at = bytes.indexOf(0xC3) + 1;
      final events = await SseParser.parse(
        Stream.fromIterable([bytes.sublist(0, at), bytes.sublist(at)]),
      ).toList();
      expect(jsonDecode(events.single.data), {'text': 'café'});
    });
  });

  group('AssistantService.eventFrom', () {
    test('maps the four events and ignores unknown ones', () {
      expect(
        AssistantService.eventFrom(
          const SseEvent('replace', '{"text":"New.","reason":"guard"}'),
        ),
        isA<ReplaceEvent>().having((e) => e.text, 'text', 'New.'),
      );
      expect(
        AssistantService.eventFrom(const SseEvent('delta', '{"text":" B."}')),
        isA<DeltaEvent>().having((e) => e.text, 'text', ' B.'),
      );
      expect(AssistantService.eventFrom(const SseEvent('ping', '{}')), isNull);
      expect(
        AssistantService.eventFrom(const SseEvent('delta', 'not json')),
        isNull,
      );
    });
  });

  group('EventStreamClient', () {
    late FakeEventStreamTransport transport;
    var token = 'old';
    var refreshes = 0;
    var refreshWorks = true;

    EventStreamClient client({bool mobile = false}) => EventStreamClient(
      transport: transport,
      serverUrl: () => 'https://box.tailnet.ts.net',
      accessToken: () async => token,
      refresh: () async {
        refreshes += 1;
        if (refreshWorks) token = 'new';
        return refreshWorks;
      },
      isMobile: mobile,
    );

    setUp(() {
      transport = FakeEventStreamTransport();
      token = 'old';
      refreshes = 0;
      refreshWorks = true;
    });

    test('posts JSON with the bearer token and reads events', () async {
      transport.answer(
        200,
        body: 'event: delta\ndata: {"text":"Hi."}\n\nevent: done\ndata: {}\n\n',
      );
      final events = await client(mobile: true)
          .post('/assistant/conversations/c1/messages', data: {'text': 'hello'})
          .toList();
      expect(events.map((e) => e.event), ['delta', 'done']);
      final request = transport.requests.single;
      expect(
        request.url.toString(),
        'https://box.tailnet.ts.net/api/assistant/conversations/c1/messages',
      );
      expect(request.headers['Authorization'], 'Bearer old');
      expect(request.headers['Accept'], 'text/event-stream');
      expect(request.headers['X-Client'], 'mobile');
      expect(request.json, {'text': 'hello'});
    });

    test('a 401 refreshes once and retries with the new token', () async {
      transport
        ..answer(401, body: '')
        ..answer(200, body: 'event: done\ndata: {}\n\n');
      final events = await client().post('/x').toList();
      expect(events.single.event, 'done');
      expect(refreshes, 1);
      expect(transport.requests, hasLength(2));
      expect(transport.requests.last.headers['Authorization'], 'Bearer new');
    });

    test('a refused refresh ends as 401', () async {
      refreshWorks = false;
      transport.answer(401, body: '');
      await expectLater(
        client().post('/x').toList(),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 401)),
      );
      expect(transport.requests, hasLength(1));
    });

    test(
      'a problem before the stream is an ApiException with its detail',
      () async {
        transport.answer(
          400,
          body: jsonEncode({
            'title': 'Validation failed',
            'status': 400,
            'detail': 'Say something first.',
          }),
        );
        await expectLater(
          client().post('/x').toList(),
          throwsA(
            isA<ApiException>()
                .having((e) => e.message, 'message', 'Say something first.')
                .having((e) => e.statusCode, 'status', 400),
          ),
        );
      },
    );

    test('cancelling the subscription cancels the body', () async {
      transport.answer(200);
      final seen = <SseEvent>[];
      final sub = client().post('/x').listen(seen.add);
      await pumpEventQueue();
      transport.frame('delta', {'text': 'One.'});
      await pumpEventQueue();
      expect(seen, hasLength(1));
      await sub.cancel();
      expect(transport.cancelled, isTrue);
    });
  });

  test('ApiException.fromResponse reads the framework errors object', () {
    final e = ApiException.fromResponse(400, {
      'errors': {
        'Text': ['The Text field is required.'],
      },
    });
    expect(e.message, 'The Text field is required.');
    expect(
      ApiException.fromResponse(500, null).message,
      'The server had a problem.',
    );
  });

  test('the service sends the message through the stream client', () async {
    final transport = FakeEventStreamTransport()
      ..answer(
        200,
        body:
            'event: capture\ndata: {"conversationId":"c1","messageId":"u","entries":[],"remembered":[]}\n\n'
            'event: mystery\ndata: {}\n\n'
            'event: done\ndata: {"conversationId":"c1","messageId":"r","userMessageId":"u","origin":"template","text":"Plain."}\n\n',
      );
    final service = AssistantService(
      FakeApiClient(),
      EventStreamClient(
        transport: transport,
        serverUrl: () => 'http://fake.test',
        accessToken: () async => null,
        refresh: () async => false,
      ),
    );
    final events = await service.send('c1', 'hello').toList();
    expect(events, [isA<CaptureEvent>(), isA<DoneEvent>()]);
    expect(
      transport.requests.single.headers.containsKey('Authorization'),
      isFalse,
    );
  });
}
