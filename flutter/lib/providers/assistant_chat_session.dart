import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
import '../models/journal.dart';
import '../models/photo.dart';
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

/// Where a photo card stands.
enum PhotoStage {
  /// The home model is looking at the picture.
  reading,

  /// The proposal is on screen.
  ready,

  /// The read failed; [PhotoRead.error] says why.
  failed,

  /// Saved (or handed to the form that saved it). The picture is gone.
  saved,
}

/// One photo in the thread. The app holds the picture only while the card
/// needs it: saving, discarding or sending drops [bytes].
class PhotoRead {
  Uint8List? bytes;

  /// The kind asked for on the last read; null lets the server sort.
  PhotoKind? asked;
  PhotoStage stage;
  PhotoAnalysis? result;
  String? error;

  /// Counts reads, so an answer to an older read is ignored.
  int attempt = 0;

  /// Set while a save is on its way.
  bool saving = false;
  String? saveError;

  /// After a save: the line that stays, and the day log entry Undo removes
  /// (null for what Undo does not apply to).
  String savedLine = '';
  JournalKind? savedKind;
  String? savedEntryId;
  bool undoing = false;
  bool undone = false;
  String? undoError;

  PhotoRead({required this.bytes, this.asked, this.stage = PhotoStage.reading});

  /// The kind the card shows now.
  PhotoKind? get shownKind => result?.shownKind;
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

  /// For a reply: the server offered a cloud lookup of the question it
  /// answered. Only a live reply carries this.
  bool lookupOffered;

  /// For a reply: the user message it answered, which a lookup drafts from.
  String? questionMessageId;

  /// For a cloud answer: its sources and the lookup it came from (null once
  /// that lookup was deleted).
  List<LookupSource> sources;
  String? lookupId;

  /// For a cloud answer sent in this session: the provider's name from the
  /// draft.
  String? provider;

  /// A photo card instead of a bubble.
  PhotoRead? photo;

  /// For a user message sent in this session: what an `other` photo shows,
  /// sent with it.
  PhotoNote? sentPhoto;

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
    this.lookupOffered = false,
    this.questionMessageId,
    this.sources = const [],
    this.lookupId,
    this.provider,
    this.photo,
    this.sentPhoto,
  }) : captures = captures ?? [];

  /// What a photo message stands for when the person typed nothing; the
  /// server stores the same words.
  static const String photoQuestion = 'What is in this photo?';

  bool get isUser => role == MessageRole.user;

  bool get isPhoto => photo != null;

  /// The words in the bubble. A message sent with a photo reads as the
  /// server stores it: the question, then a line with what the photo shows.
  String get shownText {
    final sent = sentPhoto;
    if (sent == null) return text;
    final question = text.trim().isEmpty ? photoQuestion : text;
    return '$question\nPhoto: ${sent.description}';
  }

  /// An answer from a cloud model, set apart from local replies.
  bool get isCloud => !isUser && origin == MessageOrigin.cloud;

  factory ChatItem.fromMessage(AssistantMessage m) => ChatItem(
    key: m.id,
    role: m.role,
    text: m.content,
    origin: m.origin,
    messageId: m.id,
    captures: [...m.captures],
    capturesUndone: m.capturesUndone,
    safety: m.role == MessageRole.assistant && m.redFlag,
    sources: [...m.sources],
    lookupId: m.lookupId,
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
