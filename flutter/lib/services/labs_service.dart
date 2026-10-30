import 'dart:typed_data';

import '../models/lab.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Lab panels: parse pasted or scanned lines, store confirmed reports, read
/// the marker catalogue with the person's latest values, and fetch the
/// clinician summary.
class LabsService {
  final ApiClient _api;

  LabsService(this._api);

  /// Matches [text] (OCR lines joined by newlines) to markers. Nothing is
  /// stored.
  Future<ParseResult> parse(String text) => _api.post<ParseResult>(
    '/labs/parse',
    data: {'text': text},
    fromJson: (body) => ParseResult.fromJson(asJsonMap(body)),
  );

  /// Stores a confirmed panel in one call.
  Future<LabReport> createReport({
    required String provider,
    required DateTime collectedOn,
    required List<LabResultInput> results,
    String? imageBase64,
    String? notes,
  }) => _api.post<LabReport>(
    '/labs/reports',
    data: {
      'provider': provider,
      'collectedOn': labDateOnly(collectedOn),
      'results': [for (final r in results) r.toJson()],
      'image': ?imageBase64,
      'notes': ?notes,
    },
    fromJson: (body) => LabReport.fromJson(asJsonMap(body)),
  );

  /// Newest first.
  Future<List<LabReport>> reports({String? personId}) =>
      _api.get<List<LabReport>>(
        '/labs/reports',
        queryParameters: {'personId': ?personId},
        fromJson: LabReport.listFromJson,
      );

  Future<void> deleteReport(String id) =>
      _api.delete<void>('/labs/reports/$id');

  /// The catalogue in panel order, with latest values.
  Future<List<LabMarker>> markers({String? personId}) =>
      _api.get<List<LabMarker>>(
        '/labs/markers',
        queryParameters: {'personId': ?personId},
        fromJson: LabMarker.listFromJson,
      );

  Future<LabMarkerDetail> marker(String metricKey, {String? personId}) =>
      _api.get<LabMarkerDetail>(
        '/labs/markers/$metricKey',
        queryParameters: {'personId': ?personId},
        fromJson: (body) => LabMarkerDetail.fromJson(asJsonMap(body)),
      );

  /// The two-page clinician PDF. Dates are sent as yyyy-MM-dd.
  Future<Uint8List> summaryPdf({
    DateTime? from,
    DateTime? to,
    String? personId,
  }) => _api.getBytes(
    '/labs/summary.pdf',
    queryParameters: {
      'from': ?(from == null ? null : labDateOnly(from)),
      'to': ?(to == null ? null : labDateOnly(to)),
      'personId': ?personId,
    },
  );
}
