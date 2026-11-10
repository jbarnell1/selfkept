import 'package:flutter/foundation.dart';

import '../models/date_only.dart';
import '../models/journal.dart';
import '../services/journal_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';
import 'record_changes.dart';

/// Today's journal entries and the tags offered as chips.
class JournalProvider extends ChangeNotifier with SafeNotifier {
  final JournalService _service;
  final AuthProvider _auth;
  final RecordChanges _changes;
  final DateTime Function() _now;

  List<JournalEntry> _entries = const [];
  List<JournalTagGroup> _tagGroups = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  bool _tagsLoaded = false;
  bool _saving = false;
  String? _error;
  String? _saveError;

  JournalProvider({
    required this._service,
    required this._auth,
    required this._changes,
    DateTime Function()? now,
  }) : _now = now ?? DateTime.now {
    _auth.addListener(_onAuthChanged);
    _changes.addListener(_onRecordChanged);
  }

  /// Today's entries, newest first.
  List<JournalEntry> get entries => _entries;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  bool get isSaving => _saving;
  String? get error => _error;

  /// Why the last save or delete failed.
  String? get saveError => _saveError;

  /// The chips for [kind]. Empty for notes and until the tags load.
  List<JournalTag> tagsFor(JournalKind kind) {
    for (final group in _tagGroups) {
      if (group.kind == kind) return group.tags;
    }
    return const [];
  }

  /// Every tag with its label, for the target form.
  List<JournalTag> get allTags => [
    for (final group in _tagGroups) ...group.tags,
  ];

  /// The label of a tag, falling back to the tag spelled out.
  String labelFor(String tag) {
    for (final group in _tagGroups) {
      for (final t in group.tags) {
        if (t.tag == tag) return t.label;
      }
    }
    return JournalTag.labelFor(tag);
  }

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      final today = localToday(_now());
      _entries = await _service.list(from: today, to: today);
      _hasLoaded = true;
    } catch (e) {
      debugPrint('Journal did not load: $e');
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
    if (!_tagsLoaded) await loadTags();
  }

  /// The chips rarely change, so they are fetched once per sign-in. A
  /// failure is quiet: the sheet still takes typed words.
  Future<void> loadTags() async {
    try {
      _tagGroups = await _service.tags();
      _tagsLoaded = true;
      notify();
    } catch (e) {
      debugPrint('Journal tags did not load: $e');
    }
  }

  /// Saves an entry and puts it at the top. Null when the server refused;
  /// [saveError] says why.
  Future<JournalEntry?> add(JournalDraft draft) async {
    _saving = true;
    _saveError = null;
    notify();
    try {
      final entry = await _service.create(draft);
      _entries = [entry, ..._entries];
      _changes.changed(this);
      return entry;
    } catch (e) {
      _saveError = describeError(e);
      return null;
    } finally {
      _saving = false;
      notify();
    }
  }

  /// Deletes an entry. False when the server refused.
  Future<bool> remove(String id) async {
    _saveError = null;
    try {
      await _service.delete(id);
      _entries = [
        for (final e in _entries)
          if (e.id != id) e,
      ];
      _changes.changed(this);
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

  /// The assistant saves entries from what the person says, so today's list
  /// reloads when someone else announces a change.
  void _onRecordChanged() {
    if (identical(_changes.origin, this) || !_hasLoaded || isDisposed) return;
    load();
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated) return;
    if (!_hasLoaded && !_tagsLoaded && _error == null) return;
    _entries = const [];
    _tagGroups = const [];
    _hasLoaded = false;
    _tagsLoaded = false;
    _error = null;
    _saveError = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    _changes.removeListener(_onRecordChanged);
    super.dispose();
  }
}
