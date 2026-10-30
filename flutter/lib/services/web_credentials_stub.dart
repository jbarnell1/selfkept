import 'package:dio/dio.dart';

/// No-op off web: mobile carries the refresh token in the body, not a cookie.
void configureWebCredentials(Dio dio) {}
