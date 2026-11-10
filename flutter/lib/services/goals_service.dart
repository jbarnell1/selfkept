import '../models/goal.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Goals: at most one main and one secondary at a time.
class GoalsService {
  final ApiClient _api;

  GoalsService(this._api);

  Future<List<Goal>> list({String? personId, bool includeEnded = false}) =>
      _api.get<List<Goal>>(
        '/goals',
        queryParameters: {
          'personId': ?personId,
          if (includeEnded) 'includeEnded': true,
        },
        fromJson: Goal.listFromJson,
      );

  /// Creating in a slot that is taken ends the old goal as dropped.
  Future<Goal> create(GoalDraft draft) => _api.post<Goal>(
    '/goals',
    data: draft.toJson(),
    fromJson: (body) => Goal.fromJson(asJsonMap(body)),
  );

  /// A goal keeps its slot: the draft's slot must match.
  Future<Goal> update(String id, GoalDraft draft) => _api.put<Goal>(
    '/goals/$id',
    data: draft.toJson(),
    fromJson: (body) => Goal.fromJson(asJsonMap(body)),
  );

  /// [status] is done or dropped. The goal's targets stay.
  Future<Goal> end(String id, GoalStatus status) => _api.post<Goal>(
    '/goals/$id/end',
    data: {'status': status.wireValue},
    fromJson: (body) => Goal.fromJson(asJsonMap(body)),
  );
}
