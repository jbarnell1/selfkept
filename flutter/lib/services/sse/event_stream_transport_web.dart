import 'dart:async';
import 'dart:js_interop';

import 'package:web/web.dart' as web;

import '../../utils/api_error.dart';
import 'event_stream_transport.dart';

EventStreamTransport createTransport() => FetchEventStreamTransport();

/// Streams a response with `fetch` and a `ReadableStream` reader, so each
/// event shows as soon as the server flushes it. Web only.
class FetchEventStreamTransport implements EventStreamTransport {
  @override
  Future<StreamedHttpResponse> post(
    Uri url, {
    required Map<String, String> headers,
    required String body,
  }) async {
    final abort = web.AbortController();
    final sent = web.Headers();
    headers.forEach((name, value) => sent.append(name, value));
    final web.Response response;
    try {
      response = await web.window
          .fetch(
            url.toString().toJS,
            web.RequestInit(
              method: 'POST',
              headers: sent,
              body: body.toJS,
              signal: abort.signal,
            ),
          )
          .toDart;
    } catch (_) {
      throw const ApiException.network();
    }
    final reader =
        response.body?.getReader() as web.ReadableStreamDefaultReader?;
    var stopped = false;
    late final StreamController<List<int>> controller;

    Future<void> pump() async {
      try {
        while (reader != null && !stopped) {
          final result = await reader.read().toDart;
          if (result.done) break;
          final value = result.value;
          if (value != null) controller.add((value as JSUint8Array).toDart);
        }
      } catch (_) {
        // A dropped connection, or the abort after a cancel.
        if (!stopped) controller.addError(const ApiException.network());
      }
      if (!controller.isClosed) await controller.close();
    }

    controller = StreamController<List<int>>(
      onListen: () => unawaited(pump()),
      onCancel: () {
        stopped = true;
        abort.abort();
      },
    );
    return StreamedHttpResponse(
      statusCode: response.status,
      retryAfter: response.headers.get('retry-after'),
      body: controller.stream,
    );
  }
}
