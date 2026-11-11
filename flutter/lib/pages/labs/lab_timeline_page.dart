import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../models/lab_review.dart';
import '../../models/medication.dart';
import '../../providers/systems_provider.dart';
import '../../theme/tokens.dart';
import '../../utils/body_units.dart';
import '../../utils/metric_format.dart';
import '../../widgets/page_body.dart';
import '../../widgets/person_switcher.dart';
import '../../widgets/surface_card.dart';
import '../../widgets/viewer_units.dart';
import '../systems/systems_common.dart';

/// Every panel oldest first: how it was drawn, the substances taken on the
/// day and the weight nearest it. Each panel opens its review.
class LabTimelinePage extends StatefulWidget {
  const LabTimelinePage({super.key});

  static const String title = 'Lab timeline';
  static const String emptyLine = 'No panels yet.';
  static const String loadErrorTitle = 'The timeline did not load.';
  static const String weightNotShared = 'Weight is not shared with you.';

  /// "Fasting, morning" or "Not fasting, afternoon at 15:40".
  static String drawLine(DrawConditions c) {
    final parts = <String>[
      if (c.fasting != DrawFasting.unknown) c.fasting.displayName,
      if (c.drawTime != null)
        '${c.drawPeriod.displayName.toLowerCase()} at ${c.drawTime}'
      else if (c.drawPeriod != DrawPeriod.unknown)
        c.drawPeriod.displayName.toLowerCase(),
    ];
    if (parts.isEmpty) return 'How it was drawn is not known';
    final text = parts.join(', ');
    return text[0].toUpperCase() + text.substring(1);
  }

  @override
  State<LabTimelinePage> createState() => _LabTimelinePageState();
}

class _LabTimelinePageState extends State<LabTimelinePage> {
  @override
  void initState() {
    super.initState();
    loadAfterFirstFrame(this, () => context.read<LabTimelineProvider>());
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<LabTimelineProvider>();
    final theme = Theme.of(context);
    final units = viewerUnitsOf(context);
    return SubpageFrame(
      fallback: '/labs',
      backTitle: 'Labs',
      onRefresh: provider.load,
      children: [
        const PageHeading(title: LabTimelinePage.title),
        const PersonSwitcher(),
        ...personReadBody(
          context,
          provider,
          errorTitle: LabTimelinePage.loadErrorTitle,
          builder: (t) {
            if (t.panels.isEmpty) {
              return const [QuietLine(LabTimelinePage.emptyLine)];
            }
            return [
              if (!t.weightShared) ...[
                const QuietLine(LabTimelinePage.weightNotShared),
                const SizedBox(height: Space.x3),
              ],
              for (final p in t.panels)
                Padding(
                  padding: const EdgeInsets.only(bottom: Space.x3),
                  child: Semantics(
                    button: true,
                    child: SurfaceCard(
                      key: ValueKey('panel-${p.reportId}'),
                      padding: const EdgeInsets.all(Space.x4),
                      onTap: () => context.push('/labs/report/${p.reportId}'),
                      child: Row(
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  spokenDay(p.collectedOn),
                                  style: theme.textTheme.titleSmall,
                                ),
                                Text(
                                  '${p.providerLabel} · '
                                  '${p.markerCount == 1 ? '1 result' : '${p.markerCount} results'}',
                                  style: theme.textTheme.bodySmall,
                                ),
                                Text(
                                  LabTimelinePage.drawLine(p.conditions),
                                  style: theme.textTheme.bodyMedium,
                                ),
                                if (p.substances.isNotEmpty)
                                  Text(
                                    'Taking: ${p.substances.map((s) => nameInSentence(Medication.substanceLabel(s))).join(', ')}',
                                    style: theme.textTheme.bodySmall,
                                  ),
                                if (p.weight != null)
                                  Text(
                                    'Weight ${BodyUnits.formatMass(p.weight!.kg, units)} '
                                    'on ${shortDay(p.weight!.day)}',
                                    style: theme.textTheme.bodySmall,
                                  ),
                              ],
                            ),
                          ),
                          const Icon(Icons.chevron_right),
                        ],
                      ),
                    ),
                  ),
                ),
            ];
          },
        ),
      ],
    );
  }
}
