import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../models/setup.dart';
import '../../../providers/auth_provider.dart';
import '../../../providers/household_provider.dart';
import '../../../providers/oura_provider.dart';
import '../../../theme/tokens.dart';
import '../../../utils/relative_time.dart';
import '../../../widgets/oura_app_form.dart';
import '../../../widgets/setup_step_card.dart';
import '../../../widgets/status_views.dart';
import '../setup_icons.dart';

/// Oura: the admin sets up the server's Oura application once, then each
/// person connects their own ring.
class OuraStep extends StatelessWidget {
  /// Where Oura sends the browser back to.
  static const String returnPath = '/welcome/steps';

  const OuraStep({super.key});

  @override
  Widget build(BuildContext context) {
    final oura = context.watch<OuraProvider>();
    final isAdmin = context.select<AuthProvider, bool>((a) => a.isAdmin);
    // Watched: the household loads after this card first builds.
    final household = context.watch<HouseholdProvider>();
    final theme = Theme.of(context);
    final status = oura.status;

    final Widget body;
    if (status == null) {
      body = oura.error == null
          ? const LoadingView()
          : Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                AttentionLine(oura.error ?? ''),
                TextButton(
                  onPressed: oura.load,
                  child: const Text('Check again'),
                ),
              ],
            );
    } else if (status.connected) {
      body = Text(
        '${status.sampleCount} ${status.sampleCount == 1 ? 'reading' : 'readings'} so far. '
        '${status.lastPullAt == null ? 'The first pull is under way.' : 'Last pulled ${relativeTimeInSentence(status.lastPullAt)}.'}',
        style: theme.textTheme.bodyMedium,
      );
    } else if (status.configured) {
      body = Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Sign in to Oura and allow it. Readiness, sleep and the rest come '
            'in every 15 minutes after that.',
          ),
          const SizedBox(height: Space.x3),
          FilledButton.tonal(
            onPressed: () => oura.connect(returnTo: returnPath),
            child: const Text('Connect Oura'),
          ),
          AttentionLine(oura.error ?? ''),
        ],
      );
    } else if (isAdmin) {
      body = OuraAppForm(onChanged: oura.load);
    } else {
      body = Text(
        'Oura needs a one-time setup by ${_adminName(household)}, who runs this '
        'server. Once it is done, connect your ring here or under Sources.',
        style: theme.textTheme.bodyMedium,
      );
    }

    return SetupStepCard(
      icon: setupItemIcon(SetupItem.oura),
      title: 'Oura ring',
      done: status?.connected ?? false,
      doneLabel: 'Connected',
      child: body,
    );
  }

  static String _adminName(HouseholdProvider household) {
    final members = household.household?.members ?? const [];
    final admin = members.where((m) => m.isAdmin).firstOrNull;
    return admin == null || admin.displayName.isEmpty
        ? 'the household admin'
        : admin.displayName;
  }
}
