import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../config/app_config.dart';
import '../../providers/auth_provider.dart';
import '../../providers/insights_provider.dart';
import '../../providers/server_settings.dart';
import '../../providers/startup_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/key_value_row.dart';
import '../../widgets/page_body.dart';
import '../../widgets/server_address_field.dart';
import '../../widgets/surface_card.dart';
import '../../widgets/dialogs.dart';

/// The server this app talks to, its version and features.
class ServerSection extends StatelessWidget {
  const ServerSection({super.key});

  static const String recomputeLabel = 'Recompute insights';
  static const String recomputeMessage =
      'Recomputing. Today and Insights update once it finishes.';

  Future<void> _recompute(BuildContext context) async {
    final insights = context.read<InsightsProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final ok = await insights.recompute();
    showMessage(
      messenger,
      ok ? recomputeMessage : (insights.error ?? 'That did not work.'),
    );
  }

  @override
  Widget build(BuildContext context) {
    final settings = context.watch<ServerSettings>();
    final startup = context.watch<StartupProvider>();
    final recomputing = context.select<InsightsProvider, bool>(
      (i) => i.isRecomputing,
    );
    final theme = Theme.of(context);
    final info = startup.versionInfo;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Server'),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              KeyValueRow('Address', settings.currentUrl, numeric: true),
              KeyValueRow(
                'Server version',
                info == null ? 'Unknown' : info.version,
                numeric: true,
              ),
              KeyValueRow('App version', AppConfig.appVersion, numeric: true),
              if (info != null && info.features.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: Space.x1),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      SizedBox(
                        width: 120,
                        child: Text(
                          'Features',
                          style: theme.textTheme.bodySmall,
                        ),
                      ),
                      Expanded(
                        child: Wrap(
                          spacing: Space.x2,
                          runSpacing: Space.x2,
                          children: [
                            for (final f in info.features)
                              Chip(
                                label: Text(f),
                                visualDensity: VisualDensity.compact,
                                padding: const EdgeInsets.symmetric(
                                  horizontal: Space.x2,
                                ),
                              ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              const SizedBox(height: Space.x4),
              const Divider(),
              const SizedBox(height: Space.x4),
              Text(
                'Rebuilds your baselines and cards from what the server holds. Useful after a large backfill.',
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: Space.x3),
              Align(
                alignment: Alignment.centerLeft,
                child: OutlinedButton.icon(
                  onPressed: recomputing ? null : () => _recompute(context),
                  icon: const Icon(Icons.refresh, size: 18),
                  label: Text(recomputing ? 'Recomputing' : recomputeLabel),
                ),
              ),
              const SizedBox(height: Space.x4),
              const Divider(),
              const SizedBox(height: Space.x4),
              Text(
                'Changing the address signs you out of this server.',
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: Space.x3),
              ServerAddressField(
                compact: true,
                onSaved: () async {
                  await context.read<AuthProvider>().logout();
                  if (context.mounted) {
                    await context.read<StartupProvider>().run();
                  }
                },
              ),
            ],
          ),
        ),
      ],
    );
  }
}
