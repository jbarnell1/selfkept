import 'package:go_router/go_router.dart';

import '../pages/labs/lab_review_page.dart';
import '../pages/labs/lab_timeline_page.dart';
import '../pages/systems/patterns_page.dart';
import '../pages/systems/readback_pages.dart';
import '../pages/systems/system_detail_page.dart';
import '../pages/systems/trials_page.dart';
import '../pages/systems/weekly_review_page.dart';

/// The pages behind the Systems tab, in its branch so the tab stays
/// selected. The fixed paths come before `/systems/:key`.
List<RouteBase> systemsRoutes() => [
  GoRoute(
    path: '/systems/review',
    builder: (context, state) => const WeeklyReviewPage(),
  ),
  GoRoute(
    path: '/systems/patterns',
    builder: (context, state) => const PatternsPage(),
  ),
  GoRoute(
    path: '/systems/sleep',
    builder: (context, state) => const SleepNightsPage(),
  ),
  GoRoute(
    path: '/systems/workouts',
    builder: (context, state) => const WorkoutsPage(),
  ),
  GoRoute(
    path: '/systems/trials',
    builder: (context, state) => const TrialsPage(),
  ),
  GoRoute(
    path: '/systems/:key',
    builder: (context, state) =>
        SystemDetailPage(systemKey: state.pathParameters['key'] ?? ''),
  ),
  GoRoute(
    path: '/labs/timeline',
    builder: (context, state) => const LabTimelinePage(),
  ),
  GoRoute(
    path: '/labs/report/:id',
    builder: (context, state) =>
        LabReviewPage(reportId: state.pathParameters['id'] ?? ''),
  ),
];
