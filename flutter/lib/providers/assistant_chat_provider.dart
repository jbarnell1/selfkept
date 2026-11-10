import 'dart:async';

import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
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

  @override
  Future<bool> send(String text) async {
    final words = text.trim();
    if (words.isEmpty || !canSend) return false;
    sendError = null;
    final user = ChatItem(
      key: nextKey(),
      role: MessageRole.user,
      text: words,
      origin: MessageOrigin.code,
    );
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
        .send(conversationId, words)
        .listen(
          (event) {
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
                  ..safety = isSafetyReply(event.origin, event.text);
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
              sendError = describeError(e);
              end(false);
              return;
            }
            reply.state = ChatItemState.dropped;
            end(true);
          },
          onDone: () {
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
    return send(user.text);
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
