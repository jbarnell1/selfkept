import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart' show kIsWeb;

import '../config/app_config.dart';
import '../models/user.dart';
import '../utils/api_error.dart';
import 'reachability.dart';
import 'token_store.dart';
import 'web_credentials_stub.dart'
    if (dart.library.js_interop) 'web_credentials_web.dart';

/// Decoder for a response body.
typedef FromBody<T> = T Function(dynamic body);

/// What services depend on. Kept abstract so tests can hand a fake to a
/// provider without a network stack.
abstract class ApiClient {
  /// Server origin currently in use, without the /api prefix.
  String get serverUrl;

  Future<T> get<T>(
    String path, {
    Map<String, dynamic>? queryParameters,
    FromBody<T>? fromJson,
  });

  /// GET that returns the raw response body, for downloads such as a PDF.
  Future<Uint8List> getBytes(
    String path, {
    Map<String, dynamic>? queryParameters,
  });

  Future<T> post<T>(String path, {Object? data, FromBody<T>? fromJson});

  Future<T> put<T>(String path, {Object? data, FromBody<T>? fromJson});

  Future<T> patch<T>(String path, {Object? data, FromBody<T>? fromJson});

  Future<T> delete<T>(String path, {Object? data, FromBody<T>? fromJson});

  /// Remembers the tokens of a fresh login, register or refresh.
  Future<void> storeSession(AuthResponse response);

  /// Tries to make an access token available without user input: a stored
  /// token on mobile, a cookie-driven refresh on web. True when one exists.
  Future<bool> restoreSession();

  /// Forgets the local session. Does not call the server.
  Future<void> clearSession();

  /// Called once when a 401 survives a refresh attempt: the session is dead.
  set onUnauthorized(void Function()? callback);
}

/// Dio-backed client: bearer interceptor, single-flight refresh with retry on
/// 401, X-Client header on mobile, cookies on web.
class DioApiClient implements ApiClient {
  late final Dio _dio;
  late final Dio _refreshDio;
  final TokenStore _tokens;
  final Reachability? _reachability;
  final bool _isWeb;
  String _serverUrl;

  Future<bool?>? _refreshInFlight;
  void Function()? _onUnauthorized;

  DioApiClient({
    required String serverUrl,
    TokenStore? tokenStore,
    this._reachability,
    bool? isWeb,
    HttpClientAdapter? adapter,
  }) : _tokens = tokenStore ?? SecureTokenStore(),
       _isWeb = isWeb ?? kIsWeb,
       _serverUrl = AppConfig.normalizeServerUrl(serverUrl) {
    _dio = _buildDio(adapter);
    _refreshDio = _buildDio(adapter);
    configureWebCredentials(_dio);
    configureWebCredentials(_refreshDio);
    _dio.interceptors.add(
      InterceptorsWrapper(onRequest: _onRequest, onError: _onError),
    );
  }

  Dio _buildDio(HttpClientAdapter? adapter) {
    final dio = Dio(
      BaseOptions(
        baseUrl: AppConfig.apiBaseFor(_serverUrl),
        connectTimeout: AppConfig.requestTimeout,
        receiveTimeout: AppConfig.requestTimeout,
        headers: {'Accept': 'application/json'},
        contentType: 'application/json',
        // Problem+json bodies are wanted, not thrown away: decode every status.
        validateStatus: (status) => status != null && status < 400,
      ),
    );
    if (adapter != null) dio.httpClientAdapter = adapter;
    return dio;
  }

  @override
  String get serverUrl => _serverUrl;

  /// Points every following request at another server. Tokens are not kept
  /// across servers: they would not be valid there.
  Future<void> setServerUrl(String serverUrl) async {
    final next = AppConfig.normalizeServerUrl(serverUrl);
    if (next == _serverUrl) return;
    _serverUrl = next;
    _dio.options.baseUrl = AppConfig.apiBaseFor(next);
    _refreshDio.options.baseUrl = AppConfig.apiBaseFor(next);
    await _tokens.clear();
  }

  @override
  set onUnauthorized(void Function()? callback) => _onUnauthorized = callback;

  Future<void> _onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final token = await _tokens.readAccessToken();
    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    if (!_isWeb) options.headers['X-Client'] = 'mobile';
    options.headers['X-App-Version'] = AppConfig.appVersion;
    handler.next(options);
  }

  Future<void> _onError(
    DioException error,
    ErrorInterceptorHandler handler,
  ) async {
    final status = error.response?.statusCode;
    if (status == 401 && _eligibleForRefresh(error.requestOptions)) {
      final refreshed = await _refreshSession();
      if (refreshed == true) {
        try {
          final retried = await _retry(error.requestOptions);
          return handler.resolve(retried);
        } on DioException catch (retryError) {
          if (retryError.response?.statusCode != 401) {
            return handler.next(retryError);
          }
        }
      }
      if (refreshed != null) {
        // The server answered and said no: the session is gone.
        await _tokens.clear();
        _onUnauthorized?.call();
      }
    }
    handler.next(error);
  }

  bool _eligibleForRefresh(RequestOptions options) {
    if (options.extra['retried'] == true) return false;
    final path = options.path;
    return !path.contains('/auth/refresh') &&
        !path.contains('/auth/login') &&
        !path.contains('/auth/register') &&
        !path.contains('/auth/logout');
  }

  Future<Response<dynamic>> _retry(RequestOptions options) async {
    final token = await _tokens.readAccessToken();
    if (token != null) options.headers['Authorization'] = 'Bearer $token';
    options.extra['retried'] = true;
    return _dio.fetch<dynamic>(options);
  }

  /// One refresh at a time; concurrent 401s share the result.
  Future<bool?> _refreshSession() {
    final inFlight = _refreshInFlight;
    if (inFlight != null) return inFlight;
    final future = _doRefresh();
    _refreshInFlight = future;
    unawaited(future.whenComplete(() => _refreshInFlight = null));
    return future;
  }

  /// true: new tokens stored. false: the server rejected the refresh.
  /// null: the server could not be reached, so nothing is known.
  Future<bool?> _doRefresh() async {
    try {
      final body = <String, dynamic>{};
      if (!_isWeb) {
        final refresh = await _tokens.readRefreshToken();
        if (refresh == null || refresh.isEmpty) return false;
        body['refreshToken'] = refresh;
      }
      final response = await _refreshDio.post<dynamic>(
        '/auth/refresh',
        data: body,
        options: Options(headers: _isWeb ? null : {'X-Client': 'mobile'}),
      );
      final data = response.data;
      if (data is! Map) return false;
      final auth = AuthResponse.fromJson(Map<String, dynamic>.from(data));
      if (auth.accessToken.isEmpty) return false;
      await storeSession(auth);
      _reachability?.reportSuccess();
      return true;
    } on DioException catch (e) {
      final failure = ApiException.fromDio(e);
      if (failure.isNetwork) {
        _reachability?.reportFailure();
        return null;
      }
      _reachability?.reportSuccess();
      return false;
    } catch (_) {
      return false;
    }
  }

  @override
  Future<T> get<T>(
    String path, {
    Map<String, dynamic>? queryParameters,
    FromBody<T>? fromJson,
  }) => _send(
    () => _dio.get<dynamic>(path, queryParameters: queryParameters),
    fromJson,
  );

  @override
  Future<Uint8List> getBytes(
    String path, {
    Map<String, dynamic>? queryParameters,
  }) => _send<Uint8List>(
    () => _dio.get<dynamic>(
      path,
      queryParameters: queryParameters,
      options: Options(
        responseType: ResponseType.bytes,
        headers: {'Accept': '*/*'},
      ),
    ),
    (body) => body is Uint8List
        ? body
        : Uint8List.fromList((body as List<dynamic>).cast<int>()),
  );

  @override
  Future<T> post<T>(String path, {Object? data, FromBody<T>? fromJson}) =>
      _send(() => _dio.post<dynamic>(path, data: data), fromJson);

  @override
  Future<T> put<T>(String path, {Object? data, FromBody<T>? fromJson}) =>
      _send(() => _dio.put<dynamic>(path, data: data), fromJson);

  @override
  Future<T> patch<T>(String path, {Object? data, FromBody<T>? fromJson}) =>
      _send(() => _dio.patch<dynamic>(path, data: data), fromJson);

  @override
  Future<T> delete<T>(String path, {Object? data, FromBody<T>? fromJson}) =>
      _send(() => _dio.delete<dynamic>(path, data: data), fromJson);

  Future<T> _send<T>(
    Future<Response<dynamic>> Function() request,
    FromBody<T>? fromJson,
  ) async {
    try {
      final response = await request();
      _reachability?.reportSuccess();
      if (response.statusCode == 204 ||
          response.data == null ||
          response.data == '') {
        return _noContent<T>();
      }
      if (fromJson != null) return fromJson(response.data);
      return response.data as T;
    } on DioException catch (e) {
      final failure = ApiException.fromDio(e);
      if (failure.isNetwork) {
        _reachability?.reportFailure();
      } else {
        _reachability?.reportSuccess();
      }
      throw failure;
    }
  }

  static T _noContent<T>() {
    if (null is T) return null as T;
    if (<String, dynamic>{} is T) return <String, dynamic>{} as T;
    if (<dynamic>[] is T) return <dynamic>[] as T;
    throw StateError('No content returned, but a $T was expected.');
  }

  @override
  Future<void> storeSession(AuthResponse response) async {
    await _tokens.writeAccessToken(response.accessToken);
    final refresh = response.refreshToken;
    if (!_isWeb && refresh != null && refresh.isNotEmpty) {
      await _tokens.writeRefreshToken(refresh);
    }
  }

  @override
  Future<bool> restoreSession() async {
    if (_isWeb) {
      // The access token lives in memory only; the cookie is the session.
      return await _refreshSession() == true;
    }
    final access = await _tokens.readAccessToken();
    if (access != null && access.isNotEmpty) return true;
    final refresh = await _tokens.readRefreshToken();
    if (refresh == null || refresh.isEmpty) return false;
    return await _refreshSession() == true;
  }

  @override
  Future<void> clearSession() => _tokens.clear();
}
