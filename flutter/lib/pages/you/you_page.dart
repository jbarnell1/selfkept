import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../providers/household_provider.dart';
import '../../providers/profile_provider.dart';
import '../../widgets/page_body.dart';
import 'account_section.dart';
import 'assistants_section.dart';
import 'household_section.dart';
import 'profile_section.dart';
import 'server_section.dart';
import 'sharing_section.dart';
import 'sources_section.dart';

/// You: profile, household, sharing, server and about.
class YouPage extends StatefulWidget {
  const YouPage({super.key});

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
        children: const [
          PageHeading(title: 'You'),
          ProfileSection(),
          SourcesSection(),
          HouseholdSection(),
          SharingSection(),
          AssistantsSection(),
          SharedWithMeSection(),
          ServerSection(),
          AccountSection(),
          AboutSection(),
        ],
      ),
    );
  }
}
