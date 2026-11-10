import 'package:flutter/foundation.dart';

import '../models/goal.dart';
import '../models/target.dart';
import '../services/goals_service.dart';
import '../services/targets_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'record_changes.dart';
import 'viewed_person.dart';

/// The goals page: the active goals and every target, with their edits.
class GoalsProvider extends ChangeNotifier with SafeNotifier {
  final GoalsService _goalsService;
  final TargetsService _targetsService;
  final AuthProvider _auth;
  final RecordChanges _changes;
  final ViewedPerson? _viewing;

  List<Goal> _goals = const [];
  List<Target> _targets = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  bool _notShared = false;
  bool _saving = false;
  String? _error;
  String? _saveError;

  GoalsProvider({
    required GoalsService goals,
    required TargetsService targets,
    required this._auth,
    required this._changes,
    this._viewing,
  }) : _goalsService = goals,
       _targetsService = targets {
    _auth.addListener(_onAuthChanged);
    _changes.addListener(_onRecordChanged);
    _viewing?.addListener(_onViewingChanged);
  }

  /// The active goals.
  List<Goal> get goals => _goals;

  /// Every target in sort order, paused ones included.
  List<Target> get targets => _targets;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  bool get isSaving => _saving;
  String? get error => _error;
  String? get saveError => _saveError;

  /// The server answered 404: no such person, or goals are not shared.
  bool get notShared => _notShared;

  /// True while the goals on screen are your own.
  bool get canEdit => _viewing?.isSelf ?? true;

  Goal? goalIn(GoalSlot slot) {
    for (final goal in _goals) {
      if (goal.slot == slot) return goal;
    }
    return null;
  }

  Goal? goalById(String? id) {
    if (id == null) return null;
    for (final goal in _goals) {
      if (goal.id == id) return goal;
    }
    return null;
  }

  List<Target> targetsFor(String goalId) => [
    for (final t in _targets)
      if (t.goalId == goalId) t,
  ];

  /// Targets that belong to no active goal. A target outlives its goal.
  List<Target> get standaloneTargets {
    final ids = {for (final g in _goals) g.id};
    return [
      for (final t in _targets)
        if (t.goalId == null || !ids.contains(t.goalId)) t,
    ];
  }

  Future<void> load() async {
    _loading = true;
    _error = null;
    _notShared = false;
    notify();
    final personId = _viewing?.personId;
    Object? failure;
    try {
      await _fetch(personId);
    } catch (e) {
      failure = e;
    }
    if (personId != _viewing?.personId) return;
    if (failure == null) {
      _hasLoaded = true;
    } else {
      debugPrint('Goals did not load: $failure');
      if (failure is ApiException && failure.isNotFound) {
        _notShared = true;
        _goals = const [];
        _targets = const [];
      } else {
        _error = describeError(failure);
      }
    }
    _loading = false;
    notify();
  }

  /// Creates, or updates when [id] is given.
  Future<bool> saveGoal(GoalDraft draft, {String? id}) => _act(
    () => id == null
        ? _goalsService.create(draft)
        : _goalsService.update(id, draft),
  );

  /// [status] is done or dropped.
  Future<bool> endGoal(String id, GoalStatus status) =>
      _act(() => _goalsService.end(id, status));

  Future<bool> saveTarget(TargetDraft draft, {String? id}) => _act(
    () => id == null
        ? _targetsService.create(draft)
        : _targetsService.update(id, draft),
  );

  Future<bool> deleteTarget(String id) =>
      _act(() => _targetsService.delete(id));

  /// Moves a target [by] places in the list (negative is up). The server has
  /// no reorder route, so every target whose place changes is saved with its
  /// new sort order.
  Future<bool> moveTarget(String id, int by) {
    final order = List.of(_targets);
    final from = order.indexWhere((t) => t.id == id);
    if (from < 0) return Future.value(false);
    final to = (from + by).clamp(0, order.length - 1);
    if (to == from) return Future.value(false);
    order.insert(to, order.removeAt(from));
    return _act(() async {
      for (var i = 0; i < order.length; i++) {
        final t = order[i];
        if (t.sortOrder == i) continue;
        await _targetsService.update(
          t.id,
          TargetDraft.fromTarget(t, sortOrder: i),
        );
      }
      return null;
    });
  }

  void clearSaveError() {
    if (_saveError == null) return;
    _saveError = null;
    notify();
  }

  Future<bool> _act(Future<Object?> Function() action) async {
    if (_saving || !canEdit) return false;
    _saving = true;
    _saveError = null;
    notify();
    try {
      await action();
    } catch (e) {
      _saveError = describeError(e);
      _saving = false;
      notify();
      await _reloadQuietly();
      return false;
    }
    await _reloadQuietly();
    _saving = false;
    notify();
    _changes.changed(this);
    return true;
  }

  Future<void> _fetch(String? personId) async {
    final results = await Future.wait<Object>([
      _goalsService.list(personId: personId),
      _targetsService.list(personId: personId),
    ]);
    if (personId != _viewing?.personId) return;
    _goals = [
      for (final g in results[0] as List<Goal>)
        if (g.isActive) g,
    ];
    final targets = List.of(results[1] as List<Target>)
      ..sort((a, b) => a.sortOrder.compareTo(b.sortOrder));
    _targets = targets;
  }

  Future<void> _reloadQuietly() async {
    final personId = _viewing?.personId;
    try {
      await _fetch(personId);
      if (personId != _viewing?.personId) return;
      _hasLoaded = true;
      _error = null;
      notify();
    } catch (e) {
      debugPrint('Goals did not reload: $e');
    }
  }

  void _onRecordChanged() {
    if (identical(_changes.origin, this)) return;
    if (_hasLoaded && !isDisposed) _reloadQuietly();
  }

  void _onViewingChanged() {
    final attempted = _hasLoaded || _error != null || _notShared || _loading;
    _goals = const [];
    _targets = const [];
    _hasLoaded = false;
    _notShared = false;
    _error = null;
    _saveError = null;
    notify();
    if (attempted && !isDisposed) load();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || (!_hasLoaded && _error == null)) return;
    _goals = const [];
    _targets = const [];
    _hasLoaded = false;
    _notShared = false;
    _error = null;
    _saveError = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    _changes.removeListener(_onRecordChanged);
    _viewing?.removeListener(_onViewingChanged);
    super.dispose();
  }
}
