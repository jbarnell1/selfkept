import '../models/setup.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Server-wide integrations the household admin sets up from the app.
class IntegrationService {
  final ApiClient _api;

  IntegrationService(this._api);

  Future<OuraApp> ouraApp() => _api.get<OuraApp>(
    '/integrations/oura',
    fromJson: (body) => OuraApp.fromJson(asJsonMap(body)),
  );

  Future<OuraApp> saveOuraApp({
    required String clientId,
    required String clientSecret,
  }) => _api.put<OuraApp>(
    '/integrations/oura',
    data: {'clientId': clientId, 'clientSecret': clientSecret},
    fromJson: (body) => OuraApp.fromJson(asJsonMap(body)),
  );

  Future<OuraApp> clearOuraApp() => _api.delete<OuraApp>(
    '/integrations/oura',
    fromJson: (body) => OuraApp.fromJson(asJsonMap(body)),
  );
}
