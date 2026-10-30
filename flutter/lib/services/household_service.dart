import '../models/enums.dart';
import '../models/household.dart';
import '../models/share.dart';
import '../utils/json_reader.dart';
import 'api_client.dart';

/// Household, invites and sharing endpoints.
class HouseholdService {
  final ApiClient _api;

  HouseholdService(this._api);

  Future<Household> getHousehold() => _api.get<Household>(
    '/household',
    fromJson: (body) => Household.fromJson(asJsonMap(body)),
  );

  /// Admin only.
  Future<void> rename(String name) =>
      _api.put<void>('/household', data: {'name': name.trim()});

  /// Admin only. The plain code is only ever in this response.
  Future<Invite> createInvite() => _api.post<Invite>(
    '/household/invites',
    data: const <String, dynamic>{},
    fromJson: (body) => Invite.fromJson(asJsonMap(body)),
  );

  /// Admin only. Pending invites, without their codes.
  Future<List<Invite>> listInvites() => _api.get<List<Invite>>(
    '/household/invites',
    fromJson: (body) => [for (final j in asJsonList(body)) Invite.fromJson(j)],
  );

  Future<void> deleteInvite(String id) =>
      _api.delete<void>('/household/invites/$id');

  /// Grants I have given.
  Future<List<ShareGrant>> getShares() => _api.get<List<ShareGrant>>(
    '/household/shares',
    fromJson: (body) => [
      for (final j in asJsonList(body)) ShareGrant.fromJson(j),
    ],
  );

  /// Replaces my grants to one member.
  Future<void> putShares(
    String memberPersonId,
    Set<ShareCategory> categories,
  ) => _api.put<void>(
    '/household/shares/$memberPersonId',
    data: ShareGrant.toPutJson(categories),
  );

  Future<List<SharedWithMe>> sharedWithMe() => _api.get<List<SharedWithMe>>(
    '/household/shared-with-me',
    fromJson: (body) => [
      for (final j in asJsonList(body)) SharedWithMe.fromJson(j),
    ],
  );
}
