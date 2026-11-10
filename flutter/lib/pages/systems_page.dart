import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../theme/tokens.dart';
import '../widgets/page_body.dart';
import '../widgets/surface_card.dart';
import 'trends_page.dart';

/// Systems: the way into Labs, then every trend against your own usual.
/// A thin frame around the trends list for now.
class SystemsPage extends StatelessWidget {
  const SystemsPage({super.key});

  static const String subtitle =
      'Your results and your readings, each against your own usual.';
  static const String labsLine =
      'Blood work and other results, marker by marker.';

  @override
  Widget build(BuildContext context) {
    return const TrendsPage(
      title: 'Systems',
      subtitle: subtitle,
      leading: [
        _LabsEntry(),
        SectionHeader(title: 'Trends'),
      ],
    );
  }
}

class _LabsEntry extends StatelessWidget {
  const _LabsEntry();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Semantics(
      button: true,
      child: SurfaceCard(
        onTap: () => context.push('/labs'),
        child: Row(
          children: [
            Icon(Icons.science_outlined, color: theme.colorScheme.primary),
            const SizedBox(width: Space.x4),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Labs', style: theme.textTheme.titleMedium),
                  const SizedBox(height: Space.x1),
                  Text(SystemsPage.labsLine, style: theme.textTheme.bodySmall),
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
