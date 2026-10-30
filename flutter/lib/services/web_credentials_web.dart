import 'package:dio/browser.dart';
import 'package:dio/dio.dart';

/// On web the refresh token is an HttpOnly cookie. Send it with cross-origin
/// requests so /auth/refresh can read it; JavaScript never sees the value.
void configureWebCredentials(Dio dio) {
  final adapter = dio.httpClientAdapter;
  if (adapter is BrowserHttpClientAdapter) {
    adapter.withCredentials = true;
  }
}
