import 'package:flutter/material.dart';

import '../models/insight.dart';
import '../models/metric.dart';
import '../theme/tokens.dart';
import 'surface_card.dart';

/// One insight: a severity label and a left rule in its colour, the text at
/// reading size, the metrics as quiet chips, and three ways to answer plus a
/// dismiss. The card fades in once when it first appears.
class InsightCard extends StatelessWidget {
  final Insight insight;
  final void Function(InsightReaction reaction)? onReact;
  final VoidCallback? onDismiss;

  /// False in lists that scroll many cards past, where a fade would flicker.
  final bool animateIn;

  const InsightCard({
    super.key,
    required this.insight,
    this.onReact,
    this.onDismiss,
    this.animateIn = true,
  });

  /// The key on the severity dot, so tests can read its colour.
  static const Key severityDotKey = Key('insight-severity-dot');

  /// Sage for notice, amber for watch, terracotta for clinician. Never red.
  static Color colorFor(InsightSeverity severity, AppColors colors) =>
      switch (severity) {
        InsightSeverity.notice => colors.band,
        InsightSeverity.watch => colors.attention,
        InsightSeverity.clinician => colors.clinician,
      };

  /// The metric chips read as names, not keys.
  static String metricLabel(String metric) =>
      MetricKey.fromString(metric)?.displayName ?? metric.replaceAll('_', ' ');

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final tone = colorFor(insight.severity, colors);
    final dismissed = insight.isDismissed;

    final card = SurfaceCard(
      padding: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(
          Space.x6,
          Space.x4,
          Space.x4,
          Space.x3,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                _Dot(key: severityDotKey, color: tone),
                const SizedBox(width: Space.x2),
                Text(
                  insight.severity.displayName,
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
                if (dismissed) ...[
                  const SizedBox(width: Space.x2),
                  Text('Dismissed', style: theme.textTheme.labelSmall),
                ],
                const Spacer(),
                if (onDismiss != null && !dismissed)
                  IconButton(
                    tooltip: 'Dismiss',
                    visualDensity: VisualDensity.compact,
                    onPressed: onDismiss,
                    icon: const Icon(Icons.close, size: 18),
                  ),
              ],
            ),
            const SizedBox(height: Space.x2),
            Text(
              insight.text,
              style: theme.textTheme.bodyLarge?.copyWith(height: 1.55),
            ),
            if (insight.metrics.isNotEmpty) ...[
              const SizedBox(height: Space.x3),
              Wrap(
                spacing: Space.x2,
                runSpacing: Space.x2,
                children: [
                  for (var i = 0; i < insight.metrics.length; i++)
                    _MetricChip(
                      insight.nameAt(i) ?? metricLabel(insight.metrics[i]),
                    ),
                ],
              ),
            ],
            if (onReact != null) ...[
              const SizedBox(height: Space.x3),
              Wrap(
                spacing: Space.x1,
                children: [
                  for (final r in InsightReaction.values)
                    _ReactionButton(
                      reaction: r,
                      selected: insight.reaction == r,
                      onTap: () => onReact!(r),
                    ),
                ],
              ),
            ],
          ],
        ),
      ),
    );

    if (!animateIn) return card;
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: 1),
      duration: Motion.of(context, Motion.standard),
      curve: Motion.curve,
      child: card,
      builder: (context, t, child) => Opacity(
        opacity: t,
        child: Transform.translate(
          offset: Offset(0, (1 - t) * Space.x2),
          child: child,
        ),
      ),
    );
  }
}

class _Dot extends StatelessWidget {
  final Color color;

  const _Dot({super.key, required this.color});

  @override
  Widget build(BuildContext context) => Container(
    width: 8,
    height: 8,
    decoration: BoxDecoration(color: color, shape: BoxShape.circle),
  );
}

class _MetricChip extends StatelessWidget {
  final String label;

  const _MetricChip(this.label);

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: Space.x3,
        vertical: Space.x1,
      ),
      decoration: BoxDecoration(
        color: colors.surfaceRaised,
        borderRadius: const BorderRadius.all(Radius.circular(999)),
        border: Border.all(color: colors.hairline),
      ),
      child: Text(label, style: theme.textTheme.labelSmall),
    );
  }
}

class _ReactionButton extends StatelessWidget {
  final InsightReaction reaction;
  final bool selected;
  final VoidCallback onTap;

  const _ReactionButton({
    required this.reaction,
    required this.selected,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return TextButton.icon(
      onPressed: onTap,
      style: TextButton.styleFrom(
        foregroundColor: selected ? scheme.onSurface : scheme.onSurfaceVariant,
        backgroundColor: selected ? scheme.primaryContainer : null,
        minimumSize: const Size(0, 36),
        padding: const EdgeInsets.symmetric(horizontal: Space.x3),
        visualDensity: VisualDensity.compact,
      ),
      icon: Icon(selected ? Icons.check : _iconFor(reaction), size: 16),
      label: Text(reaction.displayName),
    );
  }

  static IconData _iconFor(InsightReaction reaction) => switch (reaction) {
    InsightReaction.helpful => Icons.thumb_up_outlined,
    InsightReaction.knew => Icons.visibility_outlined,
    InsightReaction.wrong => Icons.block_outlined,
  };
}
