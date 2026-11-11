import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../models/retention.dart';
import '../providers/retention_provider.dart';
import '../theme/tokens.dart';
import 'dialogs.dart';
import 'page_body.dart';
import 'status_views.dart';
import 'surface_card.dart';

/// The household admin's choice of how long heart rate is kept reading by
/// reading before older months are folded into one row a minute
/// (docs/retention-design.md).
class RetentionCard extends StatefulWidget {
  /// The windows offered, in months; 0 keeps raw readings for ever.
  static const List<int> choices = [4, 6, 12, 24, 36, 60, 0];

  static const String explainer =
      'Older heart rate is kept as one reading a minute (average, lowest and '
      'highest). Daily values, sleep, HRV and everything else are kept in full.';
  static const String fromEnvironment =
      'Set in the server\'s .env file (SELFKEPT_RETENTION_MONTHS).';
  static const String nothingFolded = 'Nothing has been folded yet.';
  static const String runLabel = 'Fold old months now';

  /// The server feature that says it has retention (`/system/version`).
  static const String feature = 'data.retention';

  const RetentionCard({super.key});

  static String choiceLabel(int months) => switch (months) {
    0 => 'For ever',
    12 => '1 year',
    24 => '2 years',
    36 => '3 years',
    60 => '5 years',
    _ => '$months months',
  };

  static String summary(Retention r) {
    if (r.months.isEmpty) return nothingFolded;
    final oldest = r.months.last.month;
    final count = r.months.length;
    final label = DateFormat('MMMM yyyy').format(oldest);
    return count == 1
        ? '$label is folded.'
        : '$count months are folded, back to $label.';
  }

  @override
  State<RetentionCard> createState() => _RetentionCardState();
}

class _RetentionCardState extends State<RetentionCard> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final provider = context.read<RetentionProvider>();
      if (provider.retention == null && !provider.loading) provider.load();
    });
  }

  Future<void> _save(int months) async {
    final provider = context.read<RetentionProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final ok = await provider.save(months);
    if (!ok) showMessage(messenger, provider.error ?? 'That did not work.');
  }

  Future<void> _run() async {
    final provider = context.read<RetentionProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final result = await provider.run();
    if (result == null) {
      showMessage(messenger, provider.error ?? 'That did not work.');
    } else {
      showMessage(
        messenger,
        result.folded == 0
            ? 'Nothing old enough to fold.'
            : 'Folded ${result.folded} ${result.folded == 1 ? 'month' : 'months'}.',
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<RetentionProvider>();
    final theme = Theme.of(context);
    final retention = provider.retention;

    final Widget body;
    if (retention == null) {
      body = provider.error == null
          ? const LoadingView()
          : AttentionLine(provider.error ?? '');
    } else {
      final choices = {...RetentionCard.choices, retention.rawMonths}.toList();
      body = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'How long every heart rate reading is kept',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: Space.x2),
          Align(
            alignment: Alignment.centerLeft,
            child: DropdownButton<int>(
              key: const ValueKey('retention-months'),
              value: retention.rawMonths,
              onChanged: retention.fromEnvironment || provider.busy
                  ? null
                  : (v) {
                      if (v != null && v != retention.rawMonths) _save(v);
                    },
              items: [
                for (final m in choices)
                  DropdownMenuItem(
                    value: m,
                    child: Text(RetentionCard.choiceLabel(m)),
                  ),
              ],
            ),
          ),
          if (retention.fromEnvironment) ...[
            const SizedBox(height: Space.x2),
            Text(
              RetentionCard.fromEnvironment,
              style: theme.textTheme.bodySmall,
            ),
          ],
          const SizedBox(height: Space.x3),
          Text(RetentionCard.explainer, style: theme.textTheme.bodySmall),
          const SizedBox(height: Space.x3),
          Text(
            RetentionCard.summary(retention),
            style: theme.textTheme.bodySmall,
          ),
          if (!retention.keepsForever) ...[
            const SizedBox(height: Space.x3),
            Align(
              alignment: Alignment.centerLeft,
              child: OutlinedButton.icon(
                onPressed: provider.busy ? null : _run,
                icon: const Icon(Icons.compress, size: 18),
                label: const Text(RetentionCard.runLabel),
              ),
            ),
          ],
        ],
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader(title: 'Readings kept'),
        SurfaceCard(child: body),
      ],
    );
  }
}
