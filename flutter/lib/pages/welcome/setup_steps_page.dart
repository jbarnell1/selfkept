import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../models/setup.dart';
import '../../providers/auth_provider.dart';
import '../../providers/collector_provider.dart';
import '../../providers/connect_provider.dart';
import '../../providers/household_provider.dart';
import '../../providers/labs_provider.dart';
import '../../providers/oura_provider.dart';
import '../../providers/setup_provider.dart';
import '../../providers/sources_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import 'steps/oura_step.dart';
import 'steps/phone_step.dart';
import 'steps/simple_steps.dart';

/// The steps for what the person said they use, one card each. Every step
/// works on its own and can be left for later; this page is also reachable
/// from You.
class SetupStepsPage extends StatefulWidget {
  /// The `oura` query value Oura's round trip leaves behind.
  final String? ouraResult;

  const SetupStepsPage({super.key, this.ouraResult});

  @override
  State<SetupStepsPage> createState() => _SetupStepsPageState();
}

class _SetupStepsPageState extends State<SetupStepsPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      _loadFor(context.read<AuthProvider>().user?.setupPlan ?? const []);
      if (widget.ouraResult == 'error') {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Oura did not connect. Try again.')),
        );
      }
    });
  }

  /// Loads only what the chosen steps show.
  void _loadFor(List<SetupItem> plan) {
    final has = plan.contains;
    if (has(SetupItem.appleHealth) || has(SetupItem.healthConnect)) {
      final sources = context.read<SourcesProvider>();
      if (!sources.isLoading) sources.load();
      final collector = context.read<CollectorProvider>();
      if (collector.status == CollectorStatus.unknown) collector.init();
    }
    if (has(SetupItem.oura)) {
      final oura = context.read<OuraProvider>();
      if (!oura.loading) oura.load();
      final household = context.read<HouseholdProvider>();
      if (!household.hasLoaded && !household.isLoading) household.load();
    }
    if (has(SetupItem.labs)) {
      final labs = context.read<LabsProvider>();
      if (!labs.loading) labs.load();
    }
    if (has(SetupItem.household)) {
      final household = context.read<HouseholdProvider>();
      if (!household.isLoading) household.load();
    }
    if (has(SetupItem.assistant)) {
      final connect = context.read<ConnectProvider>();
      if (!connect.loading) connect.load();
    }
  }

  Future<void> _finish() async {
    final router = GoRouter.of(context);
    final pending = context.read<AuthProvider>().user?.setupPending ?? false;
    if (!pending || await context.read<SetupProvider>().finish()) {
      router.go('/');
    }
  }

  @override
  Widget build(BuildContext context) {
    final plan = context.select<AuthProvider, List<SetupItem>>(
      (a) => a.user?.setupPlan ?? const [],
    );
    final setup = context.watch<SetupProvider>();
    final pending = context.select<AuthProvider, bool>(
      (a) => a.user?.setupPending ?? false,
    );

    return Scaffold(
      body: SafeArea(
        child: PageBody(
          children: [
            const PageHeading(
              title: 'Your setup',
              subtitle:
                  'One card for each thing you use. Each works on its own, so '
                  'do them now or come back to them from You.',
            ),
            if (plan.isEmpty)
              const Padding(
                padding: EdgeInsets.only(bottom: Space.x4),
                child: Text(
                  'Nothing is picked yet. Change your answers to add a step.',
                ),
              ),
            for (final item in plan) _stepFor(item),
            AttentionLine(setup.error ?? ''),
            const SizedBox(height: Space.x4),
            Wrap(
              spacing: Space.x3,
              runSpacing: Space.x3,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                FilledButton(
                  onPressed: setup.saving ? null : _finish,
                  child: Text(pending ? 'Go to Today' : 'Done'),
                ),
                TextButton(
                  onPressed: () => context.go('/welcome'),
                  child: const Text('Change my answers'),
                ),
              ],
            ),
            const SizedBox(height: Space.x8),
          ],
        ),
      ),
    );
  }

  static Widget _stepFor(SetupItem item) => switch (item) {
    SetupItem.appleHealth ||
    SetupItem.healthConnect => PhoneStep(key: ValueKey(item), item: item),
    SetupItem.oura => const OuraStep(),
    SetupItem.labs => const LabsStep(),
    SetupItem.household => const HouseholdStep(),
    SetupItem.assistant => const AssistantStep(),
  };
}
