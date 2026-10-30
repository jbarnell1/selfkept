import 'package:flutter/material.dart';

import '../theme/tokens.dart';
import 'surface_card.dart';

/// One setup step: what it is, whether it is done, and what to do. The body
/// stays visible when done so the person can see what was set up.
class SetupStepCard extends StatelessWidget {
  final IconData icon;
  final String title;
  final bool done;

  /// Replaces "Done" beside the title, for example "Invite ready".
  final String? doneLabel;
  final Widget child;

  const SetupStepCard({
    super.key,
    required this.icon,
    required this.title,
    required this.done,
    this.doneLabel,
    required this.child,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x3),
      child: SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                CircleAvatar(
                  radius: 18,
                  backgroundColor: colors.band.withValues(alpha: 0.35),
                  child: Icon(
                    done ? Icons.check : icon,
                    size: 18,
                    color: theme.colorScheme.onSurface,
                  ),
                ),
                const SizedBox(width: Space.x3),
                Expanded(
                  child: Text(title, style: theme.textTheme.titleMedium),
                ),
                if (done)
                  Semantics(
                    label: '$title: ${doneLabel ?? 'done'}',
                    excludeSemantics: true,
                    child: Text(
                      doneLabel ?? 'Done',
                      style: theme.textTheme.labelMedium,
                    ),
                  ),
              ],
            ),
            const SizedBox(height: Space.x3),
            child,
          ],
        ),
      ),
    );
  }
}
