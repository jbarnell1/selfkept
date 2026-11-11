import '../models/context.dart';
import '../models/systems.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The Systems tab: body systems, their detail, patterns and the sleep and
/// workout read-backs. Every read takes the person being viewed.
class SystemsService {
  final ApiClient _api;

  SystemsService(this._api);

  Future<SystemsOverview> systems({String? personId}) =>
      _api.get<SystemsOverview>(
        '/systems',
        queryParameters: {'personId': ?personId},
        fromJson: (body) => SystemsOverview.fromJson(asJsonMap(body)),
      );

  /// One system with 28 days of each reading, lab history and its cards.
  Future<BodySystem> system(String key, {String? personId}) =>
      _api.get<BodySystem>(
        '/systems/$key',
        queryParameters: {'personId': ?personId},
        fromJson: (body) => BodySystem.fromJson(asJsonMap(body)),
      );

  Future<Patterns> patterns({String? personId}) => _api.get<Patterns>(
    '/patterns',
    queryParameters: {'personId': ?personId},
    fromJson: (body) => Patterns.fromJson(asJsonMap(body)),
  );

  /// The server's default window: the last 14 nights.
  Future<SleepNights> sleep({String? personId}) => _api.get<SleepNights>(
    '/sleep',
    queryParameters: {'personId': ?personId},
    fromJson: (body) => SleepNights.fromJson(asJsonMap(body)),
  );

  /// The server's default window: the last 28 days.
  Future<Workouts> workouts({String? personId}) => _api.get<Workouts>(
    '/workouts',
    queryParameters: {'personId': ?personId},
    fromJson: (body) => Workouts.fromJson(asJsonMap(body)),
  );
}
