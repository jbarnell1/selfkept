import 'package:flutter/foundation.dart';

import '../models/person.dart';
import '../models/user.dart';
import '../services/api_client.dart';
import '../services/auth_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';

/// Where the session stands.
enum AuthStatus {
  /// Nothing known yet; bootstrap has not run.
  initial,

  /// Bootstrap or a sign-in is in flight.
  loading,
  authenticated,
  unauthenticated,

  /// The last sign-in or registration failed; [AuthProvider.errorMessage] says why.
  error;

  static AuthStatus fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : initial;

  static AuthStatus fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.name.toLowerCase() == lower) return s;
    }
    return initial;
  }

  String get displayName => switch (this) {
    initial => 'Starting',
    loading => 'Loading',
    authenticated => 'Signed in',
    unauthenticated => 'Signed out',
    error => 'Error',
  };
}

/// Session state for the whole app. Bootstrap reads stored tokens, asks the
/// server who we are and settles on authenticated or unauthenticated.
class AuthProvider extends ChangeNotifier with SafeNotifier {
  final ApiClient _api;
  final AuthService _auth;

  AuthStatus _status = AuthStatus.initial;
  User? _user;
  String? _errorMessage;
  bool _sessionExpired = false;
  bool _bootstrapped = false;

  AuthProvider(ApiClient api, {AuthService? authService})
    : _api = api,
      _auth = authService ?? AuthService(api) {
    _api.onUnauthorized = _handleUnauthorized;
  }

  AuthStatus get status => _status;
  User? get user => _user;
  String? get errorMessage => _errorMessage;
  bool get isAuthenticated => _status == AuthStatus.authenticated;
  bool get isLoading => _status == AuthStatus.loading;

  /// False until [bootstrap] has settled once. The router waits on this.
  bool get isBootstrapped => _bootstrapped;

  bool get isAdmin => _user?.isAdmin ?? false;

  /// One-shot flag: true after a forced sign-out so the login page can say why.
  bool consumeSessionExpired() {
    if (!_sessionExpired) return false;
    _sessionExpired = false;
    return true;
  }

  /// Restores a session from storage. Never throws; an unreachable server
  /// leaves us unauthenticated and the splash explains the rest.
  Future<void> bootstrap() async {
    _status = AuthStatus.loading;
    notify();
    try {
      final restored = await _api.restoreSession();
      if (!restored) {
        _settle(null);
        return;
      }
      final me = await _auth.me();
      _settle(me);
    } on ApiException catch (e) {
      if (!e.isNetwork && e.isUnauthorized) await _api.clearSession();
      _settle(null);
    } catch (_) {
      _settle(null);
    } finally {
      _bootstrapped = true;
      notify();
    }
  }

  Future<bool> login({required String email, required String password}) =>
      _attempt(() => _auth.login(email: email, password: password));

  Future<bool> register({
    required String email,
    required String password,
    required String displayName,
    String? inviteCode,
  }) => _attempt(
    () => _auth.register(
      email: email,
      password: password,
      displayName: displayName,
      inviteCode: inviteCode,
    ),
  );

  Future<bool> _attempt(Future<AuthResponse> Function() action) async {
    _status = AuthStatus.loading;
    _errorMessage = null;
    notify();
    try {
      final response = await action();
      _settle(response.user);
      return true;
    } catch (e) {
      _bootstrapped = true;
      _user = null;
      _errorMessage = describeError(e);
      _status = AuthStatus.error;
      notify();
      return false;
    }
  }

  Future<void> logout() async {
    await _auth.logout();
    _user = null;
    _errorMessage = null;
    _status = AuthStatus.unauthenticated;
    notify();
  }

  /// Lets the profile editor update the signed-in person without a refetch.
  void applyPerson(Person person) {
    final current = _user;
    if (current == null) return;
    _user = current.copyWith(person: person, displayName: person.displayName);
    notify();
  }

  /// Replaces the signed-in account with a newer copy the server returned
  /// (the first-run setup answers, for one).
  void applyUser(User user) {
    if (_user?.id != user.id) return;
    _user = user;
    notify();
  }

  void clearError() {
    if (_errorMessage == null && _status != AuthStatus.error) return;
    _errorMessage = null;
    if (_status == AuthStatus.error) _status = AuthStatus.unauthenticated;
    notify();
  }

  void _handleUnauthorized() {
    if (!_bootstrapped) return;
    _user = null;
    _errorMessage = null;
    _sessionExpired = true;
    _status = AuthStatus.unauthenticated;
    notify();
  }

  void _settle(User? user) {
    _bootstrapped = true;
    _user = user;
    _errorMessage = null;
    _status = user == null
        ? AuthStatus.unauthenticated
        : AuthStatus.authenticated;
    notify();
  }

  @override
  void dispose() {
    _api.onUnauthorized = null;
    super.dispose();
  }
}
