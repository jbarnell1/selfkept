import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../models/lab.dart';
import '../../models/systems.dart';
import '../../providers/systems_provider.dart';
import '../../providers/viewed_person.dart';
import '../../theme/app_theme.dart';
import '../../theme/tokens.dart';
import '../../utils/metric_format.dart';
import '../../widgets/insight_card.dart';
import '../../widgets/page_body.dart';
import '../../widgets/person_switcher.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';
import '../../widgets/trend_sparkline.dart';
import '../systems_page.dart';
import 'systems_common.dart';

/// One body system: the server's sentence, each reading against your own
/// usual with its last 28 days, the lab results that belong to it, and its
/// cards. Readings and results open their own pages for your own record.
class SystemDetailPage extends StatefulWidget {
  final String systemKey;

  const SystemDetailPage({super.key, required this.systemKey});

  static const String readingsTitle = 'Readings';
  static const String labsTitle = 'Lab results';
  static const String cardsTitle = 'Cards';
  static const String noUsualText = 'No usual range yet';
  static const String loadErrorTitle = 'This system did not load.';
  static const String emptyLine = 'Nothing recorded for this system yet.';

  /// "Your usual 56.5 to 58.5 bpm", from the server's own range.
  static String usualWords(SystemReading reading) {
    final usual = reading.usual;
    if (usual == null) return noUsualText;
    final low = formatMetricValue(
      reading.metric,
      usual.p25,
      unit: reading.unit,
    );
    final high = formatMetricWithUnit(
      reading.metric,
      usual.p75,
      unit: reading.unit,
    );
    return 'Your usual $low to $high';
  }

  /// "High", "Low" or null for a lab flag.
  static String? flagWord(String flag) => switch (flag.toLowerCase()) {
    'high' => 'High',
    'low' => 'Low',
    _ => null,
  };

  @override
  State<SystemDetailPage> createState() => _SystemDetailPageState();
}

class _SystemDetailPageState extends State<SystemDetailPage> {
  ViewedPerson? _viewing;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      _viewing = context.read<ViewedPerson>()..addListener(_load);
      _load();
    });
  }

  @override
  void dispose() {
    _viewing?.removeListener(_load);
    super.dispose();
  }

  Future<void> _load() async {
    if (!mounted) return;
    await context.read<SystemsProvider>().loadSystem(widget.systemKey);
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<SystemsProvider>();
    final viewing = context.watch<ViewedPerson>();
    final key = widget.systemKey;
    final system = provider.detail(key);
    final title = system?.name ?? provider.summary(key)?.name ?? 'Systems';

    final List<Widget> body;
    if (provider.detailNotShared(key)) {
      body = [
        NotSharedView(name: viewing.displayName, onBack: viewing.viewSelf),
      ];
    } else if (system == null && provider.detailError(key) == null) {
      body = const [
        Padding(
          padding: EdgeInsets.symmetric(vertical: Space.x8),
          child: LoadingView(),
        ),
      ];
    } else if (system == null) {
      body = [
        ErrorView(
          title: SystemDetailPage.loadErrorTitle,
          detail: provider.detailError(key),
          onRetry: _load,
        ),
      ];
    } else {
      body = _sections(context, system, viewing.isSelf);
    }

    return SubpageFrame(
      onRefresh: _load,
      children: [
        PageHeading(title: title),
        const PersonSwitcher(),
        ...body,
      ],
    );
  }

  List<Widget> _sections(BuildContext context, BodySystem system, bool self) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final key = system.key;
    return [
      if (system.state.isNotEmpty)
        Text(system.state, style: theme.textTheme.headlineSmall),
      if (system.readings.isEmpty &&
          system.labs.isEmpty &&
          system.insights.isEmpty) ...[
        const SizedBox(height: Space.x4),
        const QuietLine(SystemDetailPage.emptyLine),
      ],
      if (system.readings.isNotEmpty) ...[
        const SectionHeader(title: SystemDetailPage.readingsTitle),
        SurfaceCard(
          padding: EdgeInsets.zero,
          child: Column(
            children: [
              for (var i = 0; i < system.readings.length; i++) ...[
                if (i > 0)
                  Divider(height: 1, indent: Space.x4, color: colors.hairline),
                _ReadingRow(reading: system.readings[i], canOpen: self),
              ],
            ],
          ),
        ),
      ],
      if (system.labs.isNotEmpty) ...[
        const SectionHeader(title: SystemDetailPage.labsTitle),
        SurfaceCard(
          padding: EdgeInsets.zero,
          child: Column(
            children: [
              for (var i = 0; i < system.labs.length; i++) ...[
                if (i > 0)
                  Divider(height: 1, indent: Space.x4, color: colors.hairline),
                _LabRow(lab: system.labs[i], canOpen: self),
              ],
            ],
          ),
        ),
      ],
      if (system.insights.isNotEmpty) ...[
        const SectionHeader(title: SystemDetailPage.cardsTitle),
        for (final card in system.insights) ...[
          InsightCard(insight: card, animateIn: false),
          const SizedBox(height: Space.x3),
        ],
      ],
      const SizedBox(height: Space.x6),
      if (key == 'sleep')
        LinkCard(
          icon: Icons.bedtime_outlined,
          title: SystemsPage.sleepTitle,
          line: SystemsPage.sleepLine,
          onTap: () => context.push('/systems/sleep'),
        ),
      if (key == 'heart' || key == 'body')
        LinkCard(
          icon: Icons.directions_run_outlined,
          title: SystemsPage.workoutsTitle,
          line: SystemsPage.workoutsLine,
          onTap: () => context.push('/systems/workouts'),
        ),
      if (system.labs.isNotEmpty)
        LinkCard(
          icon: Icons.science_outlined,
          title: 'Labs',
          line: SystemsPage.labsLine,
          onTap: () => context.push(self ? '/labs' : '/labs/timeline'),
        ),
    ];
  }
}

class _ReadingRow extends StatelessWidget {
  final SystemReading reading;
  final bool canOpen;

  const _ReadingRow({required this.reading, required this.canOpen});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final latest = reading.latestValue;
    final value = latest == null
        ? ''
        : formatMetricWithUnit(reading.metric, latest, unit: reading.unit);
    final band = reading.band;
    final outside = reading.isOutside;
    final status = outside && band != null
        ? '${band.displayName[0].toUpperCase()}${band.displayName.substring(1)}'
        : SystemDetailPage.usualWords(reading);
    final row = Padding(
      padding: const EdgeInsets.fromLTRB(
        Space.x4,
        Space.x3,
        Space.x2,
        Space.x3,
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(reading.displayName, style: theme.textTheme.titleSmall),
                const SizedBox(height: Space.x1),
                Text(
                  status,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: outside
                        ? theme.colorScheme.error
                        : theme.colorScheme.onSurfaceVariant,
                  ),
                ),
                if (outside)
                  Text(
                    SystemDetailPage.usualWords(reading),
                    style: theme.textTheme.bodySmall,
                  ),
              ],
            ),
          ),
          const SizedBox(width: Space.x3),
          TrendSparkline(
            points: reading.recent,
            range: reading.usual?.band,
            outside: outside,
          ),
          const SizedBox(width: Space.x3),
          // At least the width of "58 bpm" so values line up, wider for a
          // long unit such as "15 breaths/min", which stays on one line.
          ConstrainedBox(
            constraints: const BoxConstraints(minWidth: 76),
            child: Text(
              value,
              key: ValueKey('reading-value-${reading.metric}'),
              textAlign: TextAlign.right,
              softWrap: false,
              style: AppTheme.numeric(
                context,
                base: theme.textTheme.titleSmall,
              ),
            ),
          ),
          if (canOpen)
            Icon(
              Icons.chevron_right,
              size: 20,
              color: theme.colorScheme.onSurfaceVariant,
            )
          else
            const SizedBox(width: Space.x2),
        ],
      ),
    );
    return Semantics(
      button: canOpen,
      label: '${reading.displayName}, $value, $status',
      excludeSemantics: true,
      child: canOpen
          ? InkWell(
              onTap: () => context.push('/trends/${reading.metric}'),
              child: row,
            )
          : row,
    );
  }
}

class _LabRow extends StatelessWidget {
  final SystemLab lab;
  final bool canOpen;

  const _LabRow({required this.lab, required this.canOpen});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final unit = lab.displayUnit;
    String withUnit(double v) =>
        unit.isEmpty ? formatLabValue(v) : '${formatLabValue(v)} $unit';
    final flag = SystemDetailPage.flagWord(lab.flag);
    final previous = lab.previousValue;
    final history = lab.history;
    final row = Padding(
      padding: const EdgeInsets.fromLTRB(
        Space.x4,
        Space.x3,
        Space.x2,
        Space.x3,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(lab.displayName, style: theme.textTheme.titleSmall),
                const SizedBox(height: Space.x1),
                Text(
                  flag == null
                      ? spokenDay(lab.collectedOn)
                      : '${spokenDay(lab.collectedOn)} · $flag',
                  style: theme.textTheme.bodySmall,
                ),
                if (previous != null && lab.previousOn != null)
                  Text(
                    'Before: ${withUnit(previous)} on ${spokenDay(lab.previousOn!)}',
                    style: theme.textTheme.bodySmall,
                  ),
                if (history.length > 1)
                  Padding(
                    padding: const EdgeInsets.only(top: Space.x1),
                    child: Text(
                      [
                        for (final p in history)
                          '${shortDay(p.collectedOn)}: ${formatLabValue(p.value)}',
                      ].join(' · '),
                      style: AppTheme.numeric(
                        context,
                        base: theme.textTheme.bodySmall,
                      ),
                    ),
                  ),
              ],
            ),
          ),
          const SizedBox(width: Space.x3),
          Text(
            withUnit(lab.value),
            style: AppTheme.numeric(context, base: theme.textTheme.titleSmall),
          ),
          if (canOpen)
            Icon(
              Icons.chevron_right,
              size: 20,
              color: theme.colorScheme.onSurfaceVariant,
            )
          else
            const SizedBox(width: Space.x2),
        ],
      ),
    );
    return canOpen
        ? InkWell(
            onTap: () => context.push('/labs/marker/${lab.metric}'),
            child: row,
          )
        : row;
  }
}
