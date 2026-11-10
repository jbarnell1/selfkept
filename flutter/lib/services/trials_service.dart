import '../models/trial.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Trials: one change with a planned check. Reads take the person being
/// viewed; writes are always the caller's own.
class TrialsService {
  final ApiClient _api;

  TrialsService(this._api);

  /// Newest start first; ended trials too when [includeEnded].
  Future<List<Trial>> list({String? personId, bool includeEnded = true}) =>
      _api.get<List<Trial>>(
        '/trials',
        queryParameters: {'personId': ?personId, 'includeEnded': includeEnded},
        fromJson: Trial.listFromJson,
      );

  Future<Trial> create(TrialDraft draft) => _api.post<Trial>(
    '/trials',
    data: draft.toCreateJson(),
    fromJson: (body) => Trial.fromJson(asJsonMap(body)),
  );

  /// Only the fields in [changes] change.
  Future<Trial> update(String id, Map<String, dynamic> changes) =>
      _api.patch<Trial>(
        '/trials/$id',
        data: changes,
        fromJson: (body) => Trial.fromJson(asJsonMap(body)),
      );

  Future<void> delete(String id) => _api.delete<void>('/trials/$id');
}
