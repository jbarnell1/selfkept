import 'dart:typed_data';

import '../models/export.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Exports of the person's own data.
class ExportService {
  final ApiClient _api;

  ExportService(this._api);

  /// Builds the redacted Markdown bundle. A preview is free; an export is
  /// recorded in the audit log on the server.
  Future<PrivacyBundle> bundle(BundleOptions options, {required bool export}) =>
      _api.post<PrivacyBundle>(
        '/export/bundle',
        data: options.toJson(export: export),
        fromJson: (body) => PrivacyBundle.fromJson(asJsonMap(body)),
      );

  /// Everything the person owns, as JSON bytes.
  Future<Uint8List> full() => _api.getBytes('/export/full');
}
