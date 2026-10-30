import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../theme/tokens.dart';
import '../../widgets/page_body.dart';
import '../../widgets/surface_card.dart';

/// Links to Sources and to the setup steps.
class SourcesSection extends StatelessWidget {
  const SourcesSection({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Sources'),
        SurfaceCard(
          onTap: () => context.push('/sources'),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  'Your phone, your ring and anything else that feeds your record. Connect, pause or check the last sync.',
                  style: theme.textTheme.bodyMedium,
                ),
              ),
              const SizedBox(width: Space.x3),
              const Icon(Icons.chevron_right),
            ],
          ),
        ),
        const SizedBox(height: Space.x3),
        SurfaceCard(
          onTap: () => context.push('/welcome/steps'),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  'Setup steps: what you said you use, and anything left to connect.',
                  style: theme.textTheme.bodyMedium,
                ),
              ),
              const SizedBox(width: Space.x3),
              const Icon(Icons.chevron_right),
            ],
          ),
        ),
      ],
    );
  }
}
