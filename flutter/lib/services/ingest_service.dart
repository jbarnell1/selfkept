import '../models/sample.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// POST /ingest/batch.
class IngestService {
  final ApiClient _api;

  IngestService(this._api);

  /// The server caps a batch at this many samples; the outbox pages well
  /// below it.
  static const int maxBatchSize = 5000;

  Future<IngestResult> postBatch({
    required String sourceId,
    String? cursor,
    List<Sample> samples = const [],
    List<Workout> workouts = const [],
  }) => _api.post<IngestResult>(
    '/ingest/batch',
    data: {
      'sourceId': sourceId,
      'cursor': cursor,
      'samples': [for (final s in samples) s.toIngestJson()],
      'workouts': [for (final w in workouts) w.toIngestJson()],
    },
    fromJson: (body) => IngestResult.fromJson(asJsonMap(body)),
  );
}
