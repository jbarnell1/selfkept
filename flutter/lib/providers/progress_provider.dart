import 'package:flutter/foundation.dart';

import '../models/goal.dart';
import '../models/progress.dart';
import '../models/strength.dart';
import '../services/body_service.dart';
import '../services/goals_service.dart';
import '../services/progress_service.dart';
import '../services/strength_service.dart';
import '../services/targets_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'record_changes.dart';
import 'viewed_person.dart';

/// The Progress tab: goals, the body, the charts and the strength log in one
/// read, plus the things a person can add from it.
class ProgressProvider extends ChangeNotifier with SafeNotifier {
  final ProgressService _service;
  final GoalsService _goals;
  final TargetsService _targets;
  final BodyService _body;
  final StrengthService _strength;
  final AuthProvider _auth;
  final RecordChanges _changes;
  final ViewedPerson? _viewing;

  Progress? _progress;
  bool _loading = false;
  bool _hasLoaded = false;
  bool _notShared = false;
  bool _saving = false;
  String? _error;
  String? _actionError;
  GoalKind? _startingKind;

  ProgressProvider({
    required this._service,
    required this._goals,
    required this._targets,
    required this._body,
    required this._strength,
    required this._auth,
    required this._changes,
    this._viewing,
  }) {
    _auth.addListener(_onAuthChanged);
    _changes.addListener(_onRecordChanged);
    _viewing?.addListener(_onViewingChanged);
  }

  Progress? get progress => _progress;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get error => _error;

  /// The server answered 404: no such person, or nothing shared.
  bool get notShared => _notShared;

  /// True while the record on screen is your own, so it can be written to.
  bool get canEdit => _viewing?.isSelf ?? true;

  /// A measurement, a set or a goal is being saved.
  bool get isSaving => _saving;

  /// Why the last save failed, in the server's words.
  String? get actionError => _actionError;

  /// The suggestion being started, so its button can say so.
  GoalKind? get startingKind => _startingKind;

  Future<void> load() async {
    _loading = true;
    _error = null;
    _notShared = false;
    notify();
    final personId = _viewing?.personId;
    Progress? result;
    Object? failure;
    try {
      result = await _service.progress(personId: personId);
    } catch (e) {
      failure = e;
    }
    // Someone else was chosen while this was on its way: theirs wins.
    if (personId != _viewing?.personId) return;
    if (failure == null) {
      _progress = result;
      _hasLoaded = true;
    } else {
      debugPrint('Progress did not load: $failure');
      if (failure is ApiException && failure.isNotFound) {
        _progress = null;
        // Your own record is never withheld from you: the server does not
        // have the page, which is an error with a retry.
        if (personId == null) {
          _error = missingOnServerMessage;
        } else {
          _notShared = true;
        }
      } else {
        _error = describeError(failure);
      }
    }
    _loading = false;
    notify();
  }

  /// Creates the suggested goal, then its targets. Without [slot] the main
  /// slot is used when it is free, otherwise the secondary one.
  Future<bool> startGoal(GoalSuggestion suggestion, {GoalSlot? slot}) async {
    if (_saving || !canEdit) return false;
    _saving = true;
    _startingKind = suggestion.kind;
    _actionError = null;
    notify();
    try {
      final mainTaken = (_progress?.goals ?? const <GoalProgress>[]).any(
        (g) => g.goal.isActive && g.goal.slot == GoalSlot.main,
      );
      final goal = await _goals.create(
        GoalDraft(
          slot: slot ?? (mainTaken ? GoalSlot.secondary : GoalSlot.main),
          kind: suggestion.kind,
          title: suggestion.title,
          metricKey: suggestion.metricKey,
          startValue: suggestion.startValue,
        ),
      );
      for (final target in suggestion.targets) {
        await _targets.create(target.toDraft(goalId: goal.id));
      }
    } catch (e) {
      _actionError = describeError(e);
      _saving = false;
      _startingKind = null;
      notify();
      // A goal may exist without all of its targets: show what is there.
      await _reloadQuietly();
      _changes.changed(this);
      return false;
    }
    await _reloadQuietly();
    _saving = false;
    _startingKind = null;
    notify();
    _changes.changed(this);
    return true;
  }

  /// [value] is metric: kg, percent or cm. Returns the saved reading so it
  /// can be undone, or null when the server refused.
  Future<Measurement?> addMeasurement(
    BodyMeasure measure,
    double value, {
    DateTime? at,
  }) => _act(() => _body.addMeasurement(measure, value, at: at));

  /// Undoes or removes a typed reading.
  Future<bool> deleteMeasurement(BodyMeasure measure, String id) async =>
      await _act<bool>(() async {
        await _body.deleteMeasurement(measure, id);
        return true;
      }) ??
      false;

  /// Saves the day's top set of a lift. Saving the same lift on the same day
  /// replaces the earlier set.
  Future<StrengthSet?> addStrengthSet(StrengthSetDraft draft) =>
      _act(() => _strength.save(draft));

  Future<bool> deleteStrengthSet(String id) async =>
      await _act<bool>(() async {
        await _strength.delete(id);
        return true;
      }) ??
      false;

  void clearActionError() {
    if (_actionError == null) return;
    _actionError = null;
    notify();
  }

  Future<T?> _act<T>(Future<T> Function() action) async {
    if (_saving || !canEdit) return null;
    _saving = true;
    _actionError = null;
    notify();
    final T result;
    try {
      result = await action();
    } catch (e) {
      _actionError = describeError(e);
      _saving = false;
      notify();
      return null;
    }
    await _reloadQuietly();
    _saving = false;
    notify();
    _changes.changed(this);
    return result;
  }

  Future<void> _reloadQuietly() async {
    final personId = _viewing?.personId;
    try {
      final result = await _service.progress(personId: personId);
      if (personId != _viewing?.personId) return;
      _progress = result;
      _hasLoaded = true;
      _notShared = false;
      _error = null;
      notify();
    } catch (e) {
      debugPrint('Progress did not reload: $e');
    }
  }

  void _onRecordChanged() {
    if (identical(_changes.origin, this) || isDisposed) return;
    if (_hasLoaded) {
      _reloadQuietly();
    } else if ((_error != null || _notShared) && !_loading) {
      // A page that failed gets another go, so an updated server shows.
      load();
    }
  }

  void _onViewingChanged() {
    final attempted = _hasLoaded || _error != null || _notShared || _loading;
    _progress = null;
    _hasLoaded = false;
    _notShared = false;
    _error = null;
    _actionError = null;
    notify();
    if (attempted && !isDisposed) load();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated) return;
    if (!_hasLoaded && _error == null && !_notShared) return;
    _progress = null;
    _hasLoaded = false;
    _notShared = false;
    _error = null;
    _actionError = null;
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
