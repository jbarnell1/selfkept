import '../models/diagnostics.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The app's own error reports, and the admin's list of recent problems.
class DiagnosticsService {
  final ApiClient _api;

  DiagnosticsService(this._api);

  Future<void> report({
    required String kind,
    required String message,
    String? stack,
    String? appVersion,
    String? platform,
    String? where,
  }) => _api.post<void>(
    '/diagnostics/client-errors',
    data: {
      'kind': kind,
      'message': message,
      'stack': ?stack,
      'appVersion': ?appVersion,
      'platform': ?platform,
      'where': ?where,
    },
  );

  Future<RecentProblems> problems() => _api.get<RecentProblems>(
    '/diagnostics/problems',
    fromJson: (body) => RecentProblems.fromJson(asJsonMap(body)),
  );
}
