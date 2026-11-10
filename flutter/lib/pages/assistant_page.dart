import 'package:flutter/material.dart';

import '../widgets/page_body.dart';
import '../widgets/surface_card.dart';

/// Assistant: where conversations will live. A placeholder until the
/// assistant itself is built.
class AssistantPage extends StatelessWidget {
  const AssistantPage({super.key});

  static const String subtitle = 'The assistant arrives in a later step.';
  static const String body =
      'Until then, the chips under "Add to today" on Today keep the day log, '
      'and an assistant you connect under You can still read your record.';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SafeArea(
      child: PageBody(
        children: [
          const PageHeading(title: 'Assistant', subtitle: subtitle),
          SurfaceCard(child: Text(body, style: theme.textTheme.bodyMedium)),
        ],
      ),
    );
  }
}
