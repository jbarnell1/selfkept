import 'package:flutter/material.dart';

import '../theme/tokens.dart';

/// A quiet loading state: a thin spinner and, optionally, one line.
class LoadingView extends StatelessWidget {
  final String? message;

  const LoadingView({super.key, this.message});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(Space.x8),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(
              width: 28,
              height: 28,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
            if (message != null) ...[
              const SizedBox(height: Space.x4),
              Text(
                message!,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// An error state that says what happened and offers one way forward.
class ErrorView extends StatelessWidget {
  final String title;
  final String? detail;
  final VoidCallback? onRetry;
  final String retryLabel;

  const ErrorView({
    super.key,
    this.title = 'That did not work.',
    this.detail,
    this.onRetry,
    this.retryLabel = 'Try again',
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(Space.x8),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                title,
                style: theme.textTheme.headlineSmall,
                textAlign: TextAlign.center,
              ),
              if (detail != null) ...[
                const SizedBox(height: Space.x2),
                Text(
                  detail!,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
              ],
              if (onRetry != null) ...[
                const SizedBox(height: Space.x6),
                Center(
                  child: OutlinedButton(
                    onPressed: onRetry,
                    child: Text(retryLabel),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// Inline attention line in amber: used for form and action errors.
class AttentionLine extends StatelessWidget {
  final String message;

  const AttentionLine(this.message, {super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AnimatedSwitcher(
      duration: Motion.short,
      switchInCurve: Motion.curve,
      switchOutCurve: Motion.reverseCurve,
      child: message.isEmpty
          ? const SizedBox.shrink()
          : Padding(
              key: ValueKey(message),
              padding: const EdgeInsets.only(top: Space.x3),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(
                    Icons.info_outline,
                    size: 18,
                    color: theme.colorScheme.error,
                  ),
                  const SizedBox(width: Space.x2),
                  Expanded(
                    child: Text(
                      message,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.error,
                      ),
                    ),
                  ),
                ],
              ),
            ),
    );
  }
}
