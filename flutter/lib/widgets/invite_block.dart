import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../providers/household_provider.dart';
import '../theme/tokens.dart';
import 'copyable_value.dart';

/// Creating and revoking household invites, for the admin. Used on You and in
/// the setup steps.
class InviteBlock extends StatelessWidget {
  /// Shown under the heading; says how the code is used.
  final String lead;

  const InviteBlock({
    super.key,
    this.lead =
        'A code works once. Share it in person or over a channel you trust.',
  });

  Future<void> _create(BuildContext context) async {
    final provider = context.read<HouseholdProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final invite = await provider.createInvite();
    final error = provider.error;
    if (invite == null && error != null) {
      messenger.showSnackBar(SnackBar(content: Text(error)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final household = context.watch<HouseholdProvider>();
    final theme = Theme.of(context);
    final latest = household.latestInvite;
    final code = latest?.code;
    final expires = latest?.expiresAt;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Invite someone', style: theme.textTheme.titleMedium),
        const SizedBox(height: Space.x1),
        Text(lead, style: theme.textTheme.bodySmall),
        const SizedBox(height: Space.x3),
        AnimatedSwitcher(
          duration: Motion.standard,
          switchInCurve: Motion.curve,
          child: code == null
              ? Align(
                  alignment: Alignment.centerLeft,
                  child: OutlinedButton.icon(
                    onPressed: household.isBusy ? null : () => _create(context),
                    icon: const Icon(Icons.add, size: 18),
                    label: const Text('Create invite code'),
                  ),
                )
              : CopyableValue(
                  code,
                  key: ValueKey(code),
                  copyTooltip: 'Copy code',
                  copiedMessage: 'Code copied.',
                  style: theme.textTheme.headlineSmall,
                  caption: expires == null
                      ? null
                      : Text(
                          'Expires ${DateFormat.yMMMd().add_jm().format(expires.toLocal())}',
                          style: theme.textTheme.bodySmall,
                        ),
                  actions: [
                    IconButton(
                      tooltip: 'New code',
                      onPressed: household.isBusy
                          ? null
                          : () => _create(context),
                      icon: const Icon(Icons.refresh),
                    ),
                  ],
                ),
        ),
        if (household.invites.isNotEmpty) ...[
          const SizedBox(height: Space.x4),
          Text(
            '${household.invites.length} pending ${household.invites.length == 1 ? 'invite' : 'invites'}',
            style: theme.textTheme.labelMedium,
          ),
          for (final invite in household.invites)
            ListTile(
              contentPadding: EdgeInsets.zero,
              dense: true,
              title: Text(
                invite.expiresAt == null
                    ? 'Pending invite'
                    : 'Expires ${DateFormat.yMMMd().format(invite.expiresAt!.toLocal())}',
              ),
              trailing: IconButton(
                tooltip: 'Revoke',
                onPressed: household.isBusy
                    ? null
                    : () => household.deleteInvite(invite.id),
                icon: const Icon(Icons.delete_outline),
              ),
            ),
        ],
      ],
    );
  }
}
