import '../models/source.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Sources: where samples come from.
class SourcesService {
  final ApiClient _api;

  SourcesService(this._api);

  Future<List<Source>> list() => _api.get<List<Source>>(
    '/sources',
    fromJson: (body) => [for (final j in asJsonList(body)) Source.fromJson(j)],
  );

  /// Creates a source, or returns the existing one for this device install.
  Future<Source> create({
    required SourceKind kind,
    required String deviceName,
    required String platform,
    required String appVersion,
  }) => _api.post<Source>(
    '/sources',
    data: Source.toCreateJson(
      kind: kind,
      deviceName: deviceName,
      platform: platform,
      appVersion: appVersion,
    ),
    fromJson: (body) => Source.fromJson(asJsonMap(body)),
  );

  Future<Source> update(String id, {bool? enabled, String? deviceName}) =>
      _api.patch<Source>(
        '/sources/$id',
        data: {'enabled': ?enabled, 'deviceName': ?deviceName},
        fromJson: (body) => Source.fromJson(asJsonMap(body)),
      );

  /// Samples are kept on the server and marked orphaned.
  Future<void> delete(String id) => _api.delete<void>('/sources/$id');
}
