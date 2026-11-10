import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
import '../services/assistant_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The Assistant tab: whether the home model is answering, and the list of
/// conversations.
class AssistantProvider extends ChangeNotifier with SafeNotifier {
  final AssistantService _service;
  final AuthProvider _auth;

  AssistantStatus? _status;
  List<ConversationSummary> _conversations = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  bool _creating = false;
  String? _error;
  String? _actionError;

  AssistantProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  /// Null until the status has answered once, or when it failed.
  AssistantStatus? get status => _status;
  List<ConversationSummary> get conversations => _conversations;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  bool get isCreating => _creating;
  String? get error => _error;

  /// Why the last create or delete failed.
  String? get actionError => _actionError;

  /// True when replies come from templates because the home model is off or
  /// did not answer. Unknown status counts as answering, so a failed status
  /// call does not warn about a model that may be fine.
  bool get modelQuiet => _status != null && !_status!.modelAnswering;

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    await Future.wait([_loadStatus(), _loadList()]);
    _loading = false;
    notify();
  }

  Future<void> _loadStatus() async {
    try {
      _status = await _service.status();
    } catch (e) {
      debugPrint('Assistant status did not load: $e');
    }
  }

  Future<void> _loadList() async {
    try {
      _conversations = await _service.conversations();
      _hasLoaded = true;
    } catch (e) {
      debugPrint('Conversations did not load: $e');
      _error = describeError(e);
    }
  }

  /// For a chat opened straight from a link: the note about the home model
  /// needs the status even when the tab was never shown.
  Future<void> ensureStatus() async {
    if (_status != null) return;
    await _loadStatus();
    notify();
  }

  /// Reloads the list only, after a chat changed its title or order.
  Future<void> refreshList() async {
    await _loadList();
    notify();
  }

  /// A new, empty chat. Null when the server refused; [actionError] says why.
  Future<ConversationSummary?> create() async {
    _creating = true;
    _actionError = null;
    notify();
    try {
      final created = await _service.create();
      _conversations = [created, ..._conversations];
      return created;
    } catch (e) {
      _actionError = describeError(e);
      return null;
    } finally {
      _creating = false;
      notify();
    }
  }

  /// Deletes a conversation. Day log entries it saved stay.
  Future<bool> delete(String id) async {
    _actionError = null;
    try {
      await _service.deleteConversation(id);
      _conversations = [
        for (final c in _conversations)
          if (c.id != id) c,
      ];
      notify();
      return true;
    } catch (e) {
      _actionError = describeError(e);
      notify();
      return false;
    }
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated) return;
    if (!_hasLoaded && _status == null && _error == null) return;
    _status = null;
    _conversations = const [];
    _hasLoaded = false;
    _error = null;
    _actionError = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
