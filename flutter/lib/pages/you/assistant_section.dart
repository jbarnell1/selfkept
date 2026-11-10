import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../providers/assistant_pages_providers.dart';
import '../../providers/auth_provider.dart';
import '../../services/assistant_service.dart';
import '../../theme/app_theme.dart';
import '../../theme/tokens.dart';
import '../../widgets/assistant/assistant_feature.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';

/// The in-app assistant under You: what it remembers, the evening reminder,
/// and for an admin a check of the home model. Hidden on a server without
/// the assistant.
class AssistantYouSection extends StatelessWidget {
  const AssistantYouSection({super.key});

  static const String title = 'In-app assistant';
  static const String memoryTitle = 'What the assistant remembers';
  static const String memoryLine =
      'Facts it keeps between conversations. Delete any of them.';
  static const String nudgesTitle = 'Evening reminder';
  static const String nudgesLine =
      'A nudge to your own ntfy topic when the day is not closed out.';

  @override
  Widget build(BuildContext context) {
    if (!AssistantFeature.watch(context)) return const SizedBox.shrink();
    final isAdmin = context.select<AuthProvider, bool>((a) => a.isAdmin);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: title),
        _Link(
          icon: Icons.bookmark_outline,
          title: memoryTitle,
          line: memoryLine,
          onTap: () => context.push('/you/assistant-memory'),
        ),
        const SizedBox(height: Space.x3),
        _Link(
          icon: Icons.notifications_none,
          title: nudgesTitle,
          line: nudgesLine,
          onTap: () => context.push('/you/evening-reminder'),
        ),
        if (isAdmin) ...[
          const SizedBox(height: Space.x3),
          const ModelCheckCard(),
        ],
      ],
    );
  }
}

class _Link extends StatelessWidget {
  final IconData icon;
  final String title;
  final String line;
  final VoidCallback onTap;

  const _Link({
    required this.icon,
    required this.title,
    required this.line,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Semantics(
      button: true,
      child: SurfaceCard(
        onTap: onTap,
        child: Row(
          children: [
            Icon(icon, color: theme.colorScheme.primary),
            const SizedBox(width: Space.x4),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: theme.textTheme.titleMedium),
                  const SizedBox(height: Space.x1),
                  Text(line, style: theme.textTheme.bodySmall),
                ],
              ),
            ),
            const Icon(Icons.chevron_right),
          ],
        ),
      ),
    );
  }
}

/// Runs the server's fixed conversations through the home model and the
/// guard, and shows how each went. No one's record is used.
class ModelCheckCard extends StatelessWidget {
  const ModelCheckCard({super.key});

  static const String title = 'Check the home model';
  static const String line =
      'Runs a few short made-up conversations through your model and the '
      'checks every reply passes. No one\'s record is used.';
  static const String runLabel = 'Check the home model';
  static const String runningLabel = 'Checking';
  static const String notConfigured =
      'No home model is set up on the server, so there is nothing to check.';

  static String summary(int passed, int total) => '$passed of $total passed.';

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (context) =>
          AssistantModelCheckProvider(context.read<AssistantService>()),
      child: const _ModelCheckBody(),
    );
  }
}

class _ModelCheckBody extends StatelessWidget {
  const _ModelCheckBody();

  @override
  Widget build(BuildContext context) {
    final check = context.watch<AssistantModelCheckProvider>();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final result = check.result;
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(ModelCheckCard.title, style: theme.textTheme.titleMedium),
          const SizedBox(height: Space.x1),
          Text(ModelCheckCard.line, style: theme.textTheme.bodySmall),
          const SizedBox(height: Space.x3),
          OutlinedButton.icon(
            onPressed: check.isRunning ? null : check.run,
            icon: check.isRunning
                ? const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.fact_check_outlined, size: 18),
            label: Text(
              check.isRunning
                  ? ModelCheckCard.runningLabel
                  : ModelCheckCard.runLabel,
            ),
          ),
          if (check.error != null) AttentionLine(check.error!),
          if (result != null) ...[
            const SizedBox(height: Space.x4),
            if (!result.enabled)
              Text(
                ModelCheckCard.notConfigured,
                style: theme.textTheme.bodyMedium,
              )
            else ...[
              Text(
                [
                  if (result.model != null) result.model!,
                  ModelCheckCard.summary(result.passedCount, result.total),
                ].join(': '),
                style: AppTheme.numeric(
                  context,
                  base: theme.textTheme.bodyMedium,
                ),
              ),
              for (final c in result.cases)
                Padding(
                  padding: const EdgeInsets.only(top: Space.x3),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(
                        c.passed
                            ? Icons.check_circle_outline
                            : Icons.info_outline,
                        size: 18,
                        color: c.passed
                            ? theme.colorScheme.primary
                            : colors.attention,
                      ),
                      const SizedBox(width: Space.x2),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              c.checks.isEmpty ? c.name : c.checks,
                              style: theme.textTheme.bodyMedium,
                            ),
                            if (c.detail.isNotEmpty)
                              Text(c.detail, style: theme.textTheme.bodySmall),
                            Text(
                              '${c.latencyMs} ms',
                              style: AppTheme.numeric(
                                context,
                                base: theme.textTheme.bodySmall,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          ],
        ],
      ),
    );
  }
}
