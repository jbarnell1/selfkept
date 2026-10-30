import 'package:flutter/foundation.dart';

import '../models/insight.dart';
import '../services/insights_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// Insights grouped by one day, newest day first.
class InsightDay {
  final DateTime day;
  final List<Insight> insights;

  const InsightDay({required this.day, required this.insights});
}

/// The archive, its filters, and the one place a card is reacted to or
/// dismissed. Today's cards are the same objects, so [known] lets Today show
/// a reaction the moment it is tapped on either screen.
class InsightsProvider extends ChangeNotifier with SafeNotifier {
  final InsightsService _service;
  final AuthProvider _auth;
  final DateTime Function() _clock;

  List<Insight> _insights = const [];
  final Map<String, Insight> _known = {};
  bool _loading = false;
  bool _hasLoaded = false;
  bool _includeDismissed = false;
  InsightSeverity? _severity;
  bool _recomputing = false;
  String? _error;

  InsightsProvider({
    required this._service,
    required this._auth,
    DateTime Function()? clock,
  }) : _clock = clock ?? (() => DateTime.now().toUtc()) {
    _auth.addListener(_onAuthChanged);
  }

  /// How far back the archive reaches.
  static const int archiveDays = 90;

  /// Everything loaded, newest first, unfiltered.
  List<Insight> get insights => _insights;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  bool get includeDismissed => _includeDismissed;
  InsightSeverity? get severity => _severity;
  bool get isRecomputing => _recomputing;
  String? get error => _error;

  /// The freshest copy of a card this provider has seen, or null.
  Insight? known(String id) => _known[id];

  /// What the archive shows after the severity filter and dismissed toggle.
  List<Insight> get visible => [
    for (final i in _insights)
      if ((_includeDismissed || !i.isDismissed) &&
          (_severity == null || i.severity == _severity))
        i,
  ];

  /// [visible], grouped by calendar day, newest day first.
  List<InsightDay> get byDay {
    final groups = <DateTime, List<Insight>>{};
    for (final i in visible) {
      final day = DateTime(i.day.year, i.day.month, i.day.day);
      groups.putIfAbsent(day, () => []).add(i);
    }
    final days = groups.keys.toList()..sort((a, b) => b.compareTo(a));
    return [for (final d in days) InsightDay(day: d, insights: groups[d]!)];
  }

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      final now = _clock();
      final list = await _service.list(
        from: now.subtract(const Duration(days: archiveDays)),
        to: now,
        includeDismissed: _includeDismissed,
      );
      list.sort((a, b) => b.day.compareTo(a.day));
      _insights = list;
      for (final i in list) {
        _known[i.id] = i;
      }
      _hasLoaded = true;
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Dismissed cards come from the server, so flipping this reloads.
  Future<void> setIncludeDismissed(bool value) async {
    if (_includeDismissed == value) return;
    _includeDismissed = value;
    notify();
    await load();
  }

  void setSeverity(InsightSeverity? value) {
    if (_severity == value) return;
    _severity = value;
    notify();
  }

  /// Optimistic: the card shows the reaction at once and loses it again if
  /// the server refuses.
  Future<bool> react(Insight insight, InsightReaction reaction) async {
    final before = _known[insight.id] ?? insight;
    _apply(before.copyWith(reaction: reaction));
    try {
      _apply(await _service.react(insight.id, reaction));
      return true;
    } catch (e) {
      _apply(before);
      _error = describeError(e);
      notify();
      return false;
    }
  }

  /// Optimistic: the card leaves Today and the archive at once.
  Future<bool> dismiss(Insight insight) async {
    final before = _known[insight.id] ?? insight;
    _apply(before.copyWith(dismissedAt: _clock()));
    try {
      _apply(await _service.dismiss(insight.id));
      return true;
    } catch (e) {
      _apply(before);
      _error = describeError(e);
      notify();
      return false;
    }
  }

  /// Asks the server to rebuild baselines and cards. True when accepted.
  Future<bool> recompute() async {
    _recomputing = true;
    _error = null;
    notify();
    try {
      await _service.recompute();
      return true;
    } catch (e) {
      _error = describeError(e);
      return false;
    } finally {
      _recomputing = false;
      notify();
    }
  }

  void clearError() {
    if (_error == null) return;
    _error = null;
    notify();
  }

  void _apply(Insight insight) {
    _known[insight.id] = insight;
    final index = _insights.indexWhere((i) => i.id == insight.id);
    if (index >= 0) {
      final next = List.of(_insights);
      next[index] = insight;
      _insights = next;
    }
    notify();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated) return;
    if (!_hasLoaded && _known.isEmpty) return;
    _insights = const [];
    _known.clear();
    _hasLoaded = false;
    _severity = null;
    _includeDismissed = false;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
