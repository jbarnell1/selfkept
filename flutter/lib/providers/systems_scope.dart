import 'package:flutter/widgets.dart';
import 'package:provider/provider.dart';
import 'package:provider/single_child_widget.dart';

import '../services/api_client.dart';
import '../services/context_service.dart';
import '../services/household_overview_service.dart';
import '../services/insights_service.dart';
import '../services/lab_review_service.dart';
import '../services/review_service.dart';
import '../services/systems_service.dart';
import '../services/trials_service.dart';
import 'auth_provider.dart';
import 'context_provider.dart';
import 'startup_provider.dart';
import 'systems_provider.dart';
import 'trials_provider.dart';
import 'viewed_person.dart';

/// The `features` names in /system/version that turn on the Systems,
/// context and lab review screens.
abstract final class SystemsFeatures {
  /// Systems, context, patterns, the read-backs and the weekly review.
  static const String contextPatterns = 'context.patterns';

  /// Lab review, timeline, draw conditions, related readings, trials and
  /// the household overview.
  static const String labsReview = 'labs.review';

  /// True when the server named [feature]. Rebuilds when the server changes.
  static bool has(BuildContext context, String feature) =>
      context.select<StartupProvider, bool>(
        (s) => s.versionInfo?.hasFeature(feature) ?? false,
      );

  /// The same, read once, for callbacks.
  static bool read(BuildContext context, String feature) =>
      context.read<StartupProvider>().versionInfo?.hasFeature(feature) ?? false;
}

/// The services and providers behind Systems, context, the weekly review,
/// lab review and trials, for one MultiProvider list. They are created on
/// first use and disposed with the list. Needs [AuthProvider] and
/// [ViewedPerson] above it.
List<SingleChildWidget> systemsProviders(ApiClient client) => [
  Provider<SystemsService>(create: (_) => SystemsService(client)),
  Provider<LabReviewService>(create: (_) => LabReviewService(client)),
  ChangeNotifierProvider<SystemsProvider>(
    create: (c) => SystemsProvider(
      service: c.read<SystemsService>(),
      auth: c.read<AuthProvider>(),
      viewing: c.read<ViewedPerson>(),
    ),
  ),
  ChangeNotifierProvider<ContextProvider>(
    create: (c) => ContextProvider(
      service: ContextService(client),
      auth: c.read<AuthProvider>(),
      viewing: c.read<ViewedPerson>(),
    ),
  ),
  ChangeNotifierProvider<PatternsProvider>(
    create: (c) => PatternsProvider(
      service: c.read<SystemsService>(),
      auth: c.read<AuthProvider>(),
      viewing: c.read<ViewedPerson>(),
    ),
  ),
  ChangeNotifierProvider<SleepNightsProvider>(
    create: (c) => SleepNightsProvider(
      service: c.read<SystemsService>(),
      auth: c.read<AuthProvider>(),
      viewing: c.read<ViewedPerson>(),
    ),
  ),
  ChangeNotifierProvider<WorkoutsProvider>(
    create: (c) => WorkoutsProvider(
      service: c.read<SystemsService>(),
      auth: c.read<AuthProvider>(),
      viewing: c.read<ViewedPerson>(),
    ),
  ),
  ChangeNotifierProvider<WeeklyReviewProvider>(
    create: (c) => WeeklyReviewProvider(
      service: ReviewService(client),
      auth: c.read<AuthProvider>(),
      viewing: c.read<ViewedPerson>(),
    ),
  ),
  ChangeNotifierProvider<LabTimelineProvider>(
    create: (c) => LabTimelineProvider(
      service: c.read<LabReviewService>(),
      auth: c.read<AuthProvider>(),
      viewing: c.read<ViewedPerson>(),
    ),
  ),
  ChangeNotifierProvider<TrialsProvider>(
    create: (c) => TrialsProvider(
      service: TrialsService(client),
      insights: InsightsService(client),
      auth: c.read<AuthProvider>(),
      viewing: c.read<ViewedPerson>(),
    ),
  ),
  ChangeNotifierProvider<HouseholdOverviewProvider>(
    create: (c) => HouseholdOverviewProvider(
      service: HouseholdOverviewService(client),
      auth: c.read<AuthProvider>(),
    ),
  ),
];
