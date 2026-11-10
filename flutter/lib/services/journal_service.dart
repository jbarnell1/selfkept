import '../models/date_only.dart';
import '../models/journal.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// The journal: short entries about food, drink, symptoms, training, sleep
/// and anything else worth a line.
class JournalService {
  final ApiClient _api;

  JournalService(this._api);

  /// Newest first. Without a range the server returns the last 14 days.
  Future<List<JournalEntry>> list({
    DateTime? from,
    DateTime? to,
    String? personId,
  }) => _api.get<List<JournalEntry>>(
    '/journal',
    queryParameters: {
      if (from != null) 'from': dateOnly(from),
      if (to != null) 'to': dateOnly(to),
      'personId': ?personId,
    },
    fromJson: JournalEntry.listFromJson,
  );

  /// The tags offered as chips, grouped by kind.
  Future<List<JournalTagGroup>> tags() => _api.get<List<JournalTagGroup>>(
    '/journal/tags',
    fromJson: JournalTagGroup.listFromJson,
  );

  Future<JournalEntry> create(JournalDraft draft) => _api.post<JournalEntry>(
    '/journal',
    data: draft.toJson(),
    fromJson: (body) => JournalEntry.fromJson(asJsonMap(body)),
  );

  Future<void> delete(String id) => _api.delete<void>('/journal/$id');
}
