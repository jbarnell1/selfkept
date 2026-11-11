import 'dart:convert';

import '../../config/app_config.dart';
import '../../utils/api_error.dart';
import 'event_stream_transport.dart';
import 'sse_parser.dart';

/// Posts JSON to the app's own server and reads the answer as server-sent
/// events. It shares the app's session rather than keeping one: the access
/// token comes from [accessToken], and a 401 gets one refresh and one retry,
/// the way ApiClient handles any other call. An error the server sends before
/// the stream starts comes back as an [ApiException].
class EventStreamClient {
  final EventStreamTransport _transport;
  final String Function() _serverUrl;
  final Future<String?> Function() _accessToken;
  final Future<bool> Function() _refresh;
  final bool _isMobile;

  EventStreamClient({
    required this._transport,
    required this._serverUrl,
    required this._accessToken,
    required this._refresh,
    this._isMobile = false,
  });

  /// Cancelling the subscription stops the request.
  Stream<SseEvent> post(String path, {Object? data}) {
    final body = jsonEncode(data ?? const <String, dynamic>{});
    return Stream.fromFuture(
      _open(path, body),
    ).asyncExpand((response) => SseParser.parse(response.body));
  }

  Future<StreamedHttpResponse> _open(String path, String body) async {
    var response = await _send(path, body);
    if (response.statusCode == 401) {
      await _discard(response);
      if (await _refresh()) response = await _send(path, body);
    }
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw await _problem(response);
    }
    return response;
  }

  Future<StreamedHttpResponse> _send(String path, String body) async {
    final token = await _accessToken();
    final url = Uri.parse('${AppConfig.apiBaseFor(_serverUrl())}$path');
    return _transport.post(
      url,
      body: body,
      headers: {
        'Accept': 'text/event-stream',
        'Content-Type': 'application/json',
        if (token != null && token.isNotEmpty) 'Authorization': 'Bearer $token',
        if (_isMobile) 'X-Client': 'mobile',
        'X-App-Version': AppConfig.appVersion,
      },
    );
  }

  static Future<void> _discard(StreamedHttpResponse response) async {
    try {
      await response.body.drain<void>();
    } catch (_) {
      // Nothing in it was wanted.
    }
  }

  static Future<ApiException> _problem(StreamedHttpResponse response) async {
    dynamic data;
    try {
      final text = await utf8.decoder.bind(response.body).join();
      try {
        data = jsonDecode(text);
      } on FormatException {
        data = text;
      }
    } catch (_) {
      data = null;
    }
    return ApiException.fromResponse(
      response.statusCode,
      data,
      retryAfter: response.retryAfter,
    );
  }
}
