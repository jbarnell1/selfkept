import 'package:flutter/foundation.dart';

import '../models/retention.dart';
import '../services/retention_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The retention setting for the household admin: load, change, run now.
class RetentionProvider extends ChangeNotifier with SafeNotifier {
  final RetentionService _service;
  final AuthProvider _auth;

  Retention? _retention;
  bool _loading = false;
  bool _busy = false;
  String? _error;

  RetentionProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  /// Null until the first load succeeds.
  Retention? get retention => _retention;
  bool get loading => _loading;

  /// Saving or running.
  bool get busy => _busy;
  String? get error => _error;

  /// Members may not read the setting, so this does nothing for them.
  Future<void> load() async {
    if (!_auth.isAdmin) return;
    _loading = true;
    _error = null;
    notify();
    try {
      _retention = await _service.get();
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// True when saved; otherwise [error] says why.
  Future<bool> save(int rawMonths) async {
    _busy = true;
    _error = null;
    notify();
    try {
      _retention = await _service.save(rawMonths);
      return true;
    } catch (e) {
      _error = describeError(e);
      return false;
    } finally {
      _busy = false;
      notify();
    }
  }

  /// Null when the run failed; [error] says why.
  Future<RetentionRun?> run() async {
    _busy = true;
    _error = null;
    notify();
    try {
      final result = await _service.run();
      _retention = await _service.get();
      return result;
    } catch (e) {
      _error = describeError(e);
      return null;
    } finally {
      _busy = false;
      notify();
    }
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || _retention == null) return;
    _retention = null;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
