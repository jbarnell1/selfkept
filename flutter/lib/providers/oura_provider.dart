import 'package:flutter/foundation.dart';

import '../models/connect.dart';
import '../services/oura_service.dart';
import '../utils/api_error.dart';
import '../utils/open_url.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The Oura ring link for the signed-in person.
class OuraProvider extends ChangeNotifier with SafeNotifier {
  final OuraService _service;
  final AuthProvider? _auth;
  final UrlOpener _open;

  OuraStatus? _status;
  bool _loading = false;
  bool _syncing = false;
  String? _error;

  /// [open] sends the person to Oura. It defaults to url_launcher; tests pass
  /// their own to capture the address. [auth], when given, clears the status
  /// on sign-out.
  OuraProvider(this._service, {this._auth, UrlOpener? open})
    : _open = open ?? openUrl {
    _auth?.addListener(_onAuthChanged);
  }

  /// Null until the first load succeeds.
  OuraStatus? get status => _status;
  bool get loading => _loading;
  bool get syncing => _syncing;
  String? get error => _error;

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      _status = await _service.status();
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Asks the server for the Oura address and opens it. On web the same tab
  /// goes to Oura and comes back.
  Future<void> connect({String? returnTo}) async {
    _error = null;
    notify();
    try {
      final url = await _service.connect(returnTo: returnTo);
      final opened = await _open(Uri.parse(url));
      if (!opened) _error = 'Oura could not be opened. Try again.';
    } catch (e) {
      _error = describeError(e);
    }
    notify();
  }

  /// Asks for a pull, then reloads the status after [reloadAfter] so the
  /// server has time to start.
  Future<void> sync({Duration reloadAfter = const Duration(seconds: 2)}) async {
    _error = null;
    _syncing = true;
    notify();
    try {
      await _service.sync();
      await Future<void>.delayed(reloadAfter);
    } catch (e) {
      _error = describeError(e);
      _syncing = false;
      notify();
      return;
    }
    _syncing = false;
    if (isDisposed) return;
    await load();
  }

  Future<bool> disconnect() async {
    try {
      await _service.disconnect();
    } catch (e) {
      _error = describeError(e);
      notify();
      return false;
    }
    await load();
    return true;
  }

  void _onAuthChanged() {
    if (_auth?.isAuthenticated ?? true) return;
    if (_status == null) return;
    _status = null;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth?.removeListener(_onAuthChanged);
    super.dispose();
  }
}
