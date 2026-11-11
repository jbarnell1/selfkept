import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../providers/household_provider.dart';
import '../../providers/profile_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/page_body.dart';
import '../../widgets/surface_card.dart';
import 'account_section.dart';
import 'assistant_section.dart';
import 'assistants_section.dart';
import 'household_section.dart';
import 'profile_section.dart';
import 'server_section.dart';
import 'sharing_section.dart';
import 'sources_section.dart';

/// You: profile, goals and medicines, household, sharing, server and about.
class YouPage extends StatefulWidget {
  /// The `sso` query a provider trip lands back with (Connect under Account).
  final String? ssoResult;

  const YouPage({super.key, this.ssoResult});

  @override
  State<YouPage> createState() => _YouPageState();
}

class _YouPageState extends State<YouPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final household = context.read<HouseholdProvider>();
      if (!household.hasLoaded && !household.isLoading) household.load();
      final profile = context.read<ProfileProvider>();
      if (!profile.isLoading) profile.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: PageBody(
        children: [
          const PageHeading(title: 'You'),
          const ProfileSection(),
          const AboutYouSection(),
          const AssistantYouSection(),
          const SourcesSection(),
          const HouseholdSection(),
          const SharingSection(),
          const AssistantsSection(),
          const SharedWithMeSection(),
          const ServerSection(),
          AccountSection(ssoResult: widget.ssoResult),
          const AboutSection(),
        ],
      ),
    );
  }
}

/// The ways into Goals and into Medicines and supplements.
class AboutYouSection extends StatelessWidget {
  const AboutYouSection({super.key});

  static const String goalsTitle = 'Goals and targets';
  static const String goalsLine =
      'What you are working towards, and what to do each day.';
  static const String medicinesTitle = 'Medicines and supplements';
  static const String medicinesLine =
      'What you take, so readings and blood work make sense.';

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'About you'),
        _LinkCard(
          icon: Icons.flag_outlined,
          title: goalsTitle,
          line: goalsLine,
          onTap: () => context.push('/you/goals'),
        ),
        const SizedBox(height: Space.x3),
        _LinkCard(
          icon: Icons.medication_outlined,
          title: medicinesTitle,
          line: medicinesLine,
          onTap: () => context.push('/you/medicines'),
        ),
      ],
    );
  }
}

class _LinkCard extends StatelessWidget {
  final IconData icon;
  final String title;
  final String line;
  final VoidCallback onTap;

  const _LinkCard({
    required this.icon,
    required this.title,
    required this.line,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Semantics(
      button: true,
      child: SurfaceCard(
        onTap: onTap,
        child: Row(
          children: [
            Icon(icon, color: theme.colorScheme.primary),
            const SizedBox(width: Space.x4),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: theme.textTheme.titleMedium),
                  const SizedBox(height: Space.x1),
                  Text(line, style: theme.textTheme.bodySmall),
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
