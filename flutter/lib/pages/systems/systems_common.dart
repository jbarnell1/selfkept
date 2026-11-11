import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../../providers/person_read.dart';
import '../../providers/viewed_person.dart';
import '../../theme/tokens.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/subpage_app_bar.dart';
import '../../widgets/surface_card.dart';

/// "Nov 9, 2026".
String spokenDay(DateTime day) => DateFormat.yMMMd().format(day);

/// "Nov 9".
String shortDay(DateTime day) => DateFormat.MMMd().format(day);

/// "Nov 8, 2026 to Nov 10, 2026", or the one date when both are the same day.
String spokenSpan(DateTime from, DateTime to) => DateUtils.isSameDay(from, to)
    ? spokenDay(from)
    : '${spokenDay(from)} to ${spokenDay(to)}';

/// A card that opens a page: an icon, a title, one line and a chevron.
class LinkCard extends StatelessWidget {
  final IconData icon;
  final String title;
  final String line;
  final VoidCallback onTap;

  const LinkCard({
    super.key,
    required this.icon,
    required this.title,
    required this.line,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.x3),
      child: Semantics(
        button: true,
        child: SurfaceCard(
          onTap: onTap,
          padding: const EdgeInsets.all(Space.x4),
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
      ),
    );
  }
}

/// One line in the soft ink, for "not shared" and "nothing yet".
class QuietLine extends StatelessWidget {
  final String text;

  const QuietLine(this.text, {super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Text(
      text,
      style: theme.textTheme.bodyMedium?.copyWith(
        color: theme.colorScheme.onSurfaceVariant,
      ),
    );
  }
}

/// A page opened from Systems: a back bar, pull to refresh and the
/// readable column.
class SubpageFrame extends StatelessWidget {
  final String fallback;
  final String backTitle;
  final Future<void> Function() onRefresh;
  final List<Widget> children;

  const SubpageFrame({
    super.key,
    this.fallback = '/systems',
    this.backTitle = 'Systems',
    required this.onRefresh,
    required this.children,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: SubpageAppBar(fallback: fallback, title: Text(backTitle)),
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: onRefresh,
          color: theme.colorScheme.primary,
          child: PageBody(
            physics: const AlwaysScrollableScrollPhysics(),
            children: children,
          ),
        ),
      ),
    );
  }
}

/// Loading, not shared, failed or the data, for any [PersonRead].
List<Widget> personReadBody<T>(
  BuildContext context,
  PersonRead<T> read, {
  required String errorTitle,
  required List<Widget> Function(T data) builder,
}) {
  final data = read.data;
  if (read.notShared) {
    final viewing = context.read<ViewedPerson>();
    return [NotSharedView(name: read.viewedName, onBack: viewing.viewSelf)];
  }
  if (data == null && read.error == null) {
    return const [
      Padding(
        padding: EdgeInsets.symmetric(vertical: Space.x8),
        child: LoadingView(),
      ),
    ];
  }
  if (data == null) {
    return [
      ErrorView(title: errorTitle, detail: read.error, onRetry: read.load),
    ];
  }
  return builder(data);
}

/// Starts [read] after the first frame unless it has loaded already.
void loadAfterFirstFrame(State state, PersonRead<dynamic> Function() read) {
  WidgetsBinding.instance.addPostFrameCallback((_) {
    if (!state.mounted) return;
    read().ensureLoaded();
  });
}
