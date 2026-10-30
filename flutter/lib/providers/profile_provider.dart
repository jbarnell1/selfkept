import 'package:flutter/foundation.dart';

import '../models/person.dart';
import '../services/profile_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The signed-in person's profile: load, edit, save.
class ProfileProvider extends ChangeNotifier with SafeNotifier {
  final ProfileService _service;
  final AuthProvider _auth;

  Person? _person;
  bool _loading = false;
  bool _saving = false;
  String? _error;

  ProfileProvider({required this._service, required this._auth}) {
    _person = _auth.user?.person;
    _auth.addListener(_onAuthChanged);
  }

  Person? get person => _person;
  bool get isLoading => _loading;
  bool get isSaving => _saving;
  String? get error => _error;

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      _person = await _service.getProfile();
      final current = _person;
      if (current != null) _auth.applyPerson(current);
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Saves and returns true, or records the error and returns false.
  Future<bool> save(Person draft) async {
    _saving = true;
    _error = null;
    notify();
    try {
      _person = await _service.updateProfile(draft);
      final saved = _person;
      if (saved != null) _auth.applyPerson(saved);
      return true;
    } catch (e) {
      _error = describeError(e);
      return false;
    } finally {
      _saving = false;
      notify();
    }
  }

  Future<bool> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    _saving = true;
    _error = null;
    notify();
    try {
      await _service.changePassword(
        currentPassword: currentPassword,
        newPassword: newPassword,
      );
      return true;
    } catch (e) {
      _error = describeError(e);
      return false;
    } finally {
      _saving = false;
      notify();
    }
  }

  void clearError() {
    if (_error == null) return;
    _error = null;
    notify();
  }

  void _onAuthChanged() {
    if (!_auth.isAuthenticated) {
      if (_person == null) return;
      _person = null;
      notify();
      return;
    }
    if (_person == null && _auth.user != null) {
      _person = _auth.user!.person;
      notify();
    }
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
