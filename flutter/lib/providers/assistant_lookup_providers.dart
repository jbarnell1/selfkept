import 'dart:async';

import 'package:flutter/foundation.dart';

import '../models/assistant_lookup.dart';
import '../services/assistant_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// The cloud lookup's settings and the person's list of what was sent. App
/// wide, so a chat can name the provider and open "What was sent". Nothing
/// here asks the server until a page that exists only with the feature asks
/// it to.
class AssistantLookupProvider extends ChangeNotifier with SafeNotifier {
  final AssistantService _service;
  final AuthProvider _auth;

  LookupStatus? _status;
  bool _loadingStatus = false;
  bool _statusRequested = false;
  String? _statusError;
  LookupRefused? _statusRefusal;
  bool _saving = false;
  String? _saveError;

  List<LookupRecord> _history = const [];
  bool _loadingHistory = false;
  bool _historyLoaded = false;
  String? _historyError;
  String? _deleteError;

  AssistantLookupProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  LookupStatus? get status => _status;
  bool get isLoadingStatus => _loadingStatus;
  String? get statusError => _statusError;

  /// Set when the server answered the settings route with a refusal (it has
  /// no lookup after all).
  LookupRefused? get statusRefusal => _statusRefusal;
  bool get isSaving => _saving;
  String? get saveError => _saveError;

  List<LookupRecord> get history => _history;
  bool get isLoadingHistory => _loadingHistory;
  bool get historyLoaded => _historyLoaded;
  String? get historyError => _historyError;
  String? get deleteError => _deleteError;

  /// The provider's name from the server, or null before it answered.
  String? get providerName {
    final name = _status?.provider.trim() ?? '';
    return name.isEmpty ? null : name;
  }

  Future<void> loadStatus() async {
    _statusRequested = true;
    _loadingStatus = true;
    _statusError = null;
    _statusRefusal = null;
    notify();
    try {
      _status = await _service.lookupStatus();
    } on LookupRefused catch (e) {
      _statusRefusal = e;
    } catch (e) {
      debugPrint('Lookup settings did not load: $e');
      _statusError = describeError(e);
    } finally {
      _loadingStatus = false;
      notify();
    }
  }

  /// Loads the status once, for a chat that shows a cloud answer.
  Future<void> ensureStatus() async {
    if (_statusRequested) return;
    await loadStatus();
  }

  /// Turns the person's switch on or off. On needs [acknowledged]. True when
  /// the server took it; [saveError] says why not.
  Future<bool> setEnabled(bool enabled, {bool acknowledged = false}) async {
    _saving = true;
    _saveError = null;
    notify();
    try {
      _status = await _service.saveLookup(
        enabled: enabled,
        acknowledged: acknowledged,
      );
      return true;
    } on LookupRefused catch (e) {
      _saveError = switch (e.kind) {
        LookupRefusalKind.notAcknowledged => acknowledgeFirst,
        LookupRefusalKind.serverOff => serverOffLine,
        _ => e.message,
      };
      return false;
    } catch (e) {
      _saveError = describeError(e);
      return false;
    } finally {
      _saving = false;
      notify();
    }
  }

  static const String acknowledgeFirst =
      'Tick the box first to say you understand what leaves this server.';
  static const String serverOffLine =
      'This server does not offer cloud lookup right now.';
  static const String alreadyGone = 'That one was already deleted.';

  Future<void> loadHistory() async {
    _loadingHistory = true;
    _historyError = null;
    notify();
    try {
      _history = await _service.lookups();
      _historyLoaded = true;
    } catch (e) {
      debugPrint('Sent lookups did not load: $e');
      _historyError =
          e is LookupRefused && e.kind == LookupRefusalKind.serverOff
          ? serverOffLine
          : describeError(e);
    } finally {
      _loadingHistory = false;
      notify();
    }
  }

  /// The record of what was sent for [lookupId], fresh from the server. Null
  /// when it was deleted.
  Future<LookupRecord?> findSent(String lookupId) async {
    final list = await _service.lookups();
    _history = list;
    _historyLoaded = true;
    notify();
    for (final r in list) {
      if (r.id == lookupId) return r;
    }
    return null;
  }

  /// Deletes one record. One already gone counts as deleted.
  Future<bool> delete(String id) async {
    _deleteError = null;
    try {
      await _service.deleteLookup(id);
    } on LookupRefused catch (e) {
      if (e.kind != LookupRefusalKind.missing) {
        _deleteError = e.message;
        notify();
        return false;
      }
    } catch (e) {
      _deleteError = describeError(e);
      notify();
      return false;
    }
    _history = [
      for (final r in _history)
        if (r.id != id) r,
    ];
    notify();
    return true;
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated) return;
    _status = null;
    _statusRequested = false;
    _statusError = null;
    _statusRefusal = null;
    _saveError = null;
    _history = const [];
    _historyLoaded = false;
    _historyError = null;
    _deleteError = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}

/// Where the lookup sheet stands.
enum LookupPhase {
  /// The first draft is on its way.
  drafting,

  /// The text is on screen to read, edit and send.
  ready,

  /// Send was pressed; the server has not said it went yet.
  sending,

  /// It went; the provider is working.
  waiting,

  /// The answer came back. The sheet closes.
  done,

  /// No draft could be made ([refusal] or [draftError] says why).
  unavailable,
}

/// The lookup sheet: a draft, a mode switch, the person's edits, one send.
/// Lives as long as the sheet, so closing it stops reading the answer.
class LookupSheetProvider extends ChangeNotifier with SafeNotifier {
  /// The server's limit on the outgoing text.
  static const int maxLength = 4000;

  final AssistantService _service;

  /// The user message the lookup is about.
  final String messageId;

  LookupPhase _phase = LookupPhase.drafting;
  LookupMode _mode = LookupMode.anonymized;
  LookupDraft? _draft;
  String _text = '';
  int _draftVersion = 0;
  String? _provider;
  bool _switching = false;
  LookupRefused? _refusal;
  String? _draftError;
  List<LookupChange> _findings = const [];
  String? _failure;
  LookupFailure? _failureReason;
  LookupDoneEvent? _result;
  StreamSubscription<LookupStreamEvent>? _send;
  Completer<LookupDoneEvent?>? _sendDone;
  int _drafts = 0;

  LookupSheetProvider({
    required this._service,
    required this.messageId,
    this._provider,
  });

  LookupPhase get phase => _phase;
  LookupMode get mode => _mode;
  LookupDraft? get draft => _draft;

  /// The text as it stands now, edits included. This is what Send sends.
  String get text => _text;

  /// Goes up each time a new draft replaces the text, so the field can
  /// refill.
  int get draftVersion => _draftVersion;

  /// The provider's name from the server, once a draft has said it.
  String? get provider => _provider;
  bool get isSwitching => _switching;

  /// Why there is no draft at all.
  LookupRefused? get refusal => _refusal;

  /// Why the last draft did not come (a mode switch keeps the old one).
  String? get draftError => _draftError;

  /// What a refused send still held.
  List<LookupChange> get findings => _findings;

  /// The server's words when the last send came back without an answer.
  String? get failure => _failure;
  LookupFailure? get failureReason => _failureReason;
  LookupDoneEvent? get result => _result;

  bool get isBusy =>
      _phase == LookupPhase.sending || _phase == LookupPhase.waiting;

  bool get canEdit => _phase == LookupPhase.ready && !_switching;

  bool get canSend =>
      canEdit && _text.trim().isNotEmpty && _text.length <= maxLength;

  Future<void> start() => _loadDraft(_mode, first: true);

  /// Switches the mode and fetches the draft for it. The old text stays if
  /// the new draft does not come.
  Future<void> setMode(LookupMode mode) async {
    if (mode == _mode || isBusy || _switching) return;
    if (_phase != LookupPhase.ready) return;
    await _loadDraft(mode, first: false);
  }

  Future<void> _loadDraft(LookupMode mode, {required bool first}) async {
    final ticket = ++_drafts;
    _draftError = null;
    _findings = const [];
    _failure = null;
    _failureReason = null;
    if (!first) _switching = true;
    notify();
    try {
      final draft = await _service.lookupDraft(messageId, mode);
      if (ticket != _drafts) return;
      _draft = draft;
      _mode = mode;
      _text = draft.text;
      _draftVersion += 1;
      if (draft.provider.trim().isNotEmpty) _provider = draft.provider.trim();
      _phase = LookupPhase.ready;
    } on LookupRefused catch (e) {
      if (ticket != _drafts) return;
      if (first) {
        _refusal = e;
        _phase = LookupPhase.unavailable;
      } else {
        _draftError = refusalLine(e);
      }
    } catch (e) {
      if (ticket != _drafts) return;
      debugPrint('Lookup draft did not load: $e');
      _draftError = describeError(e);
      if (first) _phase = LookupPhase.unavailable;
    } finally {
      if (ticket == _drafts) {
        _switching = false;
        notify();
      }
    }
  }

  /// Tries the first draft again after it failed.
  Future<void> retryDraft() async {
    if (_phase != LookupPhase.unavailable) return;
    _refusal = null;
    _phase = LookupPhase.drafting;
    await _loadDraft(_mode, first: true);
  }

  /// The person changed the text.
  void edit(String text) {
    if (!canEdit || text == _text) return;
    _text = text;
    if (_findings.isNotEmpty) _findings = const [];
    notify();
  }

  /// Sends the text as shown, once. Completes with the answer, or null when
  /// it did not come; [failure], [findings] or [draftError] say why, and the
  /// person may send again, which is a new send.
  Future<LookupDoneEvent?> send() {
    if (!canSend) return Future.value();
    _phase = LookupPhase.sending;
    _findings = const [];
    _failure = null;
    _failureReason = null;
    _draftError = null;
    final done = Completer<LookupDoneEvent?>();
    _sendDone = done;
    var finished = false;

    void end([LookupDoneEvent? result]) {
      _send = null;
      _sendDone = null;
      if (!done.isCompleted) done.complete(result);
      notify();
    }

    _send = _service
        .sendLookup(messageId: messageId, mode: _mode, text: _text)
        .listen(
          (event) {
            switch (event) {
              case LookupSentEvent():
              case LookupWaitingEvent():
                if (_phase == LookupPhase.sending) {
                  _phase = LookupPhase.waiting;
                }
              case LookupDoneEvent():
                finished = true;
                _result = event;
                _phase = LookupPhase.done;
                end(event);
                return;
              case LookupFailedEvent():
                finished = true;
                _failure = event.text.trim().isEmpty
                    ? droppedLine
                    : event.text.trim();
                _failureReason = event.reason;
                _phase = LookupPhase.ready;
                end();
                return;
            }
            notify();
          },
          onError: (Object e) {
            finished = true;
            debugPrint('Lookup stopped: $e');
            _phase = LookupPhase.ready;
            if (e is LookupRefused) {
              if (e.kind == LookupRefusalKind.findings) {
                _findings = e.findings;
              } else {
                _draftError = refusalLine(e);
              }
            } else {
              _failure = e is ApiException && e.isNetwork
                  ? droppedLine
                  : describeError(e);
            }
            end();
          },
          onDone: () {
            if (finished) return;
            _failure = droppedLine;
            _phase = LookupPhase.ready;
            end();
          },
          cancelOnError: true,
        );
    notify();
    return done.future;
  }

  static const String droppedLine =
      'The connection dropped before an answer arrived. The question may '
      'still have gone; Sent to the cloud lists every one that did.';

  /// What the sheet says for a refusal that is not about the text.
  static String refusalLine(LookupRefused e) => switch (e.kind) {
    LookupRefusalKind.serverOff =>
      'This server does not offer cloud lookup right now.',
    LookupRefusalKind.personOff =>
      'Cloud lookup is off for you. Turn it on under You, then Cloud lookup.',
    LookupRefusalKind.redFlag =>
      'This message reads as something that needs care, so it is never '
          'sent to a cloud model.',
    // The sheet always acknowledges, so a 422 here is the server's check on
    // the edited text (it reads as a red flag); its own words say which.
    LookupRefusalKind.notAcknowledged =>
      e.message.trim().isNotEmpty
          ? e.message.trim()
          : 'The server did not take the send. Try again.',
    LookupRefusalKind.findings => e.message,
    LookupRefusalKind.missing => e.message,
  };

  @override
  void dispose() {
    _send?.cancel();
    _send = null;
    final done = _sendDone;
    if (done != null && !done.isCompleted) done.complete(null);
    super.dispose();
  }
}
