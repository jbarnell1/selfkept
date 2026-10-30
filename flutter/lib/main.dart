import 'dart:ui' show PlatformDispatcher;

import 'package:flutter/material.dart';
import 'package:flutter_web_plugins/url_strategy.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import 'brand/brand.dart';
import 'collectors/health_collector.dart';
import 'offline/open_outbox.dart';
import 'offline/outbox_repository.dart';
import 'offline/sync_engine.dart';
import 'providers/auth_provider.dart';
import 'providers/checkins_provider.dart';
import 'providers/collector_provider.dart';
import 'providers/household_provider.dart';
import 'providers/insights_provider.dart';
import 'providers/connect_provider.dart';
import 'providers/labs_provider.dart';
import 'providers/oura_app_provider.dart';
import 'providers/oura_provider.dart';
import 'providers/recent_problems_provider.dart';
import 'providers/setup_provider.dart';
import 'providers/profile_provider.dart';
import 'providers/server_settings.dart';
import 'providers/sources_provider.dart';
import 'providers/startup_provider.dart';
import 'providers/today_provider.dart';
import 'providers/trends_provider.dart';
import 'router/app_router.dart';
import 'services/api_client.dart';
import 'services/connect_service.dart';
import 'services/export_service.dart';
import 'services/checkins_service.dart';
import 'services/household_service.dart';
import 'services/ingest_service.dart';
import 'services/insights_service.dart';
import 'services/labs_service.dart';
import 'services/diagnostics_service.dart';
import 'services/integration_service.dart';
import 'services/oura_service.dart';
import 'services/setup_service.dart';
import 'services/profile_service.dart';
import 'services/reachability.dart';
import 'services/server_address_store.dart';
import 'services/server_version_service.dart';
import 'services/sources_service.dart';
import 'services/system_service.dart';
import 'services/today_service.dart';
import 'services/trends_service.dart';
import 'theme/app_theme.dart';
import 'utils/error_reporter.dart';
import 'utils/open_url.dart';
import 'providers/sso_provider.dart';
import 'services/sso_service.dart';
import 'widgets/reachability_banner.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  usePathUrlStrategy();

  // The stored server address must be known before the first request.
  final addressStore = SecureServerAddressStore();
  final override = await ServerSettings.readStoredOverride(addressStore);

  final reachability = Reachability();
  final client = DioApiClient(
    serverUrl: override ?? ServerSettings(store: addressStore).defaultUrl,
    reachability: reachability,
  );
  final settings = ServerSettings(
    store: addressStore,
    client: client,
    initialOverride: override,
  );

  // A build or layout failure must leave a trace in the browser console or
  // device log in every build mode (release builds otherwise swallow it), and
  // reach the person's own server so the admin sees it under Recent problems.
  final reporter = ErrorReporter(DiagnosticsService(client));
  final presentError = FlutterError.onError;
  FlutterError.onError = (details) {
    presentError?.call(details);
    debugPrint('Flutter error: ${details.exceptionAsString()}');
    if (details.stack != null) debugPrint(details.stack.toString());
    reporter.report(details.exception, details.stack, kind: 'flutter');
  };
  PlatformDispatcher.instance.onError = (error, stack) {
    debugPrint('Uncaught: $error');
    debugPrint(stack.toString());
    reporter.report(error, stack, kind: 'uncaught');
    return true;
  };

  runApp(
    App(
      client: client,
      settings: settings,
      reachability: reachability,
      outbox: openOutboxRepository(),
      collector: createHealthCollector(),
      reporter: reporter,
    ),
  );
}

/// Composition root: services, providers, router and theme.
class App extends StatefulWidget {
  final DioApiClient client;
  final ServerSettings settings;
  final Reachability reachability;
  final OutboxRepository outbox;
  final HealthCollector collector;
  final ErrorReporter reporter;

  const App({
    super.key,
    required this.client,
    required this.settings,
    required this.reachability,
    required this.outbox,
    required this.collector,
    required this.reporter,
  });

  @override
  State<App> createState() => _AppState();
}

class _AppState extends State<App> {
  late final AuthProvider _auth;
  late final StartupProvider _startup;
  late final ProfileProvider _profile;
  late final HouseholdProvider _household;
  late final SourcesProvider _sources;
  late final SyncEngine _sync;
  late final CollectorProvider _collector;
  late final CheckinsProvider _checkins;
  late final InsightsProvider _insights;
  late final TodayProvider _today;
  late final TrendsProvider _trends;
  late final LabsService _labsService;
  late final LabsProvider _labs;
  late final ConnectService _connectService;
  late final SsoProvider _sso;
  late final ExportService _exportService;
  late final ConnectProvider _connect;
  late final OuraProvider _oura;
  late final OuraAppProvider _ouraApp;
  late final SetupProvider _setup;
  late final RecentProblemsProvider _recentProblems;
  late final AppLifecycleListener _lifecycle;
  late final GoRouter _router;
  bool _collectorStarted = false;

  @override
  void initState() {
    super.initState();
    final client = widget.client;
    final system = SystemService(client);
    _auth = AuthProvider(client);
    _startup = StartupProvider(
      versions: ServerVersionService(system),
      system: system,
      auth: _auth,
      hasServerUrl: () => client.serverUrl.isNotEmpty,
    );
    _profile = ProfileProvider(service: ProfileService(client), auth: _auth);
    _household = HouseholdProvider(
      service: HouseholdService(client),
      auth: _auth,
    );
    _sources = SourcesProvider(service: SourcesService(client), auth: _auth);
    _sso = SsoProvider(service: SsoService(client), auth: _auth);
    _sync = SyncEngine(
      ingest: IngestService(client),
      outbox: widget.outbox,
      sources: _sources,
      reporter: widget.reporter,
    );
    _collector = CollectorProvider(
      collector: widget.collector,
      outbox: widget.outbox,
      sources: _sources,
      sync: _sync,
      auth: _auth,
      reporter: widget.reporter,
    );
    _checkins = CheckinsProvider(service: CheckinsService(client), auth: _auth);
    _insights = InsightsProvider(service: InsightsService(client), auth: _auth);
    _today = TodayProvider(
      service: TodayService(client),
      auth: _auth,
      checkins: _checkins,
      insights: _insights,
    );
    _trends = TrendsProvider(service: TrendsService(client), auth: _auth);
    _labsService = LabsService(client);
    _labs = LabsProvider(_labsService, auth: _auth);
    _connectService = ConnectService(client);
    _exportService = ExportService(client);
    _connect = ConnectProvider(_connectService, auth: _auth);
    _oura = OuraProvider(OuraService(client), auth: _auth);
    _ouraApp = OuraAppProvider(
      service: IntegrationService(client),
      auth: _auth,
    );
    _setup = SetupProvider(service: SetupService(client), auth: _auth);
    _recentProblems = RecentProblemsProvider(
      service: DiagnosticsService(client),
      auth: _auth,
    );
    _auth.addListener(_onAuthChanged);
    // A foreground pass every time the app comes back.
    _lifecycle = AppLifecycleListener(onResume: _collector.onResume);
    _router = buildRouter(auth: _auth, startup: _startup);
    widget.reporter.where = () => _router.state.uri.path;
  }

  /// The collector needs a signed-in person (its source belongs to one), so
  /// it starts after the first authentication and again after the next.
  void _onAuthChanged() {
    if (_auth.isAuthenticated && !_collectorStarted) {
      _collectorStarted = true;
      _sources.load().then((_) => _collector.init());
    } else if (!_auth.isAuthenticated) {
      _collectorStarted = false;
    }
  }

  @override
  void dispose() {
    _lifecycle.dispose();
    _auth.removeListener(_onAuthChanged);
    _router.dispose();
    _recentProblems.dispose();
    _setup.dispose();
    _ouraApp.dispose();
    _oura.dispose();
    _connect.dispose();
    _labs.dispose();
    _trends.dispose();
    _today.dispose();
    _insights.dispose();
    _checkins.dispose();
    _collector.dispose();
    _sync.dispose();
    _sources.dispose();
    _household.dispose();
    _profile.dispose();
    _startup.dispose();
    _auth.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider.value(value: widget.reachability),
        ChangeNotifierProvider.value(value: widget.settings),
        ChangeNotifierProvider.value(value: _auth),
        ChangeNotifierProvider.value(value: _startup),
        ChangeNotifierProvider.value(value: _profile),
        ChangeNotifierProvider.value(value: _household),
        ChangeNotifierProvider.value(value: _sources),
        ChangeNotifierProvider.value(value: _sync),
        ChangeNotifierProvider.value(value: _collector),
        ChangeNotifierProvider.value(value: _checkins),
        ChangeNotifierProvider.value(value: _insights),
        ChangeNotifierProvider.value(value: _today),
        ChangeNotifierProvider.value(value: _trends),
        Provider<LabsService>.value(value: _labsService),
        ChangeNotifierProvider.value(value: _labs),
        Provider<ConnectService>.value(value: _connectService),
        Provider<ExportService>.value(value: _exportService),
        ChangeNotifierProvider.value(value: _connect),
        ChangeNotifierProvider.value(value: _oura),
        ChangeNotifierProvider.value(value: _ouraApp),
        ChangeNotifierProvider.value(value: _setup),
        ChangeNotifierProvider.value(value: _recentProblems),
        ChangeNotifierProvider.value(value: _sso),
        Provider<UrlOpener>.value(value: openExternalUrl),
      ],
      child: MaterialApp.router(
        title: Brand.productName,
        debugShowCheckedModeBanner: false,
        theme: AppTheme.light,
        darkTheme: AppTheme.dark,
        themeMode: ThemeMode.system,
        routerConfig: _router,
        builder: (context, child) => Column(
          children: [
            const ReachabilityBanner(),
            Expanded(child: child ?? const SizedBox.shrink()),
          ],
        ),
      ),
    );
  }
}
