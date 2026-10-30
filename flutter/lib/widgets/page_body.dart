import 'package:flutter/material.dart';

import '../theme/tokens.dart';

/// Scrollable page content held to a readable width with generous gutters.
/// Every page in the shell and every public page sits inside one of these.
class PageBody extends StatelessWidget {
  final List<Widget> children;
  final double maxWidth;
  final EdgeInsetsGeometry? padding;
  final CrossAxisAlignment crossAxisAlignment;

  /// Pass [AlwaysScrollableScrollPhysics] under a RefreshIndicator so a short
  /// page can still be pulled.
  final ScrollPhysics? physics;

  const PageBody({
    super.key,
    required this.children,
    this.maxWidth = Space.maxContentWidth,
    this.padding,
    this.crossAxisAlignment = CrossAxisAlignment.stretch,
    this.physics,
  });

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.topCenter,
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: maxWidth),
        child: ListView(
          physics: physics,
          padding:
              padding ??
              const EdgeInsets.fromLTRB(
                Space.gutter,
                Space.x6,
                Space.gutter,
                Space.x8 * 2,
              ),
          children: [
            Column(crossAxisAlignment: crossAxisAlignment, children: children),
          ],
        ),
      ),
    );
  }
}

/// A page heading in the display face with an optional line beneath.
class PageHeading extends StatelessWidget {
  final String title;
  final String? subtitle;

  const PageHeading({super.key, required this.title, this.subtitle});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: theme.textTheme.displaySmall),
          if (subtitle != null) ...[
            const SizedBox(height: Space.x2),
            Text(
              subtitle!,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// A small section title above a card.
class SectionHeader extends StatelessWidget {
  final String title;
  final Widget? trailing;

  const SectionHeader({super.key, required this.title, this.trailing});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: Space.x8, bottom: Space.x3),
      child: Row(
        children: [
          Expanded(child: Text(title, style: theme.textTheme.headlineSmall)),
          ?trailing,
        ],
      ),
    );
  }
}
