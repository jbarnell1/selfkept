import 'package:dio/dio.dart';

import 'json_reader.dart';

/// A failed API call, flattened to what a screen needs: a human message, the
/// HTTP status when there was one, and whether the server was reachable at all.
class ApiException implements Exception {
  final String message;
  final int? statusCode;

  /// True when the request never got an HTTP answer (offline, DNS, timeout).
  final bool isNetwork;

  /// RFC 7807 `type` when the server sent one.
  final String? problemType;

  /// How long the server asked the client to wait, on a 429.
  final Duration? retryAfter;

  /// The decoded problem body when the server sent a JSON object, so a
  /// caller can read a route's own extra fields (the lookup's `findings`).
  final Map<String, dynamic>? body;

  const ApiException(
    this.message, {
    this.statusCode,
    this.isNetwork = false,
    this.problemType,
    this.retryAfter,
    this.body,
  });

  const ApiException.network([
    this.message = 'The server could not be reached.',
  ]) : statusCode = null,
       isNetwork = true,
       problemType = null,
       retryAfter = null,
       body = null;

  bool get isUnauthorized => statusCode == 401;
  bool get isForbidden => statusCode == 403;
  bool get isNotFound => statusCode == 404;
  bool get isConflict => statusCode == 409;
  bool get isRateLimited => statusCode == 429;

  /// Builds from a Dio failure, reading problem+json when present.
  factory ApiException.fromDio(DioException error) {
    final response = error.response;
    if (response == null || _isTransport(error)) {
      return const ApiException.network();
    }
    return ApiException.fromResponse(
      response.statusCode,
      response.data,
      retryAfter: response.headers.value('retry-after'),
    );
  }

  /// Builds from an HTTP answer read some other way (the streamed assistant
  /// reply), reading problem+json when [data] is a decoded map.
  factory ApiException.fromResponse(
    int? status,
    dynamic data, {
    String? retryAfter,
  }) {
    String? message;
    String? type;
    Map<String, dynamic>? body;
    int? retrySeconds = int.tryParse(retryAfter ?? '');
    if (data is Map) {
      body = Map<String, dynamic>.from(data);
      final r = JsonReader(body);
      message =
          r.stringOrNull('detail') ??
          r.stringOrNull('title') ??
          r.stringOrNull('message') ??
          r.stringOrNull('error');
      type = r.stringOrNull('type');
      final asked = r.integer('retryAfterSeconds');
      if (asked > 0) retrySeconds ??= asked;
      final errors = r.object('errors');
      if (message == null && errors != null && errors.isNotEmpty) {
        final first = errors.values.first;
        if (first is List && first.isNotEmpty) message = first.first.toString();
      }
    } else if (data is String && data.trim().isNotEmpty && data.length < 200) {
      message = data.trim();
    }
    return ApiException(
      message ?? _defaultMessage(status),
      statusCode: status,
      problemType: type,
      retryAfter: retrySeconds == null ? null : Duration(seconds: retrySeconds),
      body: body,
    );
  }

  static bool _isTransport(DioException error) =>
      error.type == DioExceptionType.connectionError ||
      error.type == DioExceptionType.connectionTimeout ||
      error.type == DioExceptionType.sendTimeout ||
      error.type == DioExceptionType.receiveTimeout;

  static String _defaultMessage(int? status) => switch (status) {
    400 => 'That request was not understood.',
    401 => 'Please sign in again.',
    403 => 'You do not have permission to do that.',
    404 => 'Not found.',
    409 => 'That already exists.',
    429 => 'Too many attempts. Give it a moment.',
    final s? when s >= 500 => 'The server had a problem.',
    _ => 'Something went wrong.',
  };

  @override
  String toString() => 'ApiException(${statusCode ?? 'network'}): $message';
}

/// What a page says when your own record answers 404. Nobody withholds your
/// own record from you, so the server does not have that page: it is older
/// than the app.
const String missingOnServerMessage =
    'Your server does not have this yet. It may need an update.';

/// Message for any thrown object, so screens can show one line.
String describeError(Object error) {
  if (error is ApiException) return error.message;
  if (error is DioException) return ApiException.fromDio(error).message;
  if (error is FormatException) return 'The server sent something unexpected.';
  return 'Something went wrong.';
}
