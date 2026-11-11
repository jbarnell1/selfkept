import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/assistant.dart';
import '../../models/assistant_lookup.dart';
import '../../providers/assistant_chat_provider.dart';
import '../../providers/assistant_chat_session.dart';
import '../../providers/assistant_lookup_providers.dart';
import '../../theme/tokens.dart';
import '../../utils/api_error.dart';
import '../../utils/open_url.dart';
import '../../utils/relative_time.dart';
import 'assistant_feature.dart';
import 'lookup_sheet.dart';

/// The quiet line under a reply the server offered a lookup for. Tapping it
/// opens the lookup sheet and sends nothing.
class LookupOfferLine<T extends ChatSession> extends StatelessWidget {
  final ChatItem item;

  const LookupOfferLine({super.key, required this.item});

  static const String label = 'Look this up with a cloud model';

  Future<void> _open(BuildContext context, AssistantChatProvider chat) async {
    final question = item.questionMessageId;
    if (question == null) return;
    final provider = context.read<AssistantLookupProvider>().providerName;
    final result = await showLookupSheet(
      context,
      messageId: question,
      provider: provider,
    );
    if (result == null) return;
    chat.addLookupAnswer(result.done, from: item, provider: result.provider);
  }

  @override
  Widget build(BuildContext context) {
    if (!LookupFeature.watch(context)) return const SizedBox.shrink();
    if (!item.lookupOffered || item.questionMessageId == null) {
      return const SizedBox.shrink();
    }
    final session = context.read<T>();
    if (session is! AssistantChatProvider) return const SizedBox.shrink();
    final colors = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: Space.x1),
      child: TextButton.icon(
        style: TextButton.styleFrom(
          foregroundColor: colors.textSoft,
          visualDensity: VisualDensity.compact,
          padding: const EdgeInsets.symmetric(horizontal: Space.x2),
        ),
        onPressed: session.isBusy ? null : () => _open(context, session),
        icon: const Icon(Icons.travel_explore, size: 16),
        label: const Text(label),
      ),
    );
  }
}

/// A cloud model's answer: outlined and labelled so it never reads as a
/// local reply, with its sources, the "What was sent" row and the fixed
/// line under it.
class CloudAnswerBubble extends StatefulWidget {
  final ChatItem item;

  const CloudAnswerBubble({super.key, required this.item});

  static const String label = 'Cloud model';
  static const String sourcesTitle = 'Sources';
  static const String sentLabel = 'What was sent';
  static const String fixedLine = 'General research, not advice about you.';

  @override
  State<CloudAnswerBubble> createState() => _CloudAnswerBubbleState();
}

class _CloudAnswerBubbleState extends State<CloudAnswerBubble> {
  @override
  void initState() {
    super.initState();
    // The provider's name for an answer loaded with the conversation. Asked
    // only when the server has the lookup.
    if (widget.item.provider == null) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted || !LookupFeature.read(context)) return;
        context.read<AssistantLookupProvider>().ensureStatus();
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final item = widget.item;
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final colors = AppColors.of(context);
    final feature = LookupFeature.watch(context);
    final known = context.select<AssistantLookupProvider, String?>(
      (l) => l.providerName,
    );
    final provider = item.provider ?? (feature ? known : null);
    final width = MediaQuery.sizeOf(context).width;
    final maxWidth = (width * 0.85).clamp(240.0, 560.0);
    final lookupId = item.lookupId;
    return Align(
      alignment: Alignment.centerLeft,
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: maxWidth),
        child: Semantics(
          container: true,
          label: provider == null
              ? CloudAnswerBubble.label
              : '${CloudAnswerBubble.label}, $provider',
          child: DecoratedBox(
            key: const ValueKey('cloud-answer'),
            decoration: BoxDecoration(
              color: scheme.surface,
              borderRadius: Radii.cardRadius,
              border: Border.all(color: scheme.outline, width: 1.5),
            ),
            child: Padding(
              padding: const EdgeInsets.all(Space.x4),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Wrap(
                    crossAxisAlignment: WrapCrossAlignment.center,
                    spacing: Space.x2,
                    children: [
                      Icon(
                        Icons.cloud_outlined,
                        size: 18,
                        color: scheme.secondary,
                      ),
                      Text(
                        CloudAnswerBubble.label,
                        style: theme.textTheme.titleSmall,
                      ),
                      if (provider != null)
                        Text(
                          provider,
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: colors.textSoft,
                          ),
                        ),
                    ],
                  ),
                  const SizedBox(height: Space.x2),
                  SelectableText(item.text, style: theme.textTheme.bodyMedium),
                  if (item.sources.isNotEmpty) ...[
                    const SizedBox(height: Space.x3),
                    Text(
                      CloudAnswerBubble.sourcesTitle,
                      style: theme.textTheme.titleSmall,
                    ),
                    const SizedBox(height: Space.x1),
                    for (final s in item.sources) _SourceLink(source: s),
                  ],
                  if (feature && lookupId != null) ...[
                    const SizedBox(height: Space.x2),
                    TextButton.icon(
                      style: TextButton.styleFrom(
                        visualDensity: VisualDensity.compact,
                        padding: EdgeInsets.zero,
                      ),
                      onPressed: () => showWhatWasSent(context, lookupId),
                      icon: const Icon(Icons.outbox_outlined, size: 18),
                      label: const Text(CloudAnswerBubble.sentLabel),
                    ),
                  ],
                  const SizedBox(height: Space.x2),
                  Text(
                    CloudAnswerBubble.fixedLine,
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colors.textSoft,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _SourceLink extends StatelessWidget {
  final LookupSource source;

  const _SourceLink({required this.source});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final link = source.link;
    if (link == null) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: Space.x1),
        child: SelectableText(source.label, style: theme.textTheme.bodySmall),
      );
    }
    return Semantics(
      link: true,
      child: InkWell(
        borderRadius: Radii.controlRadius,
        onTap: () => openLink(context, link.toString()),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: Space.x1),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                Icons.open_in_new,
                size: 16,
                color: theme.colorScheme.primary,
              ),
              const SizedBox(width: Space.x2),
              Expanded(
                child: Text(
                  source.label,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.primary,
                    decoration: TextDecoration.underline,
                    decorationColor: theme.colorScheme.primary,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Opens the exact text one lookup sent, fresh from the server.
Future<void> showWhatWasSent(BuildContext context, String lookupId) =>
    showDialog<void>(
      context: context,
      builder: (_) => WhatWasSentDialog(lookupId: lookupId),
    );

class WhatWasSentDialog extends StatefulWidget {
  final String lookupId;

  const WhatWasSentDialog({super.key, required this.lookupId});

  static const String goneLine =
      'This record was deleted, so the text that was sent is no longer kept.';

  /// "Anonymized, Anthropic claude-opus-5-5, 2 hours ago".
  static String meta(LookupRecord r) => [
    r.mode.displayName,
    [r.provider, r.model].where((s) => s.trim().isNotEmpty).join(' '),
    if (r.sentAt != null) relativeTime(r.sentAt),
  ].where((s) => s.isNotEmpty).join(', ');

  @override
  State<WhatWasSentDialog> createState() => _WhatWasSentDialogState();
}

class _WhatWasSentDialogState extends State<WhatWasSentDialog> {
  late final Future<LookupRecord?> _record = context
      .read<AssistantLookupProvider>()
      .findSent(widget.lookupId);

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AlertDialog(
      title: const Text(CloudAnswerBubble.sentLabel),
      content: FutureBuilder<LookupRecord?>(
        future: _record,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const SizedBox(
              height: 80,
              child: Center(child: CircularProgressIndicator()),
            );
          }
          if (snapshot.hasError) {
            return Text(
              describeError(snapshot.error!),
              style: theme.textTheme.bodyMedium,
            );
          }
          final record = snapshot.data;
          if (record == null) {
            return Text(
              WhatWasSentDialog.goneLine,
              style: theme.textTheme.bodyMedium,
            );
          }
          return SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  WhatWasSentDialog.meta(record),
                  style: theme.textTheme.bodySmall,
                ),
                const SizedBox(height: Space.x3),
                SelectableText(record.text, style: theme.textTheme.bodyMedium),
              ],
            ),
          );
        },
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Close'),
        ),
      ],
    );
  }
}
