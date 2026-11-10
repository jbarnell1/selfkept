import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
import '../services/assistant_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'record_changes.dart';

/// Where a reply stands.
enum ChatItemState {
  /// Final: stored on the server.
  done,

  /// Arriving: text grows as sentences come in.
  streaming,

  /// The connection dropped part way. What arrived is kept.
  dropped,
}

/// One bubble in the chat view. Mutable: a streamed reply grows in place.
class ChatItem {
  /// Stable within the session, for widget keys.
  final String key;
  final MessageRole role;
  String text;
  MessageOrigin origin;

  /// The server's id. For a user message, Undo works on it.
  String? messageId;

  /// What this user message saved.
  List<CaptureRef> captures;
  bool capturesUndone;
  bool undoing;
  ChatItemState state;

  /// A red-flag reply written by code: shown prominently and in full.
  bool safety;

  ChatItem({
    required this.key,
    required this.role,
    this.text = '',
    this.origin = MessageOrigin.template,
    this.messageId,
    List<CaptureRef>? captures,
    this.capturesUndone = false,
    this.undoing = false,
    this.state = ChatItemState.done,
    this.safety = false,
  }) : captures = captures ?? [];

  bool get isUser => role == MessageRole.user;

  factory ChatItem.fromMessage(AssistantMessage m) => ChatItem(
    key: m.id,
    role: m.role,
    text: m.content,
    origin: m.origin,
    messageId: m.id,
    captures: [...m.captures],
    capturesUndone: m.capturesUndone,
    safety: m.role == MessageRole.assistant && m.redFlag,
  );
}

/// A one-tap answer: [label] on the chip, [text] sent.
class QuickReply {
  final String label;
  final String text;

  const QuickReply(this.label, this.text);
}

/// What the chat view needs, whether it shows a chat or the check-in.
abstract class ChatSession extends ChangeNotifier with SafeNotifier {
  final AssistantService service;
  final RecordChanges changes;

  /// Called after a capture or an undo touched the mood check-in, so the
  /// check-in list on Today reloads.
  final VoidCallback? onMoodChanged;

  ChatSession({
    required this.service,
    required this.changes,
    this.onMoodChanged,
  });

  final List<ChatItem> items = [];
  bool _loading = false;
  bool _hasLoaded = false;
  String? _error;
  String? _sendError;
  String? _undoError;
  int _keys = 0;

  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get error => _error;

  /// Why the last message did not go.
  String? get sendError => _sendError;
  String? get undoError => _undoError;

  /// True while a reply is on its way.
  bool get isBusy;

  /// True when the composer takes a message now.
  bool get canSend;

  List<QuickReply> get quickReplies => const [];

  @protected
  String nextKey() => 'local-${_keys++}';

  @protected
  set sendError(String? value) => _sendError = value;

  /// Loads the session. Subclasses fill [items] in [fetch].
  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      await fetch();
      _hasLoaded = true;
    } catch (e) {
      debugPrint('Conversation did not load: $e');
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  @protected
  Future<void> fetch();

  /// Sends [text]. False when it did not go; [sendError] says why.
  Future<bool> send(String text);

  /// Takes back what one user message saved.
  Future<bool> undo(ChatItem item) async {
    final id = item.messageId;
    if (id == null || item.undoing || item.capturesUndone) return false;
    item.undoing = true;
    _undoError = null;
    notify();
    try {
      await service.undo(id);
      item.capturesUndone = true;
      announce(item.captures);
      return true;
    } catch (e) {
      _undoError = describeError(e);
      return false;
    } finally {
      item.undoing = false;
      notify();
    }
  }

  /// Tells the rest of the app the record changed, as the day log does.
  @protected
  void announce(List<CaptureRef> captures) {
    changes.changed(this);
    if (captures.any((c) => c.type == CaptureType.mood)) onMoodChanged?.call();
  }

  void clearSendError() {
    if (_sendError == null) return;
    _sendError = null;
    notify();
  }
}
