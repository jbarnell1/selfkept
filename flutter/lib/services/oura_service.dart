import '../models/connect.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The Oura ring link: status, authorise, pull now, disconnect.
class OuraService {
  final ApiClient _api;

  OuraService(this._api);

  Future<OuraStatus> status() => _api.get<OuraStatus>(
    '/oura',
    fromJson: (body) => OuraStatus.fromJson(asJsonMap(body)),
  );

  /// The Oura page to send the person to. [returnTo] is the in-app path the
  /// browser comes back to; the server accepts only the ones it knows.
  Future<String> connect({String? returnTo}) => _api.post<String>(
    returnTo == null
        ? '/oura/connect'
        : '/oura/connect?returnTo=${Uri.encodeQueryComponent(returnTo)}',
    fromJson: (body) => JsonReader(asJsonMap(body)).string('authorizeUrl'),
  );

  /// Asks the server to pull now. It answers 202 and works in the background.
  Future<void> sync() => _api.post<void>('/oura/sync');

  Future<void> disconnect() => _api.delete<void>('/oura');
}
