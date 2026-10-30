import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/household.dart';
import '../../providers/auth_provider.dart';
import '../../providers/household_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/invite_block.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';

/// The household, its members and, for the admin, invites.
class HouseholdSection extends StatelessWidget {
  const HouseholdSection({super.key});

  Future<void> _rename(BuildContext context, Household household) async {
    final controller = TextEditingController(text: household.name);
    final provider = context.read<HouseholdProvider>();
    final name = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Household name'),
        content: TextField(
          controller: controller,
          autofocus: true,
          textCapitalization: TextCapitalization.words,
          onSubmitted: (v) => Navigator.of(context).pop(v),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(controller.text),
            child: const Text('Save'),
          ),
        ],
      ),
    );
    controller.dispose();
    if (name != null && name.trim().isNotEmpty) await provider.rename(name);
  }

  @override
  Widget build(BuildContext context) {
    final household = context.watch<HouseholdProvider>();
    final isAdmin = context.select<AuthProvider, bool>((a) => a.isAdmin);
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final data = household.household;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(
          title: 'Household',
          trailing: (isAdmin && data != null)
              ? TextButton(
                  onPressed: () => _rename(context, data),
                  child: const Text('Rename'),
                )
              : null,
        ),
        SurfaceCard(
          child: data == null
              ? (household.isLoading
                    ? const LoadingView()
                    : ErrorView(
                        title: 'The household did not load.',
                        detail: household.error,
                        onRetry: household.load,
                      ))
              : Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(data.name, style: theme.textTheme.headlineMedium),
                    const SizedBox(height: Space.x2),
                    Text(
                      '${data.members.length} ${data.members.length == 1 ? 'member' : 'members'}',
                      style: theme.textTheme.bodySmall,
                    ),
                    const SizedBox(height: Space.x4),
                    for (final m in data.members)
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: Space.x2),
                        child: Row(
                          children: [
                            CircleAvatar(
                              radius: 16,
                              backgroundColor: colors.band.withValues(
                                alpha: 0.35,
                              ),
                              child: Text(
                                m.displayName.isEmpty
                                    ? '?'
                                    : m.displayName[0].toUpperCase(),
                                style: theme.textTheme.labelLarge,
                              ),
                            ),
                            const SizedBox(width: Space.x3),
                            Expanded(
                              child: Text(
                                m.displayName,
                                style: theme.textTheme.bodyLarge,
                              ),
                            ),
                            if (m.isAdmin)
                              Text(
                                m.role.displayName,
                                style: theme.textTheme.labelSmall,
                              ),
                          ],
                        ),
                      ),
                    if (isAdmin) ...[
                      const SizedBox(height: Space.x4),
                      const Divider(),
                      const SizedBox(height: Space.x4),
                      const InviteBlock(),
                    ],
                  ],
                ),
        ),
      ],
    );
  }
}
