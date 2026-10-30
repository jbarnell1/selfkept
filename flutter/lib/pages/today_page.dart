import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../models/checkin.dart';
import '../offline/sync_engine.dart';
import '../providers/auth_provider.dart';
import '../providers/checkins_provider.dart';
import '../providers/collector_provider.dart';
import '../providers/sources_provider.dart';
import '../providers/today_provider.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/relative_time.dart';
import '../widgets/check_in_entry.dart';
import '../widgets/insight_actions.dart';
import '../widgets/insight_card.dart';
import '../widgets/page_body.dart';
import '../widgets/readiness_row.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/dialogs.dart';

/// Today: the place a day begins. A greeting, the day's sentence against
/// your own baseline, three readiness chips, at most two cards, the check-in,
/// and where the readings come from.
class TodayPage extends StatefulWidget {
  const TodayPage({super.key});

  static const String chooseMoodMessage = 'Choose a mood first.';
  static const String keptMessage = 'Kept.';

  /// Under the sentence while baselines form. It adds what the sentence does
  /// not say (which readings get a range, and roughly when) instead of
  /// repeating "still learning".
  static const String learningMessage =
      'Sleep, HRV and resting heart rate get a usual range after about two weeks of readings.';
  static const String loadErrorTitle = 'Today did not load.';

  /// Up to this many characters the sentence gets the larger statement size.
  static const int shortSentence = 110;

  static TextStyle? heroStyle(
    ThemeData theme,
    String sentence, {
    required bool wide,
  }) {
    final short = sentence.length <= shortSentence;
    if (wide) {
      return short
          ? theme.textTheme.headlineLarge
          : theme.textTheme.headlineMedium;
    }
    return short
        ? theme.textTheme.headlineMedium
        : theme.textTheme.headlineSmall;
  }

  static String greetingFor(DateTime now) {
    final hour = now.hour;
    if (hour < 5) return 'Still up';
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    if (hour < 22) return 'Good evening';
    return 'Late evening';
  }

  @override
  State<TodayPage> createState() => _TodayPageState();
}

class _TodayPageState extends State<TodayPage> {
  /// Bumped after a successful keep so the entry starts fresh.
  int _entryGeneration = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final today = context.read<TodayProvider>();
      if (!today.hasLoaded && !today.isLoading) today.load();
      final checkins = context.read<CheckinsProvider>();
      if (!checkins.hasLoaded && !checkins.isLoading) checkins.load();
      final sources = context.read<SourcesProvider>();
      if (!sources.hasLoaded && !sources.isLoading) sources.load();
    });
  }

  Future<void> _refresh() async {
    await Future.wait([
      context.read<TodayProvider>().refresh(),
      context.read<CheckinsProvider>().load(),
      context.read<SourcesProvider>().load(),
    ]);
  }

  Future<void> _keep(Mood? mood, String note) async {
    final messenger = ScaffoldMessenger.of(context);
    if (mood == null) {
      showMessage(messenger, TodayPage.chooseMoodMessage);
      return;
    }
    final checkins = context.read<CheckinsProvider>();
    final ok = await checkins.create(
      mood: mood.index + Checkin.minMood,
      note: note,
    );
    if (!mounted) return;
    if (ok) {
      setState(() => _entryGeneration += 1);
    } else {
      final error = checkins.error;
      if (error != null) {
        showMessage(messenger, error);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final name = context.select<AuthProvider, String>(
      (a) => a.user?.person.displayName ?? a.user?.displayName ?? '',
    );
    final greeting = TodayPage.greetingFor(DateTime.now());
    final theme = Theme.of(context);
    return SafeArea(
      child: RefreshIndicator(
        onRefresh: _refresh,
        color: theme.colorScheme.primary,
        child: PageBody(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            Text(
              name.isEmpty
                  ? '$greeting.'
                  : '$greeting, ${name.split(' ').first}.',
              style: theme.textTheme.displaySmall,
            ),
            const SizedBox(height: Space.x4),
            const _Hero(),
            const SizedBox(height: Space.x6),
            const _Cards(),
            CheckInEntry(key: ValueKey(_entryGeneration), onKeep: _keep),
            const _LastCheckin(),
            const SizedBox(height: Space.x6),
            const _SourcesCard(),
          ],
        ),
      ),
    );
  }
}

/// The sentence, in the display face, and the readiness row beneath it.
/// While loading, a quiet spinner; on failure, one line and a way to retry.
class _Hero extends StatelessWidget {
  const _Hero();

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<TodayProvider>();
    final theme = Theme.of(context);
    final today = provider.today;
    final wide = MediaQuery.sizeOf(context).width >= 600;

    final Widget child;
    // Before the first load starts there is no data and no error yet: that is
    // loading too, not a failure (otherwise every open flashes the error).
    if (today == null && (provider.isLoading || provider.error == null)) {
      child = const Padding(
        key: ValueKey('loading'),
        padding: EdgeInsets.symmetric(vertical: Space.x8),
        child: LoadingView(),
      );
    } else if (today == null) {
      child = Column(
        key: const ValueKey('error'),
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(TodayPage.loadErrorTitle, style: theme.textTheme.headlineSmall),
          if (provider.error != null) ...[
            const SizedBox(height: Space.x2),
            Text(provider.error!, style: theme.textTheme.bodySmall),
          ],
          const SizedBox(height: Space.x4),
          Align(
            alignment: Alignment.centerLeft,
            child: OutlinedButton(
              onPressed: provider.load,
              child: const Text('Try again'),
            ),
          ),
        ],
      );
    } else {
      child = Column(
        key: ValueKey('data-${today.day}-${today.sentence}'),
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // The sentence is the point of the screen, so it is never cut off:
          // a longer one steps down a size instead.
          Text(
            today.sentence,
            style: TodayPage.heroStyle(theme, today.sentence, wide: wide),
          ),
          if (today.learning) ...[
            const SizedBox(height: Space.x3),
            Text(TodayPage.learningMessage, style: theme.textTheme.bodySmall),
          ],
          const SizedBox(height: Space.x6),
          ReadinessRow(
            readiness: today.readiness,
            sleepMinutes: today.sleepMinutes,
          ),
        ],
      );
    }

    return AnimatedSwitcher(
      duration: Motion.standard,
      switchInCurve: Motion.curve,
      switchOutCurve: Motion.reverseCurve,
      layoutBuilder: (current, previous) => Stack(
        alignment: Alignment.topLeft,
        children: [...previous, ?current],
      ),
      child: child,
    );
  }
}

/// Up to two cards. Reactions go through the insights provider so the
/// archive and Today agree at once.
class _Cards extends StatelessWidget {
  const _Cards();

  @override
  Widget build(BuildContext context) {
    final cards = context.watch<TodayProvider>().cards;
    if (cards.isEmpty) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final card in cards) ...[
          InsightCard(
            key: ValueKey('today-${card.id}'),
            insight: card,
            onReact: (r) => reactToInsight(context, card, r),
            onDismiss: () => dismissInsight(context, card),
          ),
          const SizedBox(height: Space.x3),
        ],
        const SizedBox(height: Space.x3),
      ],
    );
  }
}

/// The confirmation and the most recent check-in, under the entry.
class _LastCheckin extends StatelessWidget {
  const _LastCheckin();

  @override
  Widget build(BuildContext context) {
    final checkins = context.watch<CheckinsProvider>();
    final theme = Theme.of(context);
    final last = checkins.last;
    final String text;
    if (checkins.isSaving) {
      text = 'Keeping';
    } else if (checkins.justKept) {
      text = TodayPage.keptMessage;
    } else if (last != null) {
      final mood = Mood.fromInt(last.mood - Checkin.minMood).displayName;
      final note = last.note;
      text =
          'Last check-in: $mood, ${relativeTimeInSentence(last.at)}'
          '${note == null || note.isEmpty ? '.' : '. "$note"'}';
    } else if (!checkins.hasLoaded) {
      // Before the first load has answered: nothing, or why it failed.
      text = checkins.error == null ? '' : 'Check-ins did not load.';
    } else {
      text = 'No check-ins yet.';
    }
    return Padding(
      padding: const EdgeInsets.fromLTRB(Space.x2, Space.x3, Space.x2, 0),
      child: AnimatedSwitcher(
        duration: Motion.standard,
        switchInCurve: Motion.curve,
        child: Text(
          text,
          key: ValueKey(text),
          style: checkins.justKept
              ? theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.primary,
                )
              : theme.textTheme.bodySmall,
        ),
      ),
    );
  }
}

/// One card: when the readings last reached the server, and a way to the
/// Sources screen.
class _SourcesCard extends StatelessWidget {
  const _SourcesCard();

  @override
  Widget build(BuildContext context) {
    final sources = context.watch<SourcesProvider>();
    final sync = context.watch<SyncEngine>();
    final collector = context.watch<CollectorProvider>();
    final theme = Theme.of(context);

    final lastSync = sync.lastFlushAt ?? sources.lastSyncAt;
    final String detail;
    if (!sources.hasLoaded && sources.isLoading) {
      detail = 'Checking';
    } else if (!sources.hasLoaded && sources.error != null) {
      detail = 'Could not reach your sources.';
    } else if (sources.sources.isEmpty && !collector.isConnected) {
      detail = collector.status == CollectorStatus.unavailable
          ? 'Connect a phone to start collecting.'
          : 'Not connected yet.';
    } else {
      detail = 'Last sync ${relativeTimeInSentence(lastSync)}.';
    }

    return SurfaceCard(
      onTap: () => context.push('/sources'),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Sources', style: theme.textTheme.titleMedium),
                const SizedBox(height: Space.x1),
                Text(detail, style: theme.textTheme.bodySmall),
                if (sync.pendingCount > 0)
                  Text(
                    '${sync.pendingCount} waiting to send',
                    style: AppTheme.numeric(
                      context,
                      base: theme.textTheme.bodySmall,
                    ),
                  ),
                AttentionLine(sync.lastError ?? ''),
              ],
            ),
          ),
          const Icon(Icons.chevron_right),
        ],
      ),
    );
  }
}
