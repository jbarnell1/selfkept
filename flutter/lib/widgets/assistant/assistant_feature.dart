import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/startup_provider.dart';
import '../../theme/tokens.dart';
import '../surface_card.dart';

/// The in-app assistant is there when the server lists this feature in
/// /system/version. An older server has none of its routes.
abstract final class AssistantFeature {
  static const String name = 'assistant.chat';

  /// Watches the startup check, so a server update shows without a restart.
  static bool watch(BuildContext context) =>
      context.select<StartupProvider, bool>(
        (s) => s.versionInfo?.hasFeature(name) ?? false,
      );
}

/// The cloud lookup is there when the server lists this feature: the server
/// switch is on and a provider key is set. Without it there is no offer, no
/// row under You, and no request to the lookup routes.
abstract final class LookupFeature {
  static const String name = 'assistant.lookup';

  static bool watch(BuildContext context) =>
      context.select<StartupProvider, bool>(
        (s) => s.versionInfo?.hasFeature(name) ?? false,
      );

  static bool read(BuildContext context) =>
      context.read<StartupProvider>().versionInfo?.hasFeature(name) ?? false;
}

/// What the Assistant tab says on a server without the assistant.
class AssistantNeedsUpdate extends StatelessWidget {
  const AssistantNeedsUpdate({super.key});

  static const String title = 'Your server needs an update for this';
  static const String body =
      'The assistant comes with a newer version of the server. Once the '
      'person who runs it updates, this tab fills in. Everything else keeps '
      'working as it is.';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: theme.textTheme.titleMedium),
          const SizedBox(height: Space.x2),
          Text(body, style: theme.textTheme.bodyMedium),
        ],
      ),
    );
  }
}
