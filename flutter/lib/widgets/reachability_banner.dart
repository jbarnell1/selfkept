import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/reachability.dart';
import '../theme/tokens.dart';

/// Pinned above the app when the server cannot be reached. Amber, not red:
/// it is a condition to notice, not an alarm.
class ReachabilityBanner extends StatelessWidget {
  static const String message = 'Not reachable. Are you on the tailnet?';

  const ReachabilityBanner({super.key});

  @override
  Widget build(BuildContext context) {
    final reachable = context.select<Reachability, bool>((r) => r.isReachable);
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return AnimatedSize(
      duration: Motion.standard,
      curve: Motion.curve,
      alignment: Alignment.topCenter,
      child: reachable
          ? const SizedBox(width: double.infinity, height: 0)
          : Material(
              color: colors.attention.withValues(alpha: 0.16),
              child: SafeArea(
                bottom: false,
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: Space.x4,
                    vertical: Space.x3,
                  ),
                  child: Row(
                    children: [
                      Icon(
                        Icons.cloud_off_outlined,
                        size: 18,
                        color: colors.attention,
                      ),
                      const SizedBox(width: Space.x3),
                      Expanded(
                        child: Text(
                          message,
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: theme.colorScheme.onSurface,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
    );
  }
}
