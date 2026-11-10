import '../models/context.dart';
import '../models/date_only.dart';
import '../services/context_service.dart';
import '../utils/api_error.dart';
import 'person_read.dart';

/// "What is going on": the context that counts today and the events of the
/// last 30 days. Writes are your own, so they are refused while someone
/// else's record is shown.
class ContextProvider extends PersonRead<ContextList> {
  final ContextService _service;

  bool _saving = false;
  String? _actionError;

  ContextProvider({required this._service, super.auth, super.viewing});

  @override
  Future<ContextList> fetch(String? personId) =>
      _service.list(personId: personId);

  bool get isSaving => _saving;

  /// Why the last write failed, in the server's words.
  String? get actionError => _actionError;

  void clearActionError() {
    if (_actionError == null) return;
    _actionError = null;
    notify();
  }

  /// Saves a new event. Null when refused.
  Future<ContextEvent?> add(ContextDraft draft) =>
      _act(() => _service.create(draft));

  /// Changes only what [changes] names.
  Future<ContextEvent?> update(String id, Map<String, dynamic> changes) =>
      _act(() => _service.update(id, changes));

  /// Ends an open event today, the person's today as the server counts it.
  Future<ContextEvent?> end(ContextEvent event) {
    final today = data?.day ?? localToday();
    return update(event.id, {'endedOn': dateOnly(today)});
  }

  /// Opens an ended event again.
  Future<ContextEvent?> reopen(String id) => update(id, {'reopen': true});

  Future<bool> remove(String id) async =>
      await _act<bool>(() async {
        await _service.delete(id);
        return true;
      }) ??
      false;

  Future<T?> _act<T>(Future<T> Function() action) async {
    if (_saving || !canEdit) return null;
    _saving = true;
    _actionError = null;
    notify();
    final T result;
    try {
      result = await action();
    } catch (e) {
      _actionError = describeError(e);
      _saving = false;
      notify();
      return null;
    }
    await reloadQuietly();
    _saving = false;
    notify();
    return result;
  }

  @override
  void clear() {
    _actionError = null;
  }
}
