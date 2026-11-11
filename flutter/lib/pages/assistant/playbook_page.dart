import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/assistant.dart';
import '../../providers/assistant_pages_providers.dart';
import '../../services/assistant_service.dart';
import '../../theme/tokens.dart';
import '../../utils/open_url.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';

/// "Things worth trying": the reviewed playbook entries that apply today,
/// each with why it applies and where it comes from. Every word is the
/// server's.
class PlaybookPage extends StatelessWidget {
  const PlaybookPage({super.key});

  static const String title = 'Things worth trying';
  static const String subtitle =
      'Chosen from a short reviewed list by what your record shows today.';
  static const String emptyLine =
      'Nothing from the list fits today. It changes as your record does.';
  static const String loadErrorTitle = 'The list did not load.';
  static const String whyLabel = 'Why today';
  static const String sourceLabel = 'Source';

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (context) =>
          AssistantPlaybookProvider(context.read<AssistantService>())..load(),
      child: const Scaffold(
        appBar: SubpageAppBar(fallback: '/assistant', title: Text('Assistant')),
        body: _PlaybookBody(),
      ),
    );
  }
}

class _PlaybookBody extends StatelessWidget {
  const _PlaybookBody();

  @override
  Widget build(BuildContext context) {
    final playbook = context.watch<AssistantPlaybookProvider>();
    final theme = Theme.of(context);
    final Widget content;
    if (!playbook.hasLoaded) {
      content = playbook.error != null && !playbook.isLoading
          ? ErrorView(
              title: PlaybookPage.loadErrorTitle,
              detail: playbook.error,
              onRetry: playbook.load,
            )
          : const LoadingView();
    } else if (playbook.entries.isEmpty) {
      content = SurfaceCard(
        child: Text(PlaybookPage.emptyLine, style: theme.textTheme.bodyMedium),
      );
    } else {
      content = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final entry in playbook.entries) ...[
            PlaybookCard(entry: entry),
            const SizedBox(height: Space.x3),
          ],
        ],
      );
    }
    return PageBody(
      children: [
        const PageHeading(
          title: PlaybookPage.title,
          subtitle: PlaybookPage.subtitle,
        ),
        content,
      ],
    );
  }
}

/// One entry: the title, the reviewed wording, why it applies, the source
/// and any caution.
class PlaybookCard extends StatelessWidget {
  final PlaybookEntry entry;

  const PlaybookCard({super.key, required this.entry});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final link = entry.sourceLink;
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(entry.title, style: theme.textTheme.titleMedium),
          if (entry.evidenceGrade.isNotEmpty) ...[
            const SizedBox(height: Space.x1),
            Text(
              entry.evidenceGrade,
              style: theme.textTheme.labelMedium?.copyWith(
                color: colors.textSoft,
              ),
            ),
          ],
          const SizedBox(height: Space.x3),
          Text(entry.text, style: theme.textTheme.bodyMedium),
          if (entry.why.isNotEmpty) ...[
            const SizedBox(height: Space.x3),
            Text(PlaybookPage.whyLabel, style: theme.textTheme.labelLarge),
            const SizedBox(height: Space.x1),
            Text(entry.why, style: theme.textTheme.bodySmall),
          ],
          for (final caution in entry.cautions)
            Padding(
              padding: const EdgeInsets.only(top: Space.x3),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(
                    Icons.warning_amber_outlined,
                    size: 18,
                    color: colors.attention,
                  ),
                  const SizedBox(width: Space.x2),
                  Expanded(
                    child: Text(caution, style: theme.textTheme.bodySmall),
                  ),
                ],
              ),
            ),
          if (entry.source.isNotEmpty) ...[
            const SizedBox(height: Space.x3),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${PlaybookPage.sourceLabel}: ',
                  style: theme.textTheme.bodySmall,
                ),
                Expanded(
                  child: link == null
                      ? Text(entry.source, style: theme.textTheme.bodySmall)
                      : Semantics(
                          link: true,
                          child: InkWell(
                            onTap: () => openLink(context, link.toString()),
                            child: Text(
                              entry.source,
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: theme.colorScheme.primary,
                                decoration: TextDecoration.underline,
                                decorationColor: theme.colorScheme.primary,
                              ),
                            ),
                          ),
                        ),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}
