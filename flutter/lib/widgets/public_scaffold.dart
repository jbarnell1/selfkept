import 'package:flutter/material.dart';

import '../brand/brand.dart';
import '../theme/tokens.dart';
import 'surface_card.dart';

/// The product wordmark in the display face, with the tagline beneath.
class BrandMark extends StatelessWidget {
  final bool showTagline;
  final TextAlign align;

  const BrandMark({
    super.key,
    this.showTagline = true,
    this.align = TextAlign.left,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: align == TextAlign.center
          ? CrossAxisAlignment.center
          : CrossAxisAlignment.start,
      children: [
        Text(
          Brand.productName,
          style: theme.textTheme.displaySmall,
          textAlign: align,
        ),
        if (showTagline) ...[
          const SizedBox(height: Space.x2),
          Text(
            Brand.tagline,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
            textAlign: align,
          ),
        ],
      ],
    );
  }
}

/// Layout for the unauthenticated pages: wordmark on top, one card of
/// content, held to a form width and centred on wide screens.
class PublicScaffold extends StatelessWidget {
  final String title;
  final String? lead;
  final List<Widget> children;
  final Widget? footer;

  const PublicScaffold({
    super.key,
    required this.title,
    this.lead,
    required this.children,
    this.footer,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 460),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(
                Space.gutter,
                Space.x8 * 1.5,
                Space.gutter,
                Space.x8,
              ),
              children: [
                const BrandMark(),
                const SizedBox(height: Space.x8 * 1.5),
                SurfaceCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(title, style: theme.textTheme.headlineMedium),
                      if (lead != null) ...[
                        const SizedBox(height: Space.x2),
                        Text(
                          lead!,
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: theme.colorScheme.onSurfaceVariant,
                          ),
                        ),
                      ],
                      const SizedBox(height: Space.x6),
                      ...children,
                    ],
                  ),
                ),
                if (footer != null) ...[
                  const SizedBox(height: Space.x6),
                  footer!,
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
