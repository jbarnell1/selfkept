import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../models/enums.dart';
import '../models/lab.dart';
import '../providers/labs_provider.dart';
import '../providers/profile_provider.dart';
import '../theme/tokens.dart';
import '../widgets/clinician_summary_button.dart';
import '../widgets/lab_marker_tile.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/dialogs.dart';

/// Labs: every marker as a small tile against its usual range, then the
/// panels behind them.
class LabsPage extends StatefulWidget {
  const LabsPage({super.key});

  static const String emptySentence = 'Your first panel will land here.';
  static const String emptyNote =
      'Add a photo of your results, or type them in.';
  static const String addLabel = 'Add results';
  static const String sexCardText =
      'Some ranges depend on sex at birth. Set it under You to see the right ones.';
  static const String deleteTitle = 'Delete this panel?';
  static const String deleteBody = 'Its results leave your trends too.';

  /// Columns for a content width: 2 under 600, 3 under 1000, 4 above.
  static int columnsFor(double width) =>
      width < 600 ? 2 : (width < 1000 ? 3 : 4);

  @override
  State<LabsPage> createState() => _LabsPageState();
}

class _LabsPageState extends State<LabsPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final labs = context.read<LabsProvider>();
      if (!labs.hasLoaded && !labs.loading) labs.load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final labs = context.watch<LabsProvider>();
    final person = context.watch<ProfileProvider>().person;
    final theme = Theme.of(context);

    final loadingFirst = !labs.hasLoaded && labs.error == null;
    final failed = !labs.hasLoaded && labs.error != null;
    final empty = labs.hasLoaded && !labs.hasAnyResults;

    final List<Widget> body;
    if (loadingFirst) {
      body = const [
        Padding(
          padding: EdgeInsets.symmetric(vertical: Space.x8),
          child: LoadingView(),
        ),
      ];
    } else if (failed) {
      body = [
        ErrorView(
          title: 'Labs did not load.',
          detail: labs.error,
          onRetry: labs.load,
        ),
      ];
    } else if (empty) {
      body = [const _EmptyState()];
    } else {
      body = [
        for (final group in LabGroup.values)
          ..._groupSection(context, labs.markers, group),
        if (labs.reports.isNotEmpty) _Panels(reports: labs.reports),
        const SizedBox(height: Space.x6),
        const _Narrow(child: ClinicianSummaryButton()),
      ];
    }

    return SafeArea(
      child: RefreshIndicator(
        onRefresh: labs.refresh,
        color: theme.colorScheme.primary,
        child: PageBody(
          maxWidth: 1200,
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            _Header(showAdd: !empty && !failed && !loadingFirst),
            if (person != null && person.sexAtBirth == SexAtBirth.unspecified)
              const _SexCard(),
            ...body,
          ],
        ),
      ),
    );
  }

  List<Widget> _groupSection(
    BuildContext context,
    List<LabMarker> all,
    LabGroup group,
  ) {
    final markers = [
      for (final m in all)
        if (m.applicable && m.group == group) m,
    ];
    if (markers.isEmpty) return const [];
    final theme = Theme.of(context);
    final measured = [
      for (final m in markers)
        if (m.hasValue) m,
    ];
    final waiting = [
      for (final m in markers)
        if (!m.hasValue) m,
    ];
    return [
      Padding(
        padding: const EdgeInsets.only(top: Space.x6, bottom: Space.x3),
        child: Text(
          group.displayName,
          style: theme.textTheme.labelLarge?.copyWith(
            color: theme.colorScheme.secondary,
          ),
        ),
      ),
      if (measured.isNotEmpty) _MarkerGrid(markers: measured),
      if (waiting.isNotEmpty) _NotYetRow(markers: waiting),
    ];
  }
}

/// The group's markers with no result yet, as one quiet line of names
/// rather than a row of empty cards. Each name still opens its page.
class _NotYetRow extends StatelessWidget {
  final List<LabMarker> markers;

  const _NotYetRow({required this.markers});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final quiet = theme.textTheme.bodySmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    return Padding(
      padding: const EdgeInsets.only(top: Space.x3),
      child: Wrap(
        spacing: Space.x1,
        runSpacing: Space.x1,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          Padding(
            padding: const EdgeInsets.only(right: Space.x1),
            child: Text(LabMarkerTile.noResultText, style: quiet),
          ),
          for (final m in markers)
            TextButton(
              key: ValueKey('not-yet-${m.metricKey}'),
              style: TextButton.styleFrom(
                visualDensity: VisualDensity.compact,
                padding: const EdgeInsets.symmetric(horizontal: Space.x2),
              ),
              onPressed: () => context.push('/labs/marker/${m.metricKey}'),
              child: Text(m.displayName),
            ),
        ],
      ),
    );
  }
}

/// Holds a section to the reading width inside the wider Labs page.
class _Narrow extends StatelessWidget {
  final Widget child;

  const _Narrow({required this.child});

  @override
  Widget build(BuildContext context) => Align(
    alignment: Alignment.centerLeft,
    child: ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: Space.maxContentWidth),
      child: child,
    ),
  );
}

/// The title and the Add results button. On a narrow screen the button sits
/// under the title.
class _Header extends StatelessWidget {
  final bool showAdd;

  const _Header({required this.showAdd});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final title = Text('Labs', style: theme.textTheme.displaySmall);
    final button = FilledButton.tonalIcon(
      onPressed: () => context.push('/labs/add'),
      icon: const Icon(Icons.add, size: 18),
      label: const Text(LabsPage.addLabel),
    );
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x4),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (!showAdd) return title;
          if (constraints.maxWidth < 480) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                title,
                const SizedBox(height: Space.x3),
                button,
              ],
            );
          }
          return Row(
            children: [
              Expanded(child: title),
              button,
            ],
          );
        },
      ),
    );
  }
}

/// Shown while sex at birth is unspecified, since some ranges depend on it.
class _SexCard extends StatelessWidget {
  const _SexCard();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return _Narrow(
      child: Padding(
        padding: const EdgeInsets.only(bottom: Space.x4),
        child: SurfaceCard(
          padding: const EdgeInsets.all(Space.x4),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(LabsPage.sexCardText, style: theme.textTheme.bodyMedium),
              const SizedBox(height: Space.x2),
              TextButton(
                onPressed: () => context.go('/you'),
                child: const Text('Open You'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.x8),
      child: Column(
        children: [
          Text(
            LabsPage.emptySentence,
            style: theme.textTheme.headlineMedium,
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: Space.x3),
          Text(
            LabsPage.emptyNote,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: Space.x6),
          FilledButton.tonalIcon(
            onPressed: () => context.push('/labs/add'),
            icon: const Icon(Icons.add, size: 18),
            label: const Text(LabsPage.addLabel),
          ),
        ],
      ),
    );
  }
}

/// Tiles in even rows: 2 columns under 600 px, 3 under 1000, 4 above.
class _MarkerGrid extends StatelessWidget {
  final List<LabMarker> markers;

  const _MarkerGrid({required this.markers});

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = LabsPage.columnsFor(constraints.maxWidth);
        final rows = <Widget>[];
        for (var i = 0; i < markers.length; i += columns) {
          final cells = <Widget>[];
          for (var c = 0; c < columns; c++) {
            if (c > 0) cells.add(const SizedBox(width: Space.x3));
            final index = i + c;
            cells.add(
              Expanded(
                child: index < markers.length
                    ? LabMarkerTile(
                        key: ValueKey(markers[index].metricKey),
                        marker: markers[index],
                        onTap: () => context.push(
                          '/labs/marker/${markers[index].metricKey}',
                        ),
                      )
                    : const SizedBox.shrink(),
              ),
            );
          }
          if (rows.isNotEmpty) rows.add(const SizedBox(height: Space.x3));
          rows.add(
            IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: cells,
              ),
            ),
          );
        }
        return Column(children: rows);
      },
    );
  }
}

/// The stored panels, newest first, each with a delete.
class _Panels extends StatelessWidget {
  final List<LabReport> reports;

  const _Panels({required this.reports});

  Future<void> _delete(BuildContext context, LabReport report) async {
    final labs = context.read<LabsProvider>();
    final messenger = ScaffoldMessenger.of(context);
    final confirmed = await confirmAction(
      context,
      title: LabsPage.deleteTitle,
      message: LabsPage.deleteBody,
      confirmLabel: 'Delete',
    );
    if (!confirmed) return;
    final ok = await labs.deleteReport(report.id);
    if (!ok && labs.error != null) {
      showMessage(messenger, labs.error!);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return _Narrow(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SectionHeader(title: 'Panels'),
          SurfaceCard(
            padding: const EdgeInsets.symmetric(
              horizontal: Space.x4,
              vertical: Space.x2,
            ),
            child: Column(
              children: [
                for (var i = 0; i < reports.length; i++) ...[
                  if (i > 0) const Divider(height: 1),
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: Space.x2),
                    child: Row(
                      children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                DateFormat.yMMMMd().format(
                                  reports[i].collectedOn,
                                ),
                                style: theme.textTheme.titleSmall,
                              ),
                              Text(
                                '${reports[i].providerLabel} · '
                                '${_count(reports[i].results.length)}',
                                style: theme.textTheme.bodySmall?.copyWith(
                                  color: theme.colorScheme.onSurfaceVariant,
                                ),
                              ),
                            ],
                          ),
                        ),
                        IconButton(
                          tooltip: 'Delete panel',
                          onPressed: () => _delete(context, reports[i]),
                          icon: const Icon(Icons.delete_outline),
                        ),
                      ],
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  static String _count(int n) => '$n ${n == 1 ? 'result' : 'results'}';
}
