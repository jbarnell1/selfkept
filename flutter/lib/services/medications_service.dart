import '../models/medication.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The list of medicines and supplements. A record, never advice.
class MedicationsService {
  final ApiClient _api;

  MedicationsService(this._api);

  /// Current items first.
  Future<List<Medication>> list({String? personId}) =>
      _api.get<List<Medication>>(
        '/medications',
        queryParameters: {'personId': ?personId},
        fromJson: Medication.listFromJson,
      );

  Future<Medication> create(MedicationDraft draft) => _api.post<Medication>(
    '/medications',
    data: draft.toJson(),
    fromJson: (body) => Medication.fromJson(asJsonMap(body)),
  );

  Future<Medication> update(String id, MedicationDraft draft) =>
      _api.put<Medication>(
        '/medications/$id',
        data: draft.toJson(),
        fromJson: (body) => Medication.fromJson(asJsonMap(body)),
      );

  Future<void> delete(String id) => _api.delete<void>('/medications/$id');
}
