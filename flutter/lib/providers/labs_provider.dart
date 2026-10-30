import 'package:flutter/foundation.dart';

import '../models/lab.dart';
import '../services/labs_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The marker catalogue with the person's latest values, and their panels.
class LabsProvider extends ChangeNotifier with SafeNotifier {
  final LabsService _service;
  final AuthProvider? _auth;

  List<LabMarker> _markers = const [];
  List<LabReport> _reports = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  String? _error;

  /// [auth], when given, clears everything on sign-out so one person's panels
  /// are never shown to the next.
  LabsProvider(this._service, {this._auth}) {
    _auth?.addListener(_onAuthChanged);
  }

  List<LabMarker> get markers => _markers;
  List<LabReport> get reports => _reports;
  bool get loading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get error => _error;

  /// True once any marker has a value.
  bool get hasAnyResults => _markers.any((m) => m.latestValue != null);

  /// Markers and reports, fetched together.
  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      final results = await Future.wait<Object>([
        _service.markers(),
        _service.reports(),
      ]);
      _markers = results[0] as List<LabMarker>;
      _reports = results[1] as List<LabReport>;
      _hasLoaded = true;
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  Future<void> refresh() => load();

  /// Deletes a panel, then reloads. True when the delete went through.
  Future<bool> deleteReport(String id) async {
    try {
      await _service.deleteReport(id);
    } catch (e) {
      _error = describeError(e);
      notify();
      return false;
    }
    await load();
    return true;
  }

  void _onAuthChanged() {
    if (_auth?.isAuthenticated ?? true) return;
    if (!_hasLoaded && _markers.isEmpty && _reports.isEmpty) return;
    _markers = const [];
    _reports = const [];
    _hasLoaded = false;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth?.removeListener(_onAuthChanged);
    super.dispose();
  }
}
