import 'package:flutter/foundation.dart';

import '../models/trend.dart';
import '../services/trends_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The metric list, the selected metric and its series for 28 or 90 days.
/// Series are cached per metric and window for the session.
class TrendsProvider extends ChangeNotifier with SafeNotifier {
  final TrendsService _service;
  final AuthProvider _auth;

  List<TrendMetric> _metrics = const [];
  String? _selected;
  int _days = 28;
  final Map<String, TrendSeries> _series = {};
  bool _loadingMetrics = false;
  bool _hasLoadedMetrics = false;
  bool _loadingSeries = false;
  String? _error;
  String? _seriesError;
  int _request = 0;

  TrendsProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  static const List<int> windows = [28, 90];

  List<TrendMetric> get metrics => _metrics;
  String? get selected => _selected;
  TrendMetric? get selectedMetric {
    for (final m in _metrics) {
      if (m.metric == _selected) return m;
    }
    return null;
  }

  int get days => _days;
  bool get isLoadingMetrics => _loadingMetrics;
  bool get hasLoadedMetrics => _hasLoadedMetrics;
  bool get isLoadingSeries => _loadingSeries;
  String? get error => _error;
  String? get seriesError => _seriesError;

  /// The series for the selected metric and window, if it has arrived.
  TrendSeries? get series =>
      _selected == null ? null : _series[_key(_selected!, _days)];

  Future<void> loadMetrics() async {
    _loadingMetrics = true;
    _error = null;
    notify();
    try {
      _metrics = await _service.metrics();
      _hasLoadedMetrics = true;
      if (_metrics.isEmpty) {
        _selected = null;
      } else if (_selected == null ||
          !_metrics.any((m) => m.metric == _selected)) {
        _selected = _metrics.first.metric;
      }
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loadingMetrics = false;
      notify();
    }
    if (_selected != null && series == null) await _loadSeries();
  }

  Future<void> select(String metric) async {
    if (_selected == metric) return;
    _selected = metric;
    _seriesError = null;
    notify();
    if (series == null) await _loadSeries();
  }

  Future<void> setDays(int days) async {
    if (_days == days || !windows.contains(days)) return;
    _days = days;
    _seriesError = null;
    notify();
    if (_selected != null && series == null) await _loadSeries();
  }

  /// Drops the cache and fetches the current view again.
  Future<void> refresh() async {
    _series.clear();
    await loadMetrics();
  }

  Future<void> retrySeries() => _loadSeries();

  Future<void> _loadSeries() async {
    final metric = _selected;
    if (metric == null) return;
    final days = _days;
    final ticket = ++_request;
    _loadingSeries = true;
    _seriesError = null;
    notify();
    try {
      final result = await _service.series(metric, days: days);
      _series[_key(metric, days)] = result;
    } catch (e) {
      if (ticket == _request) _seriesError = describeError(e);
    } finally {
      if (ticket == _request) _loadingSeries = false;
      notify();
    }
  }

  static String _key(String metric, int days) => '$metric:$days';

  void _onAuthChanged() {
    if (_auth.isAuthenticated || !_hasLoadedMetrics) return;
    _metrics = const [];
    _series.clear();
    _selected = null;
    _days = 28;
    _hasLoadedMetrics = false;
    _error = null;
    _seriesError = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
