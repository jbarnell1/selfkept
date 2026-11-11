import 'package:flutter/foundation.dart';

import '../models/target.dart';
import '../services/targets_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'record_changes.dart';

/// Today's targets: each active target against today and the week behind it.
class TallyProvider extends ChangeNotifier with SafeNotifier {
  final TargetsService _service;
  final AuthProvider _auth;
  final RecordChanges _changes;

  GoalTally? _tally;
  bool _loading = false;
  bool _hasLoaded = false;
  String? _error;

  TallyProvider({
    required this._service,
    required this._auth,
    required this._changes,
  }) {
    _auth.addListener(_onAuthChanged);
    _changes.addListener(_onRecordChanged);
  }

  GoalTally? get tally => _tally;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get error => _error;

  /// Loads or reloads. What is on screen stays while the request runs and
  /// survives a failure.
  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      _tally = await _service.tally();
      _hasLoaded = true;
    } catch (e) {
      debugPrint('Targets did not load: $e');
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  void _onRecordChanged() {
    if (_hasLoaded && !isDisposed) load();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || (!_hasLoaded && _error == null)) return;
    _tally = null;
    _hasLoaded = false;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    _changes.removeListener(_onRecordChanged);
    super.dispose();
  }
}
