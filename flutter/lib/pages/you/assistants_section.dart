import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../theme/tokens.dart';
import '../../widgets/page_body.dart';
import '../../widgets/surface_card.dart';

/// Assistants connected to your record, and a way to manage them.
class AssistantsSection extends StatelessWidget {
  const AssistantsSection({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Assistants'),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Connect Open WebUI or another assistant to ask questions about your data.',
                style: theme.textTheme.bodyMedium,
              ),
              const SizedBox(height: Space.x2),
              TextButton(
                onPressed: () => context.push('/connections'),
                child: const Text('Manage'),
              ),
              const SizedBox(height: Space.x2),
              Text(
                'Take your data with you, or build a summary to paste into another model.',
                style: theme.textTheme.bodyMedium,
              ),
              const SizedBox(height: Space.x2),
              TextButton(
                onPressed: () => context.push('/export'),
                child: const Text('Export'),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
