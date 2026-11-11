import 'package:flutter/foundation.dart';

import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// Whose record the Progress, Goals and Medicines pages are showing: your
/// own (the default) or that of a household member who shares with you.
/// Writes are always your own, so the pages hide their edit controls while
/// someone else is shown.
class ViewedPerson extends ChangeNotifier with SafeNotifier {
  final AuthProvider? _auth;

  String? _personId;
  String _displayName = '';

  ViewedPerson({this._auth}) {
    _auth?.addListener(_onAuthChanged);
  }

  /// Null while you are looking at your own record.
  String? get personId => _personId;

  /// The other person's name; empty for your own record.
  String get displayName => _displayName;

  bool get isSelf => _personId == null;

  /// Shows [personId]'s record. Null, or your own id, goes back to yours.
  void view(String? personId, {String displayName = ''}) {
    final own = _auth?.user?.person.id;
    final next = (personId == null || personId == own) ? null : personId;
    if (next == _personId) return;
    _personId = next;
    _displayName = next == null ? '' : displayName;
    notify();
  }

  void viewSelf() => view(null);

  void _onAuthChanged() {
    if (_auth?.isAuthenticated ?? true) return;
    viewSelf();
  }

  @override
  void dispose() {
    _auth?.removeListener(_onAuthChanged);
    super.dispose();
  }
}
