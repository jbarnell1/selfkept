import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
import '../services/assistant_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'record_changes.dart';

/// What Today shows from the assistant: the morning brief and the state of
/// tonight's check-in. The check-in state reloads whenever the record
/// changes, so a turn taken elsewhere shows when Today comes back.
class AssistantTodayProvider extends ChangeNotifier with SafeNotifier {
  final AssistantService _service;
  final AuthProvider _auth;
  final RecordChanges _changes;

  MorningBrief? _brief;
  CheckinToday? _checkin;
  bool _loading = false;
  bool _hasLoaded = false;
  String? _briefError;
  String? _checkinError;

  AssistantTodayProvider({
    required this._service,
    required this._auth,
    required this._changes,
  }) {
    _auth.addListener(_onAuthChanged);
    _changes.addListener(_onRecordChanged);
  }

  MorningBrief? get brief => _brief;
  CheckinToday? get checkin => _checkin;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get briefError => _briefError;
  String? get checkinError => _checkinError;

  Future<void> load() async {
    _loading = true;
    notify();
    await Future.wait([_loadBrief(), loadCheckin(quiet: true)]);
    _hasLoaded = true;
    _loading = false;
    notify();
  }

  Future<void> _loadBrief() async {
    _briefError = null;
    try {
      _brief = await _service.brief();
    } catch (e) {
      debugPrint('Morning brief did not load: $e');
      _brief = null;
      _briefError = describeError(e);
    }
  }

  /// Reloads tonight's check-in state. What is on screen stays on failure.
  Future<void> loadCheckin({bool quiet = false}) async {
    _checkinError = null;
    try {
      _checkin = await _service.checkinToday();
    } catch (e) {
      debugPrint('Check-in state did not load: $e');
      _checkinError = describeError(e);
    }
    if (!quiet) notify();
  }

  void _onRecordChanged() {
    if (identical(_changes.origin, this) || !_hasLoaded || isDisposed) return;
    loadCheckin();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || !_hasLoaded) return;
    _brief = null;
    _checkin = null;
    _hasLoaded = false;
    _briefError = null;
    _checkinError = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    _changes.removeListener(_onRecordChanged);
    super.dispose();
  }
}
