import '../models/target.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Targets and their tally against the last seven days.
class TargetsService {
  final ApiClient _api;

  TargetsService(this._api);

  Future<List<Target>> list({String? personId}) => _api.get<List<Target>>(
    '/targets',
    queryParameters: {'personId': ?personId},
    fromJson: Target.listFromJson,
  );

  Future<GoalTally> tally({String? personId}) => _api.get<GoalTally>(
    '/targets/tally',
    queryParameters: {'personId': ?personId},
    fromJson: (body) => GoalTally.fromJson(asJsonMap(body)),
  );

  Future<Target> create(TargetDraft draft) => _api.post<Target>(
    '/targets',
    data: draft.toJson(),
    fromJson: (body) => Target.fromJson(asJsonMap(body)),
  );

  Future<Target> update(String id, TargetDraft draft) => _api.put<Target>(
    '/targets/$id',
    data: draft.toJson(),
    fromJson: (body) => Target.fromJson(asJsonMap(body)),
  );

  Future<void> delete(String id) => _api.delete<void>('/targets/$id');
}
