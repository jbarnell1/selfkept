import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/connect.dart';
import '../../providers/assistant_pages_providers.dart';
import '../../services/assistant_service.dart';
import '../../theme/tokens.dart';
import '../../utils/relative_time.dart';
import '../../widgets/dialogs.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';

/// "What the assistant remembers": the facts kept between conversations,
/// including any an assistant connected over /mcp wrote. Only the person
/// sees these.
class AssistantMemoryPage extends StatelessWidget {
  const AssistantMemoryPage({super.key});

  static const String title = 'What the assistant remembers';
  static const String subtitle =
      'Facts it uses to make sense of what you say. Only you see these.';
  static const String emptyLine =
      'Nothing yet. When you mention something lasting, such as eating '
      'vegetarian or working nights, it shows here.';
  static const String loadErrorTitle = 'These did not load.';
  static const String deleteQuestion = 'Forget this?';
  static const String deleteLabel = 'Forget';
  static const String keepLabel = 'Keep';
  static const String deletedMessage = 'Forgotten.';
  static const String deleteTooltip = 'Forget this';

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (context) =>
          AssistantMemoryProvider(context.read<AssistantService>())..load(),
      child: const Scaffold(
        appBar: SubpageAppBar(fallback: '/you', title: Text('You')),
        body: _MemoryBody(),
      ),
    );
  }
}

class _MemoryBody extends StatefulWidget {
  const _MemoryBody();

  @override
  State<_MemoryBody> createState() => _MemoryBodyState();
}

class _MemoryBodyState extends State<_MemoryBody> {
  String? _confirming;

  Future<void> _delete(String id) async {
    final memory = context.read<AssistantMemoryProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final ok = await memory.delete(id);
    if (!mounted) return;
    setState(() => _confirming = null);
    showMessage(
      messenger,
      ok ? AssistantMemoryPage.deletedMessage : (memory.deleteError ?? ''),
    );
  }

  @override
  Widget build(BuildContext context) {
    final memory = context.watch<AssistantMemoryProvider>();
    final theme = Theme.of(context);
    final Widget content;
    if (!memory.hasLoaded) {
      content = memory.error != null && !memory.isLoading
          ? ErrorView(
              title: AssistantMemoryPage.loadErrorTitle,
              detail: memory.error,
              onRetry: memory.load,
            )
          : const LoadingView();
    } else if (memory.items.isEmpty) {
      content = SurfaceCard(
        child: Text(
          AssistantMemoryPage.emptyLine,
          style: theme.textTheme.bodyMedium,
        ),
      );
    } else {
      content = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final item in memory.items) ...[
            _MemoryCard(
              key: ValueKey(item.id),
              item: item,
              confirming: _confirming == item.id,
              onAsk: () => setState(() => _confirming = item.id),
              onKeep: () => setState(() => _confirming = null),
              onDelete: () => _delete(item.id),
            ),
            const SizedBox(height: Space.x3),
          ],
        ],
      );
    }
    return PageBody(
      children: [
        const PageHeading(
          title: AssistantMemoryPage.title,
          subtitle: AssistantMemoryPage.subtitle,
        ),
        content,
      ],
    );
  }
}

class _MemoryCard extends StatelessWidget {
  final AiMemoryItem item;
  final bool confirming;
  final VoidCallback onAsk;
  final VoidCallback onKeep;
  final VoidCallback onDelete;

  const _MemoryCard({
    super.key,
    required this.item,
    required this.confirming,
    required this.onAsk,
    required this.onKeep,
    required this.onDelete,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final text = item.displayText ?? item.value;
    final meta = [
      item.type.displayName,
      ?item.source,
      if (item.updatedAt != null) relativeTime(item.updatedAt),
    ].join(', ');
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      text.isEmpty ? item.key : text,
                      style: theme.textTheme.bodyLarge,
                    ),
                    const SizedBox(height: Space.x1),
                    Text(meta, style: theme.textTheme.bodySmall),
                  ],
                ),
              ),
              if (!confirming)
                IconButton(
                  tooltip: AssistantMemoryPage.deleteTooltip,
                  icon: const Icon(Icons.delete_outline),
                  onPressed: onAsk,
                ),
            ],
          ),
          if (confirming) ...[
            const SizedBox(height: Space.x3),
            Row(
              children: [
                Expanded(
                  child: Text(
                    AssistantMemoryPage.deleteQuestion,
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
                TextButton(
                  onPressed: onKeep,
                  child: const Text(AssistantMemoryPage.keepLabel),
                ),
                const SizedBox(width: Space.x2),
                FilledButton.tonal(
                  onPressed: onDelete,
                  child: const Text(AssistantMemoryPage.deleteLabel),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}
