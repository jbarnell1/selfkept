import 'dart:async';

import 'package:flutter/foundation.dart';

import '../config/app_config.dart';
import '../services/diagnostics_service.dart';
import 'api_error.dart';

/// Sends the app's own errors to the person's own server (never anywhere
/// else), where they land in the server log and the admin's Recent problems.
///
/// Only failures that look like bugs are sent: being offline, a sign-in that
/// expired or a refused permission is not one. Reports are throttled, so a
/// widget that throws on every frame costs one report, not thousands. Error
/// text and stack traces are sent; health values are not part of either.
class ErrorReporter {
  /// Most reports one run of the app sends.
  static const int maxPerSession = 20;

  /// The same message is sent at most once in this window.
  static const Duration repeatWindow = Duration(minutes: 1);

  final DiagnosticsService _service;
  final DateTime Function() _clock;
  final Map<String, DateTime> _lastSent = {};
  int _sent = 0;

  /// The in-app location to attach to a report (the current route).
  String Function()? where;

  ErrorReporter(this._service, {DateTime Function()? clock})
    : _clock = clock ?? DateTime.now;

  int get sentCount => _sent;

  /// True for failures that are not bugs in the app.
  static bool isExpected(Object error) =>
      error is ApiException &&
      (error.isNetwork ||
          error.isUnauthorized ||
          error.isForbidden ||
          error.isRateLimited);

  /// Reports [error] unless it is expected, repeated or over the session cap.
  /// Never throws and never waits on the network.
  void report(Object error, StackTrace? stack, {required String kind}) {
    if (isExpected(error) || _sent >= maxPerSession) return;
    final message = _clip(error.toString(), 500);
    final now = _clock();
    final last = _lastSent[message];
    if (last != null && now.difference(last) < repeatWindow) return;
    _lastSent[message] = now;
    _sent += 1;
    unawaited(
      _service
          .report(
            kind: kind,
            message: message,
            stack: stack == null ? null : _clip(stack.toString(), 4000),
            appVersion: AppConfig.appVersion,
            platform: kIsWeb ? 'web' : defaultTargetPlatform.name,
            where: _safeWhere(),
          )
          .catchError((Object _) {}),
    );
  }

  String? _safeWhere() {
    try {
      return where?.call();
    } catch (_) {
      return null;
    }
  }

  static String _clip(String text, int max) =>
      text.length <= max ? text : text.substring(0, max);
}
