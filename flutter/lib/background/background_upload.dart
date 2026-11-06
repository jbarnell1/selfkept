import '../collectors/health_collector.dart';
import '../offline/outbox_repository.dart';
import '../offline/sync_engine.dart';
import '../providers/auth_provider.dart';
import '../providers/collector_provider.dart';
import '../providers/sources_provider.dart';
import '../services/api_client.dart';
import '../services/ingest_service.dart';
import '../services/sources_service.dart';
import '../utils/error_reporter.dart';

/// One send with no screen: the Android worker starts a headless engine
/// while the app is closed, and this reads what the phone collected and
/// sends it to the person's server through the same collector, outbox and
/// sync engine the app uses. Nothing here asks for anything; a signed-out
/// phone, a phone that never connected Health Connect or an unreachable
/// server simply ends the pass.
class BackgroundUpload {
  final ApiClient _client;
  final OutboxRepository _outbox;
  final HealthCollector _collector;
  final ErrorReporter? _reporter;

  BackgroundUpload({
    required this._client,
    required this._outbox,
    required this._collector,
    this._reporter,
  });

  /// True when everything collected reached the server.
  Future<bool> run() async {
    final auth = AuthProvider(_client);
    final sources = SourcesProvider(
      service: SourcesService(_client),
      auth: auth,
    );
    final sync = SyncEngine(
      ingest: IngestService(_client),
      outbox: _outbox,
      sources: sources,
      reporter: _reporter,
    );
    final collector = CollectorProvider(
      collector: _collector,
      outbox: _outbox,
      sources: sources,
      sync: sync,
      auth: auth,
      reporter: _reporter,
    );
    try {
      await auth.bootstrap();
      if (!auth.isAuthenticated) return false;
      await sources.load();
      await collector.init(collect: false);
      if (!collector.isConnected) return false;
      await collector.collectAndFlush();
      return collector.error == null && await _outbox.count() == 0;
    } finally {
      collector.dispose();
      sync.dispose();
      sources.dispose();
      auth.dispose();
    }
  }
}
