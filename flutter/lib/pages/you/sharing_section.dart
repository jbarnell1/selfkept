import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/enums.dart';
import '../../models/household.dart';
import '../../providers/household_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';

/// What each member may see of yours.
class SharingSection extends StatelessWidget {
  const SharingSection({super.key});

  @override
  Widget build(BuildContext context) {
    final household = context.watch<HouseholdProvider>();
    final theme = Theme.of(context);
    if (!household.hasLoaded) return const SizedBox.shrink();
    final members = household.otherMembers;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Sharing'),
        if (members.isEmpty)
          SurfaceCard(
            child: Text(
              'When someone joins your household, you choose here what they can see of yours. Nothing is shared until you say so.',
              style: theme.textTheme.bodyMedium,
            ),
          )
        else
          for (final member in members) ...[
            _MemberShareCard(member: member),
            const SizedBox(height: Space.x3),
          ],
        AttentionLine(household.error ?? ''),
      ],
    );
  }
}

class _MemberShareCard extends StatelessWidget {
  final Member member;

  const _MemberShareCard({required this.member});

  @override
  Widget build(BuildContext context) {
    final household = context.watch<HouseholdProvider>();
    final granted = household.grantsFor(member.personId);
    final theme = Theme.of(context);
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(
        Space.x6,
        Space.x6,
        Space.x4,
        Space.x4,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'What ${member.displayName} can see',
            style: theme.textTheme.titleMedium,
          ),
          const SizedBox(height: Space.x1),
          Text(
            granted.isEmpty
                ? 'Nothing yet.'
                : '${granted.length} of ${ShareCategory.values.length} categories',
            style: theme.textTheme.bodySmall,
          ),
          const SizedBox(height: Space.x2),
          for (final category in ShareCategory.values)
            SwitchListTile.adaptive(
              contentPadding: EdgeInsets.zero,
              title: Text(category.displayName),
              subtitle: Text(category.description),
              value: granted.contains(category),
              onChanged: (on) =>
                  household.setGrant(member.personId, category, on),
            ),
        ],
      ),
    );
  }
}

/// What other members share with you.
class SharedWithMeSection extends StatelessWidget {
  const SharedWithMeSection({super.key});

  @override
  Widget build(BuildContext context) {
    final household = context.watch<HouseholdProvider>();
    final theme = Theme.of(context);
    if (!household.hasLoaded) return const SizedBox.shrink();
    final shared = household.sharedWithMe;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Shared with me'),
        SurfaceCard(
          child: shared.isEmpty
              ? Text(
                  'No one has shared anything with you yet.',
                  style: theme.textTheme.bodyMedium,
                )
              : Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    for (final s in shared)
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: Space.x2),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              s.displayName,
                              style: theme.textTheme.titleMedium,
                            ),
                            const SizedBox(height: Space.x2),
                            Wrap(
                              spacing: Space.x2,
                              runSpacing: Space.x2,
                              children: [
                                for (final c in ShareCategory.values)
                                  if (s.categories.contains(c))
                                    Chip(label: Text(c.displayName)),
                              ],
                            ),
                          ],
                        ),
                      ),
                  ],
                ),
        ),
      ],
    );
  }
}
