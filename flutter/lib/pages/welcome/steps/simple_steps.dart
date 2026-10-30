import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../models/setup.dart';
import '../../../providers/connect_provider.dart';
import '../../../providers/household_provider.dart';
import '../../../providers/labs_provider.dart';
import '../../../providers/server_settings.dart';
import '../../../theme/tokens.dart';
import '../../../widgets/invite_block.dart';
import '../../../widgets/open_webui_steps.dart';
import '../../../widgets/setup_step_card.dart';
import '../../../widgets/status_views.dart';
import '../setup_icons.dart';

/// Blood test results: the scan and review flow, one tap away.
class LabsStep extends StatelessWidget {
  const LabsStep({super.key});

  @override
  Widget build(BuildContext context) {
    final labs = context.watch<LabsProvider>();
    return SetupStepCard(
      icon: setupItemIcon(SetupItem.labs),
      title: 'Blood test results',
      done: labs.hasAnyResults,
      doneLabel: 'Added',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Photograph a printed or on-screen report, or type the numbers. '
            'You check every value before it is kept.',
          ),
          const SizedBox(height: Space.x3),
          OutlinedButton.icon(
            onPressed: () => context.push('/labs/add'),
            icon: const Icon(Icons.add, size: 18),
            label: Text(labs.hasAnyResults ? 'Add another' : 'Add results'),
          ),
        ],
      ),
    );
  }
}

/// The admin invites the rest of the household.
class HouseholdStep extends StatelessWidget {
  const HouseholdStep({super.key});

  @override
  Widget build(BuildContext context) {
    final household = context.watch<HouseholdProvider>();
    final server = context.read<ServerSettings>().currentUrl;
    final joined = household.otherMembers;
    return SetupStepCard(
      icon: setupItemIcon(SetupItem.household),
      title: 'Your household',
      done: joined.isNotEmpty,
      doneLabel: 'Joined',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (joined.isNotEmpty) ...[
            Text('${joined.map((m) => m.displayName).join(', ')} joined.'),
            const SizedBox(height: Space.x4),
          ],
          InviteBlock(
            lead:
                'They open $server, choose Join a household and enter the '
                'code. Nothing of yours is shared until you say so under You.',
          ),
        ],
      ),
    );
  }
}

/// A private assistant over MCP.
class AssistantStep extends StatelessWidget {
  const AssistantStep({super.key});

  @override
  Widget build(BuildContext context) {
    final connect = context.watch<ConnectProvider>();
    final data = connect.connections;
    final linked =
        data != null && (data.assistants.isNotEmpty || data.tokens.isNotEmpty);
    return SetupStepCard(
      icon: setupItemIcon(SetupItem.assistant),
      title: 'A private assistant',
      done: linked,
      doneLabel: 'Connected',
      child: data == null
          ? (connect.error == null
                ? const LoadingView()
                : AttentionLine(connect.error ?? ''))
          : Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                OpenWebUiSteps(mcpUrl: data.mcpUrl),
                const SizedBox(height: Space.x2),
                TextButton(
                  onPressed: () => context.push('/connections'),
                  child: const Text('Assistants and tokens'),
                ),
              ],
            ),
    );
  }
}
