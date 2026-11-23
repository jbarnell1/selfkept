import 'dart:async';

import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
import '../models/assistant_lookup.dart';
import '../models/journal.dart';
import '../models/photo.dart';
import '../services/assistant_service.dart';
import '../utils/api_error.dart';
import 'assistant_chat_session.dart';

/// One open chat. A reply streams in sentence by sentence; a replace swaps
/// what was shown; done makes it final. Disposing stops reading the stream.
class AssistantChatProvider extends ChatSession {
  /// The server's limit on one message.
  static const int maxLength = 2000;

  static const String readOnlyNote =
      'This is an evening check-in. It is kept here to read.';

  final String conversationId;

  ConversationSummary? _summary;
  StreamSubscription<AssistantStreamEvent>? _reply;
  Completer<bool>? _replyDone;

  AssistantChatProvider({
    required this.conversationId,
    required super.service,
    required super.changes,
    super.onMoodChanged,
  });

  ConversationSummary? get summary => _summary;
  String get title => _summary?.title ?? '';

  /// A check-in opened from the list reads here; it is answered on its own
  /// page.
  bool get readOnly => _summary?.kind == ConversationKind.checkin;

  @override
  bool get isBusy => _reply != null;

  @override
  bool get canSend => hasLoaded && !readOnly && !isBusy;

  @override
  Future<void> fetch() async {
    final conversation = await service.conversation(conversationId);
    _summary = conversation.summary;
    items
      ..clear()
      ..addAll(conversation.messages.map(ChatItem.fromMessage));
  }

  /// The `other` photo card waiting for a question, if any: the next
  /// message goes with what it shows, and may then be empty.
  ChatItem? get pendingPhoto {
    for (final item in items.reversed) {
      final photo = item.photo;
      if (photo != null &&
          photo.stage == PhotoStage.ready &&
          photo.shownKind == PhotoKind.other) {
        return item;
      }
    }
    return null;
  }

  /// True while the home model is looking at a photo. One at a time.
  bool get photoReading =>
      items.any((i) => i.photo?.stage == PhotoStage.reading);

  @override
  Future<bool> send(String text) {
    final pending = pendingPhoto;
    final result = pending?.photo?.result;
    final note = result == null
        ? null
        : PhotoNote(kind: PhotoKind.other, description: result.description);
    return _send(text.trim(), photo: note, photoItem: pending);
  }

  Future<bool> _send(
    String words, {
    PhotoNote? photo,
    ChatItem? photoItem,
  }) async {
    if ((words.isEmpty && photo == null) || !canSend) return false;
    sendError = null;
    final user = ChatItem(
      key: nextKey(),
      role: MessageRole.user,
      text: words,
      origin: MessageOrigin.code,
      sentPhoto: photo,
    );
    // The card gives way to the message; it comes back if the server
    // refuses before saving anything.
    final photoAt = photoItem == null ? -1 : items.indexOf(photoItem);
    if (photoAt >= 0) items.removeAt(photoAt);
    final reply = ChatItem(
      key: nextKey(),
      role: MessageRole.assistant,
      state: ChatItemState.streaming,
    );
    items
      ..add(user)
      ..add(reply);
    final done = Completer<bool>();
    _replyDone = done;
    var started = false;
    var finished = false;

    void end(bool ok) {
      _reply = null;
      _replyDone = null;
      if (!done.isCompleted) done.complete(ok);
      notify();
    }

    _reply = service
        .send(conversationId, words, photo: photo)
        .listen(
          (event) {
            if (!started) photoItem?.photo?.bytes = null;
            started = true;
            switch (event) {
              case CaptureEvent():
                user.messageId = event.messageId;
                user.captures = event.refs;
                if (user.captures.isNotEmpty) announce(user.captures);
              case DeltaEvent():
                reply.text += event.text;
              case ReplaceEvent():
                reply.text = event.text;
              case DoneEvent():
                finished = true;
                reply
                  ..text = event.text
                  ..origin = event.origin
                  ..messageId = event.messageId
                  ..state = ChatItemState.done
                  ..safety = event.redFlag
                  ..lookupOffered = event.lookupOffered && !event.redFlag
                  ..questionMessageId = event.userMessageId;
                user.messageId ??= event.userMessageId;
            }
            notify();
          },
          onError: (Object e) {
            debugPrint('Reply stream stopped: $e');
            final refused = e is ApiException && !e.isNetwork;
            if (!started && refused) {
              // The server said no before saving anything.
              items
                ..remove(user)
                ..remove(reply);
              if (photoAt >= 0) {
                items.insert(photoAt.clamp(0, items.length), photoItem!);
              }
              sendError = describeError(e);
              end(false);
              return;
            }
            photoItem?.photo?.bytes = null;
            reply.state = ChatItemState.dropped;
            end(true);
          },
          onDone: () {
            photoItem?.photo?.bytes = null;
            if (!finished) reply.state = ChatItemState.dropped;
            end(true);
          },
          cancelOnError: true,
        );
    notify();
    return done.future;
  }

  /// Sends the message of a dropped reply again. What the first attempt saved
  /// is taken back first, so the day log does not hold it twice.
  Future<bool> retry(ChatItem reply) async {
    final at = items.indexOf(reply);
    if (at < 1 || reply.state != ChatItemState.dropped || isBusy) return false;
    final user = items[at - 1];
    if (!user.isUser) return false;
    if (user.captures.isNotEmpty && !user.capturesUndone) {
      final ok = await undo(user);
      if (!ok) return false;
    }
    items
      ..remove(reply)
      ..remove(user);
    notify();
    return _send(user.text, photo: user.sentPhoto);
  }

  /// Puts a picked photo in the thread and asks the home model to read it.
  Future<void> readPhoto(Uint8List bytes, {PhotoKind? kind}) async {
    if (photoReading) return;
    final item = ChatItem(
      key: nextKey(),
      role: MessageRole.user,
      photo: PhotoRead(bytes: bytes, asked: kind),
    );
    items.add(item);
    await _read(item);
  }

  /// "Not right?": the same picture, read again as [kind].
  Future<void> rereadPhoto(ChatItem item, PhotoKind kind) async {
    final photo = item.photo;
    if (photo == null || photo.bytes == null) return;
    photo.asked = kind;
    await _read(item);
  }

  /// "Try again" after a failed read, with the kind asked last time.
  Future<void> retryPhoto(ChatItem item) => _read(item);

  Future<void> _read(ChatItem item) async {
    final photo = item.photo;
    final bytes = photo?.bytes;
    if (photo == null || bytes == null) return;
    final attempt = ++photo.attempt;
    photo
      ..stage = PhotoStage.reading
      ..error = null
      ..saveError = null;
    notify();
    if (bytes.length > AssistantService.maxPhotoBytes) {
      // The server refuses it anyway; saying so here spares a long upload.
      photo
        ..error = 'The photo is larger than 6 MB.'
        ..stage = PhotoStage.failed;
      notify();
      return;
    }
    try {
      final result = await service.analyzePhoto(bytes, kind: photo.asked);
      if (attempt != photo.attempt || photo.bytes == null) return;
      photo
        ..result = result
        ..stage = PhotoStage.ready;
    } catch (e) {
      if (attempt != photo.attempt || photo.bytes == null) return;
      debugPrint('The photo was not read: $e');
      photo
        ..error = describeError(e)
        ..stage = PhotoStage.failed;
    }
    notify();
  }

  /// Discard: the card goes and the picture leaves memory.
  void discardPhoto(ChatItem item) {
    final photo = item.photo;
    if (photo == null) return;
    photo
      ..bytes = null
      ..attempt += 1;
    items.remove(item);
    notify();
  }

  /// Marks a save as on its way, or clears it with the server's reason.
  void photoSaving(ChatItem item, {required bool saving, String? error}) {
    final photo = item.photo;
    if (photo == null) return;
    photo
      ..saving = saving
      ..saveError = error;
    notify();
  }

  /// The proposal was saved. The card turns into [line]; [entryId], for a
  /// day log entry, is what Undo removes.
  void photoSaved(
    ChatItem item, {
    required String line,
    JournalKind? kind,
    String? entryId,
  }) {
    final photo = item.photo;
    if (photo == null) return;
    photo
      ..bytes = null
      ..saving = false
      ..saveError = null
      ..stage = PhotoStage.saved
      ..savedLine = line
      ..savedKind = kind
      ..savedEntryId = entryId;
    notify();
  }

  /// Undo on a saved photo: [remove] deletes the entry; true when it did.
  Future<bool> undoPhoto(
    ChatItem item,
    Future<bool> Function(String entryId) remove, {
    String? Function()? why,
  }) async {
    final photo = item.photo;
    final id = photo?.savedEntryId;
    if (photo == null || id == null || photo.undoing || photo.undone) {
      return false;
    }
    photo
      ..undoing = true
      ..undoError = null;
    notify();
    final ok = await remove(id);
    photo
      ..undoing = false
      ..undone = ok
      ..undoError = ok ? null : why?.call();
    notify();
    return ok;
  }

  /// Adds a cloud answer the lookup sheet brought back, and the home
  /// model's note on it when there is one. [from] is the reply whose offer
  /// was used; its offer goes away so the same question is not sent twice by
  /// accident.
  void addLookupAnswer(
    LookupDoneEvent done, {
    ChatItem? from,
    String? provider,
  }) {
    from?.lookupOffered = false;
    items.add(
      ChatItem.fromMessage(done.message)
        ..provider = provider
        ..lookupId ??= done.lookupId.isEmpty ? null : done.lookupId,
    );
    final applied = done.applied;
    if (applied != null) items.add(ChatItem.fromMessage(applied));
    notify();
  }

  @override
  void dispose() {
    _reply?.cancel();
    _reply = null;
    final done = _replyDone;
    if (done != null && !done.isCompleted) done.complete(false);
    super.dispose();
  }
}
