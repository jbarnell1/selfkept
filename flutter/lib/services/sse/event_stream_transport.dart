import 'event_stream_transport_dio.dart'
    if (dart.library.js_interop) 'event_stream_transport_web.dart'
    as impl;

/// An HTTP answer whose body arrives in pieces as the server writes them.
class StreamedHttpResponse {
  final int statusCode;
  final String? retryAfter;

  /// Cancelling the subscription aborts the request.
  final Stream<List<int>> body;

  const StreamedHttpResponse({
    required this.statusCode,
    required this.body,
    this.retryAfter,
  });
}

/// Sends one request and hands back the body as it streams. Dio's browser
/// adapter only delivers a body once all of it has arrived, so web uses
/// `fetch` and a stream reader; everywhere else Dio streams the response.
abstract class EventStreamTransport {
  /// Resolves once the status and headers are in. Throws an ApiException
  /// marked as a network failure when the server cannot be reached.
  Future<StreamedHttpResponse> post(
    Uri url, {
    required Map<String, String> headers,
    required String body,
  });
}

/// The transport for the platform this build runs on.
EventStreamTransport createEventStreamTransport() => impl.createTransport();
