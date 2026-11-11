import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/context.dart';
import '../../providers/systems_provider.dart';
import '../../theme/tokens.dart';
import '../../utils/metric_format.dart';
import '../../widgets/page_body.dart';
import '../../widgets/person_switcher.dart';
import '../../widgets/surface_card.dart';
import 'systems_common.dart';

/// Your patterns: nights after a tag in the day log against the rest, each
/// in the server's own words. Tags without enough nights yet are listed as
/// building, with their counts and nothing more.
class PatternsPage extends StatefulWidget {
  const PatternsPage({super.key});

  static const String title = 'Your patterns';
  static const String buildingTitle = 'Not enough nights yet';
  static const String emptyLine =
      'No patterns yet. Tag your days in the day log and patterns appear here once there are enough nights.';
  static const String loadErrorTitle = 'Patterns did not load.';

  /// "9 nights with, 31 without".
  static String counts(int withTag, int without) =>
      '${_nights(withTag)} with, $without without';

  /// The floor the server sent, said once above the building list.
  static String floorLine(Patterns p) =>
      'A pattern needs at least ${_nights(p.minNightsWith)} with the tag and '
      '${p.minNightsWithout} without, in the last ${p.windowDays} days.';

  static String _nights(int n) => n == 1 ? '1 night' : '$n nights';

  @override
  State<PatternsPage> createState() => _PatternsPageState();
}

class _PatternsPageState extends State<PatternsPage> {
  @override
  void initState() {
    super.initState();
    loadAfterFirstFrame(this, () => context.read<PatternsProvider>());
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<PatternsProvider>();
    final note = provider.data?.note ?? '';
    return SubpageFrame(
      onRefresh: provider.load,
      children: [
        PageHeading(
          title: PatternsPage.title,
          subtitle: note.isEmpty ? null : note,
        ),
        const PersonSwitcher(),
        ...personReadBody(
          context,
          provider,
          errorTitle: PatternsPage.loadErrorTitle,
          builder: (p) => _body(context, p),
        ),
      ],
    );
  }

  List<Widget> _body(BuildContext context, Patterns p) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    if (p.isEmpty) return const [QuietLine(PatternsPage.emptyLine)];
    return [
      for (final pattern in p.patterns)
        Padding(
          padding: const EdgeInsets.only(bottom: Space.x3),
          child: SurfaceCard(
            key: ValueKey('pattern-${pattern.tag}-${pattern.metric}'),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${pattern.tagLabel} and ${nameInSentence(pattern.metricName)}',
                  style: theme.textTheme.titleMedium,
                ),
                const SizedBox(height: Space.x2),
                Text(pattern.sentence, style: theme.textTheme.bodyLarge),
                const SizedBox(height: Space.x2),
                Text(
                  PatternsPage.counts(
                    pattern.nightsWith,
                    pattern.nightsWithout,
                  ),
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
        ),
      if (p.building.isNotEmpty) ...[
        const SectionHeader(title: PatternsPage.buildingTitle),
        if (p.minNightsWith > 0) ...[
          QuietLine(PatternsPage.floorLine(p)),
          const SizedBox(height: Space.x3),
        ],
        SurfaceCard(
          padding: const EdgeInsets.symmetric(
            horizontal: Space.x4,
            vertical: Space.x2,
          ),
          child: Column(
            children: [
              for (var i = 0; i < p.building.length; i++) ...[
                if (i > 0) Divider(height: 1, color: colors.hairline),
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: Space.x3),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        p.building[i].tagLabel,
                        style: theme.textTheme.titleSmall,
                      ),
                      Text(
                        PatternsPage.counts(
                          p.building[i].nightsWith,
                          p.building[i].nightsWithout,
                        ),
                        style: theme.textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
              ],
            ],
          ),
        ),
      ],
    ];
  }
}
