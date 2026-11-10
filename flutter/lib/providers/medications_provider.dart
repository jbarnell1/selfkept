import 'package:flutter/foundation.dart';

import '../models/medication.dart';
import '../services/medications_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'viewed_person.dart';

/// The medicines and supplements a person has written down.
class MedicationsProvider extends ChangeNotifier with SafeNotifier {
  final MedicationsService _service;
  final AuthProvider _auth;
  final ViewedPerson? _viewing;

  List<Medication> _items = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  bool _notShared = false;
  bool _saving = false;
  String? _error;
  String? _saveError;

  MedicationsProvider({
    required this._service,
    required this._auth,
    this._viewing,
  }) {
    _auth.addListener(_onAuthChanged);
    _viewing?.addListener(_onViewingChanged);
  }

  /// Current items first, then the stopped ones, as the server orders them.
  List<Medication> get items => _items;
  List<Medication> get current => [
    for (final m in _items)
      if (m.active) m,
  ];
  List<Medication> get stopped => [
    for (final m in _items)
      if (!m.active) m,
  ];
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  bool get isSaving => _saving;
  String? get error => _error;
  String? get saveError => _saveError;

  /// The server answered 404: no such person, or the list is not shared.
  bool get notShared => _notShared;

  /// True while the list on screen is your own.
  bool get canEdit => _viewing?.isSelf ?? true;

  Future<void> load() async {
    _loading = true;
    _error = null;
    _notShared = false;
    notify();
    final personId = _viewing?.personId;
    List<Medication>? result;
    Object? failure;
    try {
      result = await _service.list(personId: personId);
    } catch (e) {
      failure = e;
    }
    if (personId != _viewing?.personId) return;
    if (failure == null) {
      _items = result ?? const [];
      _hasLoaded = true;
    } else {
      debugPrint('Medicines did not load: $failure');
      if (failure is ApiException && failure.isNotFound) {
        _notShared = true;
        _items = const [];
      } else {
        _error = describeError(failure);
      }
    }
    _loading = false;
    notify();
  }

  /// Creates, or updates when [id] is given. The list is read again so the
  /// order and the substance tags the server works out are right.
  Future<bool> save(MedicationDraft draft, {String? id}) async {
    if (_saving || !canEdit) return false;
    _saving = true;
    _saveError = null;
    notify();
    try {
      if (id == null) {
        await _service.create(draft);
      } else {
        await _service.update(id, draft);
      }
    } catch (e) {
      _saveError = describeError(e);
      _saving = false;
      notify();
      return false;
    }
    await _reloadQuietly();
    _saving = false;
    notify();
    return true;
  }

  Future<bool> remove(String id) async {
    if (!canEdit) return false;
    _saveError = null;
    try {
      await _service.delete(id);
      _items = [
        for (final m in _items)
          if (m.id != id) m,
      ];
      notify();
      return true;
    } catch (e) {
      _saveError = describeError(e);
      notify();
      return false;
    }
  }

  void clearSaveError() {
    if (_saveError == null) return;
    _saveError = null;
    notify();
  }

  Future<void> _reloadQuietly() async {
    final personId = _viewing?.personId;
    try {
      final result = await _service.list(personId: personId);
      if (personId != _viewing?.personId) return;
      _items = result;
      _hasLoaded = true;
    } catch (e) {
      debugPrint('Medicines did not reload: $e');
    }
  }

  void _onViewingChanged() {
    final attempted = _hasLoaded || _error != null || _notShared || _loading;
    _items = const [];
    _hasLoaded = false;
    _notShared = false;
    _error = null;
    _saveError = null;
    notify();
    if (attempted && !isDisposed) load();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || (!_hasLoaded && _error == null)) return;
    _items = const [];
    _hasLoaded = false;
    _notShared = false;
    _error = null;
    _saveError = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    _viewing?.removeListener(_onViewingChanged);
    super.dispose();
  }
}
