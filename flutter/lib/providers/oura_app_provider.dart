import 'package:flutter/foundation.dart';

import '../models/setup.dart';
import '../services/integration_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The server's Oura application, for the household admin: what is set, and
/// saving a new client id and secret.
class OuraAppProvider extends ChangeNotifier with SafeNotifier {
  final IntegrationService _service;
  final AuthProvider _auth;

  OuraApp? _app;
  bool _loading = false;
  bool _saving = false;
  String? _error;

  OuraAppProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  /// Null until the first load succeeds.
  OuraApp? get app => _app;
  bool get loading => _loading;
  bool get saving => _saving;
  String? get error => _error;

  /// Loads the application. Members are not allowed to, so it does nothing
  /// for them.
  Future<void> load() async {
    if (!_auth.isAdmin) return;
    _loading = true;
    _error = null;
    notify();
    try {
      _app = await _service.ouraApp();
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Saves the pair. True on success.
  Future<bool> save({required String clientId, required String clientSecret}) =>
      _run(
        () => _service.saveOuraApp(
          clientId: clientId,
          clientSecret: clientSecret,
        ),
      );

  Future<bool> clear() => _run(_service.clearOuraApp);

  Future<bool> _run(Future<OuraApp> Function() action) async {
    _saving = true;
    _error = null;
    notify();
    try {
      _app = await action();
      return true;
    } catch (e) {
      _error = describeError(e);
      return false;
    } finally {
      _saving = false;
      notify();
    }
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || _app == null) return;
    _app = null;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
