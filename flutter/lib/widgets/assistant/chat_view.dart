import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/assistant.dart';
import '../../models/journal.dart';
import '../../providers/assistant_chat_provider.dart';
import '../../providers/assistant_chat_session.dart';
import '../../providers/journal_provider.dart';
import '../../theme/tokens.dart';
import '../day_log.dart' show journalKindIcon;
import '../status_views.dart';
import 'lookup_widgets.dart';

/// The words on a saved-item chip: "Food: High protein, Vegetables",
/// "Remembered: Eats a vegetarian diet", "Mood: Good".
String captureLabel(CaptureRef c, String Function(String tag) tagLabel) {
  switch (c.type) {
    case CaptureType.journal:
      final kind = JournalKind.fromString(c.kind).displayName;
      final text = c.text?.trim() ?? '';
      if (c.tags.isNotEmpty) return '$kind: ${c.tags.map(tagLabel).join(', ')}';
      return text.isEmpty ? kind : '$kind: $text';
    case CaptureType.memory:
      final text = c.text?.trim() ?? '';
      final what = text.isNotEmpty
          ? text
          : c.tags.map(JournalTag.labelFor).join(', ');
      return what.isEmpty ? 'Remembered' : 'Remembered: $what';
    case CaptureType.mood:
      final text = c.text?.trim() ?? '';
      return text.isEmpty ? 'Mood' : 'Mood: $text';
  }
}

IconData _captureIcon(CaptureRef c) => switch (c.type) {
  CaptureType.journal => journalKindIcon(JournalKind.fromString(c.kind)),
  CaptureType.memory => Icons.bookmark_outline,
  CaptureType.mood => Icons.mood_outlined,
};

/// The conversation: bubbles from the top, newest at the bottom, with the
/// bottom held in place while a reply grows. [footer] is the composer or
/// whatever the session offers instead.
class ChatView<T extends ChatSession> extends StatelessWidget {
  final Widget? header;
  final Widget footer;
  final String emptyLine;

  const ChatView({
    super.key,
    this.header,
    required this.footer,
    this.emptyLine = 'Say how the day is going.',
  });

  static const String loadErrorTitle = 'This conversation did not load.';

  @override
  Widget build(BuildContext context) {
    final session = context.watch<T>();
    final theme = Theme.of(context);
    if (!session.hasLoaded) {
      if (session.error != null) {
        return ErrorView(
          title: loadErrorTitle,
          detail: session.error,
          onRetry: session.load,
        );
      }
      return const LoadingView();
    }
    final items = session.items.reversed.toList();
    return Align(
      alignment: Alignment.topCenter,
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: Space.maxContentWidth),
        child: Column(
          children: [
            Expanded(
              child: ListView(
                reverse: true,
                padding: const EdgeInsets.fromLTRB(
                  Space.x4,
                  Space.x4,
                  Space.x4,
                  Space.x4,
                ),
                children: [
                  for (final item in items)
                    Padding(
                      key: ValueKey(item.key),
                      padding: const EdgeInsets.only(bottom: Space.x3),
                      child: MessageBubble<T>(item: item),
                    ),
                  if (items.isEmpty)
                    Padding(
                      padding: const EdgeInsets.all(Space.x6),
                      child: Text(
                        emptyLine,
                        textAlign: TextAlign.center,
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: theme.colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ),
                  ?header,
                ],
              ),
            ),
            footer,
          ],
        ),
      ),
    );
  }
}

/// One message. A reply that code wrote for a red flag gets the terracotta
/// edge and is never shortened.
class MessageBubble<T extends ChatSession> extends StatelessWidget {
  final ChatItem item;

  const MessageBubble({super.key, required this.item});

  static const String thinking = 'Thinking';
  static const String droppedLine =
      'The reply was cut off. What arrived is kept.';
  static const String retryLabel = 'Try again';
  static const String safetyLabel = 'Getting help';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final width = MediaQuery.sizeOf(context).width;
    final maxWidth = (width * 0.85).clamp(240.0, 560.0);

    if (item.isUser) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          ConstrainedBox(
            constraints: BoxConstraints(maxWidth: maxWidth),
            child: DecoratedBox(
              decoration: BoxDecoration(
                color: theme.colorScheme.primaryContainer,
                borderRadius: Radii.cardRadius,
              ),
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: Space.x4,
                  vertical: Space.x3,
                ),
                child: Text(item.text, style: theme.textTheme.bodyMedium),
              ),
            ),
          ),
          if (item.captures.isNotEmpty) CaptureChips<T>(item: item),
        ],
      );
    }

    if (item.safety) {
      return Semantics(
        container: true,
        label: safetyLabel,
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: colors.clinician.withValues(alpha: 0.10),
            borderRadius: Radii.cardRadius,
            border: Border.all(color: colors.clinician, width: 1.5),
          ),
          child: Padding(
            padding: const EdgeInsets.all(Space.x4),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Icon(
                      Icons.health_and_safety_outlined,
                      color: colors.clinician,
                      size: 20,
                    ),
                    const SizedBox(width: Space.x2),
                    Text(
                      safetyLabel,
                      style: theme.textTheme.titleSmall?.copyWith(
                        color: colors.clinician,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: Space.x2),
                SelectableText(item.text, style: theme.textTheme.bodyLarge),
              ],
            ),
          ),
        ),
      );
    }

    if (item.isCloud) return CloudAnswerBubble(item: item);

    final waiting = item.state == ChatItemState.streaming && item.text.isEmpty;
    return Align(
      alignment: Alignment.centerLeft,
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: maxWidth),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            DecoratedBox(
              decoration: BoxDecoration(
                color: colors.surfaceRaised,
                borderRadius: Radii.cardRadius,
              ),
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: Space.x4,
                  vertical: Space.x3,
                ),
                child: waiting
                    ? Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const SizedBox(
                            width: 14,
                            height: 14,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          ),
                          const SizedBox(width: Space.x2),
                          Text(thinking, style: theme.textTheme.bodySmall),
                        ],
                      )
                    : Text(item.text, style: theme.textTheme.bodyMedium),
              ),
            ),
            if (item.state == ChatItemState.dropped) _Dropped<T>(item: item),
            if (item.lookupOffered) LookupOfferLine<T>(item: item),
          ],
        ),
      ),
    );
  }
}

class _Dropped<T extends ChatSession> extends StatelessWidget {
  final ChatItem item;

  const _Dropped({required this.item});

  @override
  Widget build(BuildContext context) {
    final session = context.read<T>();
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: Space.x2),
      child: Wrap(
        crossAxisAlignment: WrapCrossAlignment.center,
        spacing: Space.x2,
        children: [
          Text(
            MessageBubble.droppedLine,
            style: theme.textTheme.bodySmall?.copyWith(
              color: theme.colorScheme.error,
            ),
          ),
          if (session is AssistantChatProvider)
            TextButton(
              onPressed: session.isBusy ? null : () => session.retry(item),
              child: const Text(MessageBubble.retryLabel),
            ),
        ],
      ),
    );
  }
}

/// What a message saved, with Undo.
class CaptureChips<T extends ChatSession> extends StatelessWidget {
  final ChatItem item;

  const CaptureChips({super.key, required this.item});

  static const String savedLabel = 'Saved';
  static const String undoLabel = 'Undo';
  static const String undoingLine = 'Taking it back out';
  static const String undoneLine = 'Taken back out.';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final journal = context.watch<JournalProvider>();
    if (item.capturesUndone) {
      return Padding(
        padding: const EdgeInsets.only(top: Space.x2),
        child: Text(undoneLine, style: theme.textTheme.bodySmall),
      );
    }
    final session = context.read<T>();
    return Padding(
      padding: const EdgeInsets.only(top: Space.x2),
      child: Wrap(
        alignment: WrapAlignment.end,
        crossAxisAlignment: WrapCrossAlignment.center,
        spacing: Space.x2,
        runSpacing: Space.x2,
        children: [
          Text(savedLabel, style: theme.textTheme.bodySmall),
          for (final c in item.captures)
            ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 280),
              child: Chip(
                avatar: Icon(_captureIcon(c), size: 16),
                label: Text(
                  captureLabel(c, journal.labelFor),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                visualDensity: VisualDensity.compact,
              ),
            ),
          if (item.undoing)
            Text(undoingLine, style: theme.textTheme.bodySmall)
          else if (item.messageId != null)
            TextButton(
              onPressed: () => session.undo(item),
              child: const Text(undoLabel),
            ),
        ],
      ),
    );
  }
}

/// The message field, its send button and any one-tap answers above it.
class ChatComposer extends StatefulWidget {
  final bool enabled;
  final Future<bool> Function(String text) onSend;
  final List<QuickReply> quickReplies;
  final String hint;
  final String? error;
  final Widget? trailing;

  const ChatComposer({
    super.key,
    required this.enabled,
    required this.onSend,
    this.quickReplies = const [],
    this.hint = 'Message',
    this.error,
    this.trailing,
  });

  static const String sendTooltip = 'Send';

  @override
  State<ChatComposer> createState() => _ChatComposerState();
}

class _ChatComposerState extends State<ChatComposer> {
  final TextEditingController _controller = TextEditingController();

  Future<void> _send([String? quick]) async {
    if (!widget.enabled) return;
    final text = quick ?? _controller.text;
    if (text.trim().isEmpty) return;
    if (quick == null) _controller.clear();
    final ok = await widget.onSend(text);
    if (!ok && quick == null && mounted && _controller.text.isEmpty) {
      _controller.text = text;
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: colors.hairline)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
            Space.x4,
            Space.x3,
            Space.x4,
            Space.x3,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (widget.quickReplies.isNotEmpty) ...[
                Wrap(
                  spacing: Space.x2,
                  runSpacing: Space.x2,
                  children: [
                    for (final reply in widget.quickReplies)
                      ActionChip(
                        label: Text(reply.label),
                        onPressed: widget.enabled
                            ? () => _send(reply.text)
                            : null,
                      ),
                  ],
                ),
                const SizedBox(height: Space.x3),
              ],
              Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Expanded(
                    child: TextField(
                      controller: _controller,
                      minLines: 1,
                      maxLines: 5,
                      maxLength: AssistantChatProvider.maxLength,
                      buildCounter:
                          (
                            context, {
                            required currentLength,
                            required isFocused,
                            maxLength,
                          }) => null,
                      textInputAction: TextInputAction.send,
                      onSubmitted: (_) => _send(),
                      decoration: InputDecoration(hintText: widget.hint),
                    ),
                  ),
                  const SizedBox(width: Space.x2),
                  IconButton.filled(
                    tooltip: ChatComposer.sendTooltip,
                    onPressed: widget.enabled ? _send : null,
                    icon: const Icon(Icons.arrow_upward),
                  ),
                ],
              ),
              if (widget.error != null) AttentionLine(widget.error!),
              if (widget.trailing != null) ...[
                const SizedBox(height: Space.x2),
                widget.trailing!,
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// One line above a chat when replies come from templates.
class ModelQuietNote extends StatelessWidget {
  const ModelQuietNote({super.key});

  static const String text =
      'Replies are simpler right now because the home model is not '
      'answering. What you say is still saved to your day log.';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.info_outline, size: 18, color: colors.attention),
          const SizedBox(width: Space.x2),
          Expanded(child: Text(text, style: theme.textTheme.bodySmall)),
        ],
      ),
    );
  }
}
