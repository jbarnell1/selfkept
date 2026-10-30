import 'package:flutter/foundation.dart';

import '../models/diagnostics.dart';
import '../services/diagnostics_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The server's recent warnings and errors, for the household admin.
class RecentProblemsProvider extends ChangeNotifier with SafeNotifier {
  final DiagnosticsService _service;
  final AuthProvider _auth;

  RecentProblems? _problems;
  bool _loading = false;
  String? _error;

  RecentProblemsProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  /// Null until the first load succeeds.
  RecentProblems? get problems => _problems;
  bool get loading => _loading;
  String? get error => _error;

  /// Members may not read the list, so this does nothing for them.
  Future<void> load() async {
    if (!_auth.isAdmin) return;
    _loading = true;
    _error = null;
    notify();
    try {
      _problems = await _service.problems();
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || _problems == null) return;
    _problems = null;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
