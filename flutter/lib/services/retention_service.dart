import '../models/retention.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// How long the server keeps raw heart rate (household admin only).
class RetentionService {
  final ApiClient _api;

  RetentionService(this._api);

  Future<Retention> get() => _api.get<Retention>(
    '/retention',
    fromJson: (body) => Retention.fromJson(asJsonMap(body)),
  );

  Future<Retention> save(int rawMonths) => _api.put<Retention>(
    '/retention',
    data: {'rawMonths': rawMonths},
    fromJson: (body) => Retention.fromJson(asJsonMap(body)),
  );

  Future<RetentionRun> run() => _api.post<RetentionRun>(
    '/retention/run',
    fromJson: (body) => RetentionRun.fromJson(asJsonMap(body)),
  );
}
