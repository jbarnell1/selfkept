import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
import '../utils/api_error.dart';
import 'assistant_chat_session.dart';

/// Tonight's check-in as a chat. Code on the server decides each question;
/// this keeps the turns, the topics and whether the person left.
class AssistantCheckinProvider extends ChatSession {
  static const List<QuickReply> moodReplies = [
    QuickReply('Great', 'great'),
    QuickReply('Good', 'good'),
    QuickReply('Okay', 'okay'),
    QuickReply('Meh', 'meh'),
    QuickReply('Low', 'low'),
    QuickReply('Very low', 'very low'),
  ];
  static const QuickReply noneReply = QuickReply('None today', 'none today');
  static const QuickReply skipReply = QuickReply('Skip', 'skip');

  String? _conversationId;
  CheckinStatus _status = CheckinStatus.notStarted;
  CheckinTopic? _topic;
  List<CheckinTopic> _done = const [];
  List<CheckinTopic> _skipped = const [];
  List<CheckinTopic> _remaining = const [];
  bool _redFlag = false;
  bool _busy = false;

  AssistantCheckinProvider({
    required super.service,
    required super.changes,
    super.onMoodChanged,
  });

  String? get conversationId => _conversationId;
  CheckinStatus get status => _status;

  /// The topic waiting for an answer.
  CheckinTopic? get topic => _topic;
  List<CheckinTopic> get done => _done;
  List<CheckinTopic> get skipped => _skipped;
  List<CheckinTopic> get remaining => _remaining;

  /// The last answer named a red flag and the check-in stopped.
  bool get redFlag => _redFlag;

  bool get isFinished => _status == CheckinStatus.done;

  /// Started, not finished, and nothing asked: the person left (or a red
  /// flag stopped it). "Pick up again" resumes.
  bool get isPaused =>
      hasLoaded && _status != CheckinStatus.done && _topic == null;

  @override
  bool get isBusy => _busy;

  @override
  bool get canSend => hasLoaded && !_busy && _topic != null;

  @override
  List<QuickReply> get quickReplies {
    final topic = _topic;
    if (topic == null) return const [];
    if (topic == CheckinTopic.mood) return const [...moodReplies, skipReply];
    return const [noneReply, skipReply];
  }

  @override
  Future<void> fetch() async {
    final today = await service.checkinToday();
    _conversationId = today.conversationId;
    _status = today.status;
    _topic = today.topic;
    _done = today.done;
    _skipped = today.skipped;
    _remaining = [
      for (final t in today.missing)
        if (!today.done.contains(t) && !today.skipped.contains(t)) t,
    ];
    items.clear();
    final id = today.conversationId;
    if (id != null) {
      final conversation = await service.conversation(id);
      items.addAll(conversation.messages.map(ChatItem.fromMessage));
    }
    // Not started, or left earlier: opening the check-in starts or resumes it.
    if (today.status != CheckinStatus.done && today.topic == null) {
      _apply(await service.checkin(), null);
    }
  }

  @override
  Future<bool> send(String text) async {
    final words = text.trim();
    if (words.isEmpty || !canSend) return false;
    final user = ChatItem(
      key: nextKey(),
      role: MessageRole.user,
      text: words,
      origin: MessageOrigin.code,
    );
    items.add(user);
    return _turn(() => service.checkin(answer: words), user);
  }

  /// Stops now; everything said so far is kept and the day is partial.
  Future<bool> leave() => _turn(() => service.checkin(leave: true), null);

  /// Picks the check-in up again with whatever is still missing.
  Future<bool> resume() => _turn(service.checkin, null);

  Future<bool> _turn(
    Future<CheckinTurn> Function() call,
    ChatItem? user,
  ) async {
    if (_busy) return false;
    _busy = true;
    sendError = null;
    notify();
    try {
      _apply(await call(), user);
      return true;
    } catch (e) {
      debugPrint('Check-in turn failed: $e');
      if (user != null) items.remove(user);
      sendError = describeError(e);
      return false;
    } finally {
      _busy = false;
      notify();
    }
  }

  void _apply(CheckinTurn turn, ChatItem? user) {
    _conversationId = turn.conversationId;
    _status = turn.status;
    _topic = turn.topic;
    _done = turn.done;
    _skipped = turn.skipped;
    _remaining = turn.remaining;
    _redFlag = turn.redFlag;
    final saved = <CaptureRef>[
      for (final e in turn.captured) CaptureRef.entry(e),
      if (turn.mood != null)
        CaptureRef(
          type: CaptureType.mood,
          id: '',
          kind: 'mood',
          text: moodLabel(turn.mood!),
        ),
    ];
    if (user != null) {
      user
        ..messageId = turn.userMessageId
        ..captures = saved;
    }
    if (turn.reply.isNotEmpty) {
      items.add(
        ChatItem(
          key: turn.assistantMessageId.isEmpty
              ? nextKey()
              : turn.assistantMessageId,
          role: MessageRole.assistant,
          text: turn.reply,
          origin: turn.redFlag ? MessageOrigin.guard : MessageOrigin.template,
          messageId: turn.assistantMessageId,
          safety: turn.redFlag,
        ),
      );
    }
    // Every turn moves the check-in on, so Today's card reloads.
    announce(saved);
  }

  /// The check-in scale, 1 to 6, in the words the question offers.
  static String moodLabel(int mood) => switch (mood) {
    <= 1 => 'Very low',
    2 => 'Low',
    3 => 'Meh',
    4 => 'Okay',
    5 => 'Good',
    _ => 'Great',
  };
}
