import '../models/progress.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The Progress tab in one read.
class ProgressService {
  final ApiClient _api;

  ProgressService(this._api);

  Future<Progress> progress({String? personId}) => _api.get<Progress>(
    '/progress',
    queryParameters: {'personId': ?personId},
    fromJson: (body) => Progress.fromJson(asJsonMap(body)),
  );
}
