import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';
import 'package:selfkept/collectors/health_collector.dart';
import 'package:selfkept/offline/outbox_repository.dart';
import 'package:selfkept/offline/sync_engine.dart';
import 'package:selfkept/providers/auth_provider.dart';
import 'package:selfkept/providers/checkins_provider.dart';
import 'package:selfkept/providers/collector_provider.dart';
import 'package:selfkept/providers/goals_provider.dart';
import 'package:selfkept/providers/journal_provider.dart';
import 'package:selfkept/providers/medications_provider.dart';
import 'package:selfkept/providers/progress_provider.dart';
import 'package:selfkept/providers/record_changes.dart';
import 'package:selfkept/providers/tally_provider.dart';
import 'package:selfkept/providers/viewed_person.dart';
import 'package:selfkept/providers/connect_provider.dart';
import 'package:selfkept/providers/household_provider.dart';
import 'package:selfkept/providers/insights_provider.dart';
import 'package:selfkept/providers/labs_provider.dart';
import 'package:selfkept/providers/oura_app_provider.dart';
import 'package:selfkept/providers/oura_provider.dart';
import 'package:selfkept/providers/profile_provider.dart';
import 'package:selfkept/providers/recent_problems_provider.dart';
import 'package:selfkept/providers/retention_provider.dart';
import 'package:selfkept/providers/server_settings.dart';
import 'package:selfkept/providers/setup_provider.dart';
import 'package:selfkept/providers/sources_provider.dart';
import 'package:selfkept/providers/startup_provider.dart';
import 'package:selfkept/providers/systems_scope.dart';
import 'package:selfkept/providers/today_provider.dart';
import 'package:selfkept/providers/trends_provider.dart';
import 'package:selfkept/services/body_service.dart';
import 'package:selfkept/services/checkins_service.dart';
import 'package:selfkept/services/goals_service.dart';
import 'package:selfkept/services/journal_service.dart';
import 'package:selfkept/services/medications_service.dart';
import 'package:selfkept/services/progress_service.dart';
import 'package:selfkept/services/strength_service.dart';
import 'package:selfkept/services/targets_service.dart';
import 'package:selfkept/services/connect_service.dart';
import 'package:selfkept/services/export_service.dart';
import 'package:selfkept/services/household_service.dart';
import 'package:selfkept/services/ingest_service.dart';
import 'package:selfkept/services/insights_service.dart';
import 'package:selfkept/services/diagnostics_service.dart';
import 'package:selfkept/services/retention_service.dart';
import 'package:selfkept/services/integration_service.dart';
import 'package:selfkept/services/labs_service.dart';
import 'package:selfkept/services/oura_service.dart';
import 'package:selfkept/services/profile_service.dart';
import 'package:selfkept/services/reachability.dart';
import 'package:selfkept/services/server_address_store.dart';
import 'package:selfkept/services/server_version_service.dart';
import 'package:selfkept/services/setup_service.dart';
import 'package:selfkept/services/sources_service.dart';
import 'package:selfkept/services/system_service.dart';
import 'package:selfkept/services/today_service.dart';
import 'package:selfkept/services/trends_service.dart';
import 'package:selfkept/theme/app_theme.dart';
import 'package:selfkept/utils/open_url.dart';
import 'package:selfkept/providers/sso_provider.dart';
import 'package:selfkept/utils/device_time_zone.dart';
import 'package:selfkept/services/sso_service.dart';

import 'package:selfkept/providers/assistant_providers.dart';
import 'package:selfkept/services/sse/event_stream_client.dart';

import 'fake_api_client.dart';
import 'fake_event_stream.dart';

/// Everything a page needs, built on one fake client. Never touches the network.
class TestHarness {
  final FakeApiClient api;
  final AuthProvider auth;
  final StartupProvider startup;
  final ServerSettings settings;
  final Reachability reachability;
  final ProfileProvider profile;
  final HouseholdProvider household;
  final SourcesProvider sources;
  final OutboxRepository outbox;
  final SyncEngine sync;
  final CollectorProvider collector;
  final CheckinsProvider checkins;
  final InsightsProvider insights;
  final TodayProvider today;
  final TrendsProvider trends;
  final LabsService labsService;
  final LabsProvider labs;
  final ConnectService connectService;
  final ConnectProvider connect;
  final OuraProvider oura;
  final OuraAppProvider ouraApp;
  final SetupProvider setup;
  final RecentProblemsProvider recentProblems;
  final RetentionProvider retention;
  final SsoProvider sso;
  final RecordChanges recordChanges;
  final ViewedPerson viewing;
  final TallyProvider tally;
  final JournalProvider journal;
  final GoalsProvider goals;
  final MedicationsProvider medications;
  final ProgressProvider progress;

  /// The in-app assistant, streaming through [streams].
  final AssistantProviders assistant;
  final FakeEventStreamTransport streams;

  /// Stands in for the browser everywhere a page opens a link.
  final UrlOpener openUrl;

  TestHarness._({
    required this.api,
    required this.auth,
    required this.startup,
    required this.settings,
    required this.reachability,
    required this.profile,
    required this.household,
    required this.sources,
    required this.outbox,
    required this.sync,
    required this.collector,
    required this.checkins,
    required this.insights,
    required this.today,
    required this.trends,
    required this.labsService,
    required this.labs,
    required this.connectService,
    required this.connect,
    required this.oura,
    required this.ouraApp,
    required this.setup,
    required this.recentProblems,
    required this.retention,
    required this.sso,
    required this.recordChanges,
    required this.viewing,
    required this.tally,
    required this.journal,
    required this.goals,
    required this.medications,
    required this.progress,
    required this.assistant,
    required this.streams,
    required this.openUrl,
  });

  /// [collector] defaults to the no-op one, which is what the web build has.
  ///
  /// [openUrl] stands in for the browser; the Oura provider calls it.
  /// [ssoBrowser] stands in for the provider sign-in sheet.
  factory TestHarness({
    FakeApiClient? api,
    HealthCollector collector = const NoopHealthCollector(),
    OutboxRepository? outbox,
    Future<bool> Function(Uri uri)? openUrl,
    SsoBrowser? ssoBrowser,
    DeviceTimeZone? deviceZone,
  }) {
    final client = api ?? FakeApiClient();
    final auth = AuthProvider(client);
    final system = SystemService(client);
    final startup = StartupProvider(
      versions: ServerVersionService(
        system,
        appVersion: '0.1.0',
        minServerVersion: '0.1.0',
      ),
      system: system,
      auth: auth,
      hasServerUrl: () => client.serverUrl.isNotEmpty,
    );
    final sources = SourcesProvider(
      service: SourcesService(client),
      auth: auth,
    );
    final box = outbox ?? MemoryOutboxRepository();
    final sync = SyncEngine(
      ingest: IngestService(client),
      outbox: box,
      sources: sources,
    );
    final checkins = CheckinsProvider(
      service: CheckinsService(client),
      auth: auth,
    );
    final insights = InsightsProvider(
      service: InsightsService(client),
      auth: auth,
    );
    final changes = RecordChanges();
    final viewing = ViewedPerson(auth: auth);
    final goalsService = GoalsService(client);
    final targetsService = TargetsService(client);
    final streams = FakeEventStreamTransport();
    return TestHarness._(
      streams: streams,
      assistant: AssistantProviders(
        api: client,
        events: EventStreamClient(
          transport: streams,
          serverUrl: () => client.serverUrl,
          accessToken: () async => 'test-token',
          refresh: () async => false,
        ),
        auth: auth,
        changes: changes,
      ),
      recordChanges: changes,
      viewing: viewing,
      tally: TallyProvider(
        service: targetsService,
        auth: auth,
        changes: changes,
      ),
      journal: JournalProvider(
        service: JournalService(client),
        auth: auth,
        changes: changes,
        now: () => DateTime(2026, 11, 10, 12),
      ),
      goals: GoalsProvider(
        goals: goalsService,
        targets: targetsService,
        auth: auth,
        changes: changes,
        viewing: viewing,
      ),
      medications: MedicationsProvider(
        service: MedicationsService(client),
        auth: auth,
        viewing: viewing,
      ),
      progress: ProgressProvider(
        service: ProgressService(client),
        goals: goalsService,
        targets: targetsService,
        body: BodyService(client),
        strength: StrengthService(client),
        auth: auth,
        changes: changes,
        viewing: viewing,
      ),
      api: client,
      auth: auth,
      startup: startup,
      settings: ServerSettings(store: MemoryServerAddressStore()),
      reachability: Reachability(listenToInterface: false),
      profile: ProfileProvider(
        service: ProfileService(client),
        auth: auth,
        deviceZone: deviceZone ?? () async => null,
      ),
      household: HouseholdProvider(
        service: HouseholdService(client),
        auth: auth,
      ),
      sources: sources,
      outbox: box,
      sync: sync,
      collector: CollectorProvider(
        collector: collector,
        outbox: box,
        sources: sources,
        sync: sync,
        auth: auth,
        platformName: 'test',
        appVersion: '0.1.0',
      ),
      checkins: checkins,
      insights: insights,
      today: TodayProvider(
        service: TodayService(client),
        auth: auth,
        checkins: checkins,
        insights: insights,
      ),
      trends: TrendsProvider(service: TrendsService(client), auth: auth),
      labsService: LabsService(client),
      labs: LabsProvider(LabsService(client), auth: auth),
      connectService: ConnectService(client),
      connect: ConnectProvider(ConnectService(client), auth: auth),
      oura: OuraProvider(OuraService(client), auth: auth, open: openUrl),
      ouraApp: OuraAppProvider(service: IntegrationService(client), auth: auth),
      setup: SetupProvider(service: SetupService(client), auth: auth),
      recentProblems: RecentProblemsProvider(
        service: DiagnosticsService(client),
        auth: auth,
      ),
      retention: RetentionProvider(
        service: RetentionService(client),
        auth: auth,
      ),
      sso: SsoProvider(
        service: SsoService(client),
        auth: auth,
        browser: ssoBrowser ?? (_) async => null,
        client: 'mobile',
      ),
      openUrl: openUrl ?? (_) async => true,
    );
  }

  /// [router], when given, replaces [child] so navigation can be asserted.
  Widget wrap(Widget child, {ThemeData? theme, GoRouter? router}) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider.value(value: reachability),
        ChangeNotifierProvider.value(value: settings),
        ChangeNotifierProvider.value(value: auth),
        ChangeNotifierProvider.value(value: startup),
        ChangeNotifierProvider.value(value: profile),
        ChangeNotifierProvider.value(value: household),
        ChangeNotifierProvider.value(value: sources),
        ChangeNotifierProvider.value(value: sync),
        ChangeNotifierProvider.value(value: collector),
        ChangeNotifierProvider.value(value: checkins),
        ChangeNotifierProvider.value(value: insights),
        ChangeNotifierProvider.value(value: today),
        ChangeNotifierProvider.value(value: trends),
        Provider<LabsService>.value(value: labsService),
        ChangeNotifierProvider.value(value: labs),
        Provider<ConnectService>.value(value: connectService),
        Provider<ExportService>.value(value: ExportService(api)),
        ChangeNotifierProvider.value(value: connect),
        ChangeNotifierProvider.value(value: oura),
        ChangeNotifierProvider.value(value: ouraApp),
        ChangeNotifierProvider.value(value: setup),
        ChangeNotifierProvider.value(value: recentProblems),
        ChangeNotifierProvider.value(value: retention),
        ChangeNotifierProvider.value(value: sso),
        ChangeNotifierProvider.value(value: recordChanges),
        ChangeNotifierProvider.value(value: viewing),
        ChangeNotifierProvider.value(value: tally),
        ChangeNotifierProvider.value(value: journal),
        ChangeNotifierProvider.value(value: goals),
        ChangeNotifierProvider.value(value: medications),
        ChangeNotifierProvider.value(value: progress),
        Provider<UrlOpener>.value(value: openUrl),
        // Systems, context, weekly review, lab review and trials.
        ...systemsProviders(api),
        ...assistant.providers,
      ],
      child: router == null
          ? MaterialApp(theme: theme ?? AppTheme.light, home: child)
          : MaterialApp.router(
              theme: theme ?? AppTheme.light,
              routerConfig: router,
            ),
    );
  }

  void dispose() {
    assistant.dispose();
    progress.dispose();
    medications.dispose();
    goals.dispose();
    journal.dispose();
    tally.dispose();
    viewing.dispose();
    recordChanges.dispose();
    sso.dispose();
    retention.dispose();
    recentProblems.dispose();
    setup.dispose();
    ouraApp.dispose();
    oura.dispose();
    connect.dispose();
    labs.dispose();
    trends.dispose();
    today.dispose();
    insights.dispose();
    checkins.dispose();
    collector.dispose();
    sync.dispose();
    sources.dispose();
    household.dispose();
    profile.dispose();
    startup.dispose();
    auth.dispose();
    reachability.dispose();
    settings.dispose();
  }
}

/// Runs [body] once with the light theme and once with the dark theme.
void testBothThemes(
  String name,
  Future<void> Function(WidgetTester tester, ThemeData theme) body,
) {
  testWidgets('$name (light)', (tester) => body(tester, AppTheme.light));
  testWidgets('$name (dark)', (tester) => body(tester, AppTheme.dark));
}

/// A phone-sized surface so bottom sheets and columns lay out like a device.
Future<void> usePhoneSize(WidgetTester tester) async {
  tester.view.physicalSize = const Size(390 * 3, 844 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
}

/// Pumps a handful of frames. Use instead of pumpAndSettle when a progress
/// indicator is legitimately on screen, since that never settles.
Future<void> pumpFrames(WidgetTester tester, {int frames = 6}) async {
  for (var i = 0; i < frames; i++) {
    await tester.pump(const Duration(milliseconds: 100));
  }
}

/// Scrolls the page's own list until [finder] is truly on screen. A ListView
/// builds children ahead of the viewport, so scrollUntilVisible alone can stop
/// while the target is still off screen; ensureVisible finishes the job.
Future<void> scrollTo(WidgetTester tester, Finder finder) async {
  await tester.scrollUntilVisible(
    finder,
    200,
    scrollable: find.byType(Scrollable).first,
  );
  await tester.ensureVisible(finder.first);
  await tester.pumpAndSettle();
}
