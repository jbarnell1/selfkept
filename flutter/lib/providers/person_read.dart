import 'package:flutter/foundation.dart';

import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'viewed_person.dart';

/// One read of the record of the person being viewed (yourself, or someone
/// who shares with you), loaded the way Progress loads: a 404 means "not
/// shared", a change of person starts again, a sign-out forgets everything.
abstract class PersonRead<T> extends ChangeNotifier with SafeNotifier {
  final AuthProvider? _auth;
  final ViewedPerson? _viewing;

  T? _data;
  bool _loading = false;
  bool _hasLoaded = false;
  bool _notShared = false;
  String? _error;

  PersonRead({this._auth, this._viewing}) {
    _auth?.addListener(_onAuthChanged);
    _viewing?.addListener(_onViewingChanged);
  }

  /// The read itself. [personId] is null for your own record.
  @protected
  Future<T> fetch(String? personId);

  T? get data => _data;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get error => _error;

  /// The server answered 404: no such person, or nothing shared.
  bool get notShared => _notShared;

  /// Null while you are looking at your own record.
  String? get personId => _viewing?.personId;

  /// The other person's name; empty for your own record.
  String get viewedName => _viewing?.displayName ?? '';

  /// True while the record on screen is your own, so it can be written to.
  bool get canEdit => _viewing?.isSelf ?? true;

  /// Loads unless a load has run or is running.
  void ensureLoaded() {
    if (_hasLoaded || _loading || _notShared || _error != null) return;
    load();
  }

  Future<void> load() async {
    _loading = true;
    _error = null;
    _notShared = false;
    notify();
    final who = personId;
    T? result;
    Object? failure;
    try {
      result = await fetch(who);
    } catch (e) {
      failure = e;
    }
    // Someone else was chosen while this was on its way: theirs wins.
    if (who != personId || isDisposed) return;
    if (failure == null) {
      _data = result;
      _hasLoaded = true;
    } else {
      debugPrint('$runtimeType did not load: $failure');
      if (failure is ApiException && failure.isNotFound) {
        _notShared = true;
        _data = null;
      } else {
        _error = describeError(failure);
      }
    }
    _loading = false;
    notify();
  }

  Future<void> refresh() => load();

  /// Reads again after a write, keeping what is on screen if it fails.
  @protected
  Future<void> reloadQuietly() async {
    final who = personId;
    try {
      final result = await fetch(who);
      if (who != personId || isDisposed) return;
      _data = result;
      _hasLoaded = true;
      _notShared = false;
      _error = null;
      notify();
    } catch (e) {
      debugPrint('$runtimeType did not reload: $e');
    }
  }

  /// Called when the record goes away (a new person, a sign-out).
  /// Subclasses clear their own state here.
  @protected
  void clear() {}

  void _reset() {
    _data = null;
    _hasLoaded = false;
    _notShared = false;
    _error = null;
    clear();
  }

  void _onViewingChanged() {
    final attempted = _hasLoaded || _error != null || _notShared || _loading;
    _reset();
    notify();
    if (attempted && !isDisposed) load();
  }

  void _onAuthChanged() {
    if (_auth?.isAuthenticated ?? true) return;
    if (!_hasLoaded && _error == null && !_notShared) return;
    _reset();
    notify();
  }

  @override
  void dispose() {
    _auth?.removeListener(_onAuthChanged);
    _viewing?.removeListener(_onViewingChanged);
    super.dispose();
  }
}
