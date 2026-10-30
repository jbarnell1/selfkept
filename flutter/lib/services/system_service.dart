import '../models/system.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Unauthenticated system endpoints.
class SystemService {
  final ApiClient _api;

  SystemService(this._api);

  Future<VersionInfo> version() => _api.get<VersionInfo>(
    '/system/version',
    fromJson: (body) => VersionInfo.fromJson(asJsonMap(body)),
  );

  Future<SetupStatus> setupStatus() => _api.get<SetupStatus>(
    '/system/setup-status',
    fromJson: (body) => SetupStatus.fromJson(asJsonMap(body)),
  );

  Future<HealthStatus> health() => _api.get<HealthStatus>(
    '/system/health',
    fromJson: (body) => HealthStatus.fromJson(asJsonMap(body)),
  );
}
