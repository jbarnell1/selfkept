import 'dart:async';
import 'dart:convert';

import 'package:selfkept/services/sse/event_stream_transport.dart';

/// One request the fake transport received.
class FakeStreamRequest {
  final Uri url;
  final Map<String, String> headers;
  final String body;

  const FakeStreamRequest(this.url, this.headers, this.body);

  Map<String, dynamic> get json => jsonDecode(body) as Map<String, dynamic>;
}

/// A streaming transport the test drives by hand: each [post] answers with
/// the next queued status and a body the test writes to through [current].
class FakeEventStreamTransport implements EventStreamTransport {
  final List<FakeStreamRequest> requests = [];
  final List<int> _statuses = [];
  final List<String?> _bodies = [];

  /// The body of the most recent request, open until the test closes it.
  StreamController<List<int>>? current;

  /// Set when the reader cancelled (the page went away).
  bool cancelled = false;

  /// Queues an answer. [body] given: the whole body is sent and closed at
  /// once. Without one, [current] stays open for the test to write to.
  void answer(int status, {String? body}) {
    _statuses.add(status);
    _bodies.add(body);
  }

  /// Writes a whole frame to [current].
  void frame(String event, Map<String, dynamic> data) =>
      write('event: $event\ndata: ${jsonEncode(data)}\n\n');

  void write(String text) => current!.add(utf8.encode(text));

  Future<void> close() async => current!.close();

  void drop() {
    current!.addError(Exception('connection reset'));
    current!.close();
  }

  @override
  Future<StreamedHttpResponse> post(
    Uri url, {
    required Map<String, String> headers,
    required String body,
  }) async {
    requests.add(FakeStreamRequest(url, headers, body));
    final status = _statuses.isEmpty ? 200 : _statuses.removeAt(0);
    final text = _bodies.isEmpty ? null : _bodies.removeAt(0);
    final controller = StreamController<List<int>>(
      onCancel: () => cancelled = true,
    );
    if (text != null) {
      controller.add(utf8.encode(text));
      unawaited(controller.close());
    } else {
      current = controller;
    }
    return StreamedHttpResponse(statusCode: status, body: controller.stream);
  }
}

/// SSE frames the server sends, as JSON.
abstract final class StreamFixtures {
  static Map<String, dynamic> capture({
    List<Map<String, dynamic>> entries = const [],
    List<Map<String, dynamic>> remembered = const [],
  }) => {
    'conversationId': 'c1',
    'messageId': 'm-user',
    'entries': entries,
    'remembered': remembered,
  };

  static Map<String, dynamic> done({
    String text = 'Noted.',
    String origin = 'model',
  }) => {
    'conversationId': 'c1',
    'messageId': 'm-reply',
    'userMessageId': 'm-user',
    'origin': origin,
    'text': text,
  };

  static Map<String, dynamic> entry({
    String id = 'j1',
    String kind = 'food',
    String? text = 'Salmon and rice',
    List<String> tags = const ['high_protein'],
  }) => {
    'id': id,
    'at': '2026-11-10T12:00:00Z',
    'day': '2026-11-10',
    'kind': kind,
    'text': text,
    'tags': tags,
    'portion': null,
    'source': 'assistant',
    'messageId': 'm-user',
  };

  static Map<String, dynamic> memory() => {
    'id': 'mem1',
    'type': 'preference',
    'key': 'diet',
    'value': 'Eats a vegetarian diet',
    'displayText': null,
    'source': 'In-app assistant',
    'createdAt': '2026-11-10T12:00:00Z',
    'updatedAt': '2026-11-10T12:00:00Z',
  };
}
