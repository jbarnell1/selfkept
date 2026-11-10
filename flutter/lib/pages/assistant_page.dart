import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../models/assistant.dart';
import '../providers/assistant_provider.dart';
import '../theme/tokens.dart';
import '../utils/relative_time.dart';
import '../widgets/assistant/assistant_feature.dart';
import '../widgets/assistant/chat_view.dart' show ModelQuietNote;
import '../widgets/dialogs.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';

/// Assistant: conversations with the home model, the evening check-in and
/// the things worth trying today. Everything here is the person's own.
class AssistantPage extends StatefulWidget {
  const AssistantPage({super.key});

  static const String subtitle =
      'Talk the day through. Anything worth keeping goes on today\'s log, '
      'and none of it is shared with the household.';
  static const String newLabel = 'New conversation';
  static const String checkinLabel = 'Evening check-in';
  static const String playbookTitle = 'Things worth trying';
  static const String playbookLine =
      'Reviewed suggestions that fit what your record shows today.';
  static const String listTitle = 'Conversations';
  static const String emptyLine =
      'No conversations yet. Start one and say how the day is going.';
  static const String loadErrorLine = 'Conversations did not load.';
  static const String deleteQuestion =
      'Delete this conversation? Entries it saved to your day log stay.';
  static const String deleteLabel = 'Delete';
  static const String keepLabel = 'Keep';
  static const String deletedMessage = 'Conversation deleted.';
  static const String deleteTooltip = 'Delete conversation';

  @override
  State<AssistantPage> createState() => _AssistantPageState();
}

class _AssistantPageState extends State<AssistantPage> {
  bool _asked = false;

  void _loadOnce() {
    if (_asked) return;
    _asked = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final assistant = context.read<AssistantProvider>();
      if (!assistant.isLoading) assistant.load();
    });
  }

  Future<void> _create() async {
    final assistant = context.read<AssistantProvider>();
    final router = GoRouter.of(context);
    final messenger = ScaffoldMessenger.of(context);
    final created = await assistant.create();
    if (created == null) {
      showMessage(messenger, assistant.actionError ?? 'That did not work.');
      return;
    }
    await router.push('/assistant/chat/${created.id}');
    await assistant.refreshList();
  }

  @override
  Widget build(BuildContext context) {
    final available = AssistantFeature.watch(context);
    if (available) _loadOnce();
    final theme = Theme.of(context);
    return SafeArea(
      child: RefreshIndicator(
        onRefresh: () => context.read<AssistantProvider>().load(),
        color: theme.colorScheme.primary,
        child: PageBody(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            const PageHeading(
              title: 'Assistant',
              subtitle: AssistantPage.subtitle,
            ),
            if (!available)
              const AssistantNeedsUpdate()
            else ...[
              _Actions(onCreate: _create),
              const SizedBox(height: Space.x3),
              SurfaceCard(
                onTap: () => context.push('/assistant/playbook'),
                child: Row(
                  children: [
                    Icon(
                      Icons.lightbulb_outline,
                      color: theme.colorScheme.primary,
                    ),
                    const SizedBox(width: Space.x4),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            AssistantPage.playbookTitle,
                            style: theme.textTheme.titleMedium,
                          ),
                          const SizedBox(height: Space.x1),
                          Text(
                            AssistantPage.playbookLine,
                            style: theme.textTheme.bodySmall,
                          ),
                        ],
                      ),
                    ),
                    const Icon(Icons.chevron_right),
                  ],
                ),
              ),
              const SectionHeader(title: AssistantPage.listTitle),
              const _ConversationList(),
            ],
          ],
        ),
      ),
    );
  }
}

class _Actions extends StatelessWidget {
  final VoidCallback onCreate;

  const _Actions({required this.onCreate});

  @override
  Widget build(BuildContext context) {
    final assistant = context.watch<AssistantProvider>();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (assistant.modelQuiet) const ModelQuietNote(),
        Wrap(
          spacing: Space.x3,
          runSpacing: Space.x3,
          children: [
            FilledButton.icon(
              onPressed: assistant.isCreating ? null : onCreate,
              icon: const Icon(Icons.add, size: 18),
              label: const Text(AssistantPage.newLabel),
            ),
            OutlinedButton.icon(
              onPressed: () => context.push('/assistant/checkin'),
              icon: const Icon(Icons.nightlight_outlined, size: 18),
              label: const Text(AssistantPage.checkinLabel),
            ),
          ],
        ),
      ],
    );
  }
}

class _ConversationList extends StatefulWidget {
  const _ConversationList();

  @override
  State<_ConversationList> createState() => _ConversationListState();
}

class _ConversationListState extends State<_ConversationList> {
  /// The conversation asking "Delete this conversation?", if any.
  String? _confirming;

  Future<void> _delete(String id) async {
    final assistant = context.read<AssistantProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final ok = await assistant.delete(id);
    if (!mounted) return;
    setState(() => _confirming = null);
    showMessage(
      messenger,
      ok ? AssistantPage.deletedMessage : (assistant.actionError ?? ''),
    );
  }

  void _open(ConversationSummary c) {
    final today = DateTime.now();
    final day = c.day;
    final tonight =
        c.kind == ConversationKind.checkin &&
        day != null &&
        day.year == today.year &&
        day.month == today.month &&
        day.day == today.day;
    final assistant = context.read<AssistantProvider>();
    final path = tonight ? '/assistant/checkin' : '/assistant/chat/${c.id}';
    context.push(path).then((_) => assistant.refreshList());
  }

  @override
  Widget build(BuildContext context) {
    final assistant = context.watch<AssistantProvider>();
    final theme = Theme.of(context);
    if (!assistant.hasLoaded) {
      if (assistant.error != null && !assistant.isLoading) {
        return SurfaceCard(
          child: Row(
            children: [
              Expanded(
                child: Text(
                  AssistantPage.loadErrorLine,
                  style: theme.textTheme.bodyMedium,
                ),
              ),
              TextButton(
                onPressed: assistant.load,
                child: const Text('Try again'),
              ),
            ],
          ),
        );
      }
      return const LoadingView();
    }
    final list = assistant.conversations;
    if (list.isEmpty) {
      return SurfaceCard(
        child: Text(AssistantPage.emptyLine, style: theme.textTheme.bodyMedium),
      );
    }
    return SurfaceCard(
      padding: const EdgeInsets.symmetric(vertical: Space.x2),
      child: Column(
        children: [
          for (final c in list)
            if (_confirming == c.id)
              _ConfirmDelete(
                key: ValueKey('confirm-${c.id}'),
                onKeep: () => setState(() => _confirming = null),
                onDelete: () => _delete(c.id),
              )
            else
              ListTile(
                key: ValueKey('conversation-${c.id}'),
                leading: Icon(
                  c.kind == ConversationKind.checkin
                      ? Icons.nightlight_outlined
                      : Icons.chat_bubble_outline,
                ),
                title: Text(
                  c.title.isEmpty ? c.kind.displayName : c.title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                ),
                subtitle: c.updatedAt == null
                    ? null
                    : Text(relativeTime(c.updatedAt)),
                onTap: () => _open(c),
                trailing: IconButton(
                  tooltip: AssistantPage.deleteTooltip,
                  icon: const Icon(Icons.delete_outline),
                  onPressed: () => setState(() => _confirming = c.id),
                ),
              ),
        ],
      ),
    );
  }
}

class _ConfirmDelete extends StatelessWidget {
  final VoidCallback onKeep;
  final VoidCallback onDelete;

  const _ConfirmDelete({
    super.key,
    required this.onKeep,
    required this.onDelete,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: Space.x4,
        vertical: Space.x2,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(AssistantPage.deleteQuestion, style: theme.textTheme.bodyMedium),
          const SizedBox(height: Space.x2),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              TextButton(
                onPressed: onKeep,
                child: const Text(AssistantPage.keepLabel),
              ),
              const SizedBox(width: Space.x2),
              FilledButton.tonal(
                onPressed: onDelete,
                child: const Text(AssistantPage.deleteLabel),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
