import 'package:flutter/foundation.dart';

import '../models/insight.dart';
import '../models/trial.dart';
import '../services/insights_service.dart';
import '../services/trials_service.dart';
import '../utils/api_error.dart';
import 'person_read.dart';

/// Trials and, for each one, the server's latest word on it: the
/// `trial_ready` or `retest_due` card that names it.
class TrialsState {
  final List<Trial> trials;

  /// Newest card first, by trial id.
  final Map<String, Insight> cards;

  const TrialsState({this.trials = const [], this.cards = const {}});

  List<Trial> get active => [
    for (final t in trials)
      if (t.isActive) t,
  ];

  List<Trial> get ended => [
    for (final t in trials)
      if (!t.isActive) t,
  ];
}

/// "Trying something": the list, the editor's writes, and the cards that
/// say a trial is ready. Writes are your own only.
class TrialsProvider extends PersonRead<TrialsState> {
  /// The card kinds that speak about one trial.
  static const Set<String> cardKinds = {'trial_ready', 'retest_due'};

  final TrialsService _service;
  final InsightsService? _insights;
  final DateTime Function() _now;

  bool _saving = false;
  String? _actionError;

  TrialsProvider({
    required this._service,
    this._insights,
    DateTime Function()? now,
    super.auth,
    super.viewing,
  }) : _now = now ?? DateTime.now;

  @override
  Future<TrialsState> fetch(String? personId) async {
    final trials = await _service.list(personId: personId);
    return TrialsState(trials: trials, cards: await _cards(personId));
  }

  /// The cards are a bonus: without the insights share, or on a failure,
  /// the trials still show.
  Future<Map<String, Insight>> _cards(String? personId) async {
    final insights = _insights;
    if (insights == null) return const {};
    final now = _now();
    try {
      final cards = await insights.list(
        from: now.subtract(const Duration(days: 30)),
        to: now,
        personId: personId,
      );
      final byTrial = <String, Insight>{};
      for (final card in cards) {
        if (!cardKinds.contains(card.kind)) continue;
        final id = card.evidence['trialId']?.toString();
        if (id == null || byTrial.containsKey(id)) continue;
        byTrial[id] = card;
      }
      return byTrial;
    } catch (e) {
      debugPrint('Trial cards did not load: $e');
      return const {};
    }
  }

  bool get isSaving => _saving;
  String? get actionError => _actionError;

  void clearActionError() {
    if (_actionError == null) return;
    _actionError = null;
    notify();
  }

  Future<Trial?> create(TrialDraft draft) => _act(() => _service.create(draft));

  Future<Trial?> update(String id, Map<String, dynamic> changes) =>
      _act(() => _service.update(id, changes));

  /// Ends a trial as done or dropped, with the person's own words on it.
  Future<Trial?> finish(Trial trial, TrialStatus status, {String? outcome}) =>
      update(trial.id, {
        'status': status.wireValue,
        if (outcome != null) 'outcome': outcome.trim(),
      });

  Future<bool> remove(String id) async =>
      await _act<bool>(() async {
        await _service.delete(id);
        return true;
      }) ??
      false;

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
    await reloadQuietly();
    _saving = false;
    notify();
    return result;
  }

  @override
  void clear() {
    _actionError = null;
  }
}
