import 'package:flutter/foundation.dart';

import '../models/insight.dart';
import '../models/today.dart';
import '../services/today_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'checkins_provider.dart';
import 'insights_provider.dart';

/// The Today screen's data. Reloads after a check-in is kept (the sentence
/// mentions it) and reflects reactions made through [InsightsProvider]
/// without a round trip.
class TodayProvider extends ChangeNotifier with SafeNotifier {
  final TodayService _service;
  final AuthProvider _auth;
  final CheckinsProvider _checkins;
  final InsightsProvider _insights;

  Today? _today;
  bool _loading = false;
  bool _hasLoaded = false;
  String? _error;
  int _seenKeptCount = 0;

  TodayProvider({
    required this._service,
    required this._auth,
    required this._checkins,
    required this._insights,
  }) {
    _seenKeptCount = _checkins.keptCount;
    _auth.addListener(_onAuthChanged);
    _checkins.addListener(_onCheckinsChanged);
    _insights.addListener(notify);
  }

  Today? get today => _today;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get error => _error;

  /// Today's cards with the freshest reaction state, minus anything the
  /// person has dismissed.
  List<Insight> get cards {
    final current = _today;
    if (current == null) return const [];
    return [
      for (final c in current.cards)
        if (!(_insights.known(c.id) ?? c).isDismissed)
          _insights.known(c.id) ?? c,
    ];
  }

  /// Loads or reloads. Data already on screen stays while the request runs
  /// and survives a failure; [error] says what happened.
  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      _today = await _service.today();
      _hasLoaded = true;
    } catch (e, stack) {
      debugPrint('Today did not load: $e');
      debugPrint(stack.toString());
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Alias for pull to refresh.
  Future<void> refresh() => load();

  void _onCheckinsChanged() {
    final count = _checkins.keptCount;
    if (count == _seenKeptCount) return;
    _seenKeptCount = count;
    if (_hasLoaded && !_loading) load();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || !_hasLoaded) return;
    _today = null;
    _hasLoaded = false;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    _checkins.removeListener(_onCheckinsChanged);
    _insights.removeListener(notify);
    super.dispose();
  }
}
