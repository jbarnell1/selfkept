import '../models/household_overview.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Everyone in the household at a glance, each part under its own share.
class HouseholdOverviewService {
  final ApiClient _api;

  HouseholdOverviewService(this._api);

  Future<HouseholdOverview> overview() => _api.get<HouseholdOverview>(
    '/household/overview',
    fromJson: (body) => HouseholdOverview.fromJson(asJsonMap(body)),
  );
}
