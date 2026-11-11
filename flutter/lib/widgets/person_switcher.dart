import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../providers/household_provider.dart';
import '../providers/viewed_person.dart';
import '../theme/tokens.dart';

/// "You" and everyone who shares with you, as a row of chips. Nothing at all
/// when no one shares, so most households never see it.
class PersonSwitcher extends StatefulWidget {
  const PersonSwitcher({super.key});

  static const String selfLabel = 'You';

  /// Under the chips while someone else is shown.
  static String readOnlyLine(String name) =>
      'You are looking at $name\'s record. Only they can change it.';

  @override
  State<PersonSwitcher> createState() => _PersonSwitcherState();
}

class _PersonSwitcherState extends State<PersonSwitcher> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final household = context.read<HouseholdProvider>();
      if (!household.hasLoaded && !household.isLoading) household.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final shared = context.watch<HouseholdProvider>().sharedWithMe;
    final viewing = context.watch<ViewedPerson>();
    final theme = Theme.of(context);
    if (shared.isEmpty && viewing.isSelf) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: Space.x2,
            runSpacing: Space.x2,
            children: [
              ChoiceChip(
                label: const Text(PersonSwitcher.selfLabel),
                selected: viewing.isSelf,
                onSelected: (_) => viewing.viewSelf(),
              ),
              for (final person in shared)
                ChoiceChip(
                  label: Text(person.displayName),
                  selected: viewing.personId == person.personId,
                  onSelected: (_) => viewing.view(
                    person.personId,
                    displayName: person.displayName,
                  ),
                ),
            ],
          ),
          if (!viewing.isSelf) ...[
            const SizedBox(height: Space.x2),
            Text(
              PersonSwitcher.readOnlyLine(viewing.displayName),
              style: theme.textTheme.bodySmall,
            ),
          ],
        ],
      ),
    );
  }
}
