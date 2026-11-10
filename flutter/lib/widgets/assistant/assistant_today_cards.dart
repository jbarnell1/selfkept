import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../models/assistant.dart';
import '../../providers/assistant_today_provider.dart';
import '../../theme/tokens.dart';
import '../surface_card.dart';
import 'assistant_feature.dart';

/// The assistant's two cards at the top of Today: the morning brief, and
/// the evening check-in. Nothing shows on a server without the assistant.
class AssistantTodayCards extends StatefulWidget {
  /// The clock that decides whether it is evening. Tests pass a fixed one.
  final DateTime Function() now;

  const AssistantTodayCards({super.key, this.now = DateTime.now});

  /// From this hour the check-in card offers to close out the day.
  static const int eveningHour = 17;

  @override
  State<AssistantTodayCards> createState() => _AssistantTodayCardsState();
}

class _AssistantTodayCardsState extends State<AssistantTodayCards> {
  bool _asked = false;

  void _loadOnce() {
    if (_asked) return;
    _asked = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final today = context.read<AssistantTodayProvider>();
      if (!today.hasLoaded && !today.isLoading) today.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    if (!AssistantFeature.watch(context)) return const SizedBox.shrink();
    _loadOnce();
    final today = context.watch<AssistantTodayProvider>();
    final evening = widget.now().hour >= AssistantTodayCards.eveningHour;
    // Quiet while loading: both cards are extras, so nothing flashes.
    final cards = <Widget>[
      if (today.hasLoaded) ...[
        if (today.brief != null && !today.brief!.isEmpty)
          MorningBriefCard(brief: today.brief!)
        else if (today.briefError != null)
          const _QuietLine(MorningBriefCard.errorLine),
        if (today.checkin != null)
          ?CheckinCard.forState(today.checkin!, evening: evening)
        else if (today.checkinError != null && evening)
          const _QuietLine(CheckinCard.errorLine),
      ],
    ];
    if (cards.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (var i = 0; i < cards.length; i++) ...[
            if (i > 0) const SizedBox(height: Space.x3),
            cards[i],
          ],
        ],
      ),
    );
  }
}

/// A few lines from the record, each worded by the server.
class MorningBriefCard extends StatelessWidget {
  final MorningBrief brief;

  const MorningBriefCard({super.key, required this.brief});

  static const String title = 'Since yesterday';
  static const String errorLine = 'The morning brief did not load.';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: theme.textTheme.titleMedium),
          const SizedBox(height: Space.x3),
          for (final item in brief.items)
            Padding(
              padding: const EdgeInsets.only(bottom: Space.x2),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Padding(
                    padding: const EdgeInsets.only(top: 7),
                    child: Container(
                      width: 6,
                      height: 6,
                      decoration: BoxDecoration(
                        color: colors.band,
                        shape: BoxShape.circle,
                      ),
                    ),
                  ),
                  const SizedBox(width: Space.x3),
                  Expanded(
                    child: Text(item.text, style: theme.textTheme.bodyMedium),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

/// The evening check-in's card: "Close out today", or "Pick up where you
/// left off" with what is done and what is still to go.
class CheckinCard extends StatelessWidget {
  final CheckinToday state;

  const CheckinCard({super.key, required this.state});

  static const String startTitle = 'Close out today';
  static const String resumeTitle = 'Pick up where you left off';
  static const String doneLine = 'Today\'s check-in is done.';
  static const String startLabel = 'Start';
  static const String resumeLabel = 'Carry on';
  static const String errorLine = 'Tonight\'s check-in did not load.';

  /// The card for [state], or null when there is nothing to show: before
  /// the evening with nothing started, or with nothing missing.
  static Widget? forState(CheckinToday state, {required bool evening}) {
    switch (state.status) {
      case CheckinStatus.done:
        return evening ? const _QuietLine(doneLine) : null;
      case CheckinStatus.partial:
        return CheckinCard(state: state);
      case CheckinStatus.notStarted:
        if (!evening || state.missing.isEmpty) return null;
        return CheckinCard(state: state);
    }
  }

  static String _names(List<CheckinTopic> topics) =>
      topics.map((t) => t.displayName.toLowerCase()).join(', ');

  static String startLine(List<CheckinTopic> missing) =>
      'A minute or two about what today\'s log does not have yet: '
      '${_names(missing)}.';

  static String progressLine(CheckinToday state) {
    final covered = [...state.done, ...state.skipped];
    final left = [
      for (final t in state.missing)
        if (!covered.contains(t)) t,
    ];
    final parts = <String>[
      if (covered.isNotEmpty) 'Covered: ${_names(covered)}.',
      if (left.isNotEmpty) 'Still to go: ${_names(left)}.',
    ];
    return parts.isEmpty
        ? 'Everything you said so far is saved.'
        : parts.join(' ');
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final partial = state.status == CheckinStatus.partial;
    return SurfaceCard(
      onTap: () => context.push('/assistant/checkin'),
      child: Row(
        children: [
          Icon(Icons.nightlight_outlined, color: theme.colorScheme.primary),
          const SizedBox(width: Space.x4),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  partial ? resumeTitle : startTitle,
                  style: theme.textTheme.titleMedium,
                ),
                const SizedBox(height: Space.x1),
                Text(
                  partial ? progressLine(state) : startLine(state.missing),
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
          const SizedBox(width: Space.x3),
          FilledButton.tonal(
            onPressed: () => context.push('/assistant/checkin'),
            child: Text(partial ? resumeLabel : startLabel),
          ),
        ],
      ),
    );
  }
}

class _QuietLine extends StatelessWidget {
  final String text;

  const _QuietLine(this.text);

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: Space.x2),
      child: Text(text, style: theme.textTheme.bodySmall),
    );
  }
}
