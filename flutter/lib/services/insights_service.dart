import '../models/insight.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The insights archive and what the person says about a card.
class InsightsService {
  final ApiClient _api;

  InsightsService(this._api);

  /// Newest first. Dismissed cards are left out unless asked for.
  Future<List<Insight>> list({
    required DateTime from,
    required DateTime to,
    bool includeDismissed = false,
    String? personId,
  }) => _api.get<List<Insight>>(
    '/insights',
    queryParameters: {
      'from': _day(from),
      'to': _day(to),
      'includeDismissed': includeDismissed,
      'personId': ?personId,
    },
    fromJson: (body) => [for (final j in asJsonList(body)) Insight.fromJson(j)],
  );

  Future<Insight> react(String id, InsightReaction reaction) =>
      _api.post<Insight>(
        '/insights/$id/reaction',
        data: Insight.toReactionJson(reaction),
        fromJson: (body) => Insight.fromJson(asJsonMap(body)),
      );

  Future<Insight> dismiss(String id) => _api.post<Insight>(
    '/insights/$id/dismiss',
    fromJson: (body) => Insight.fromJson(asJsonMap(body)),
  );

  /// Asks the server to rebuild baselines and cards. Answers 202 at once;
  /// the work finishes in the background.
  Future<void> recompute({String? personId}) =>
      _api.post<void>('/insights/recompute', data: {'personId': ?personId});

  static String _day(DateTime d) {
    final local = d.toLocal();
    final m = local.month.toString().padLeft(2, '0');
    final day = local.day.toString().padLeft(2, '0');
    return '${local.year}-$m-$day';
  }
}
