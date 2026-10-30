import 'package:flutter/foundation.dart';

import '../models/setup.dart';
import '../services/setup_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The first-run answers being chosen, and saving them. The saved copy lives
/// on the signed-in [User]; this holds the draft between the two screens.
class SetupProvider extends ChangeNotifier with SafeNotifier {
  final SetupService _service;
  final AuthProvider _auth;

  final Set<SetupItem> _chosen = {};
  bool _seeded = false;
  bool _saving = false;
  String? _error;

  SetupProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  /// The chosen items in a stable order, the order of the questions.
  List<SetupItem> get chosen =>
      SetupItem.values.where(_chosen.contains).toList(growable: false);
  bool isChosen(SetupItem item) => _chosen.contains(item);
  bool get saving => _saving;
  String? get error => _error;

  /// Starts the draft from what the account already saved, or from
  /// [suggested] (this phone's own health store) the first time.
  void seed({Iterable<SetupItem> suggested = const []}) {
    if (_seeded) return;
    _seeded = true;
    final saved = _auth.user?.setupPlan ?? const [];
    _chosen
      ..clear()
      ..addAll(saved.isNotEmpty ? saved : suggested);
    notify();
  }

  void toggle(SetupItem item) {
    if (!_chosen.remove(item)) _chosen.add(item);
    notify();
  }

  /// Saves the answers without finishing. True on success.
  Future<bool> saveAnswers() => _save(completed: false, plan: chosen);

  /// Finishes setup with the answers already saved (the steps page shows
  /// those, not the draft).
  Future<bool> finish() =>
      _save(completed: true, plan: _auth.user?.setupPlan ?? chosen);

  /// Finishes setup with no answers: the person can find everything later.
  Future<bool> skip() {
    _chosen.clear();
    return _save(completed: true, plan: const []);
  }

  Future<bool> _save({
    required bool completed,
    required List<SetupItem> plan,
  }) async {
    _saving = true;
    _error = null;
    notify();
    try {
      final user = await _service.save(plan: plan, completed: completed);
      _auth.applyUser(user);
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
    if (_auth.isAuthenticated) return;
    _chosen.clear();
    _seeded = false;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
