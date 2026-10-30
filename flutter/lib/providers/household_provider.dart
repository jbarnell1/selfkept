import 'package:flutter/foundation.dart';

import '../models/enums.dart';
import '../models/household.dart';
import '../models/share.dart';
import '../services/household_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// Household, invites, the grants I give and the grants I receive.
class HouseholdProvider extends ChangeNotifier with SafeNotifier {
  final HouseholdService _service;
  final AuthProvider _auth;

  Household? _household;
  List<Invite> _invites = const [];
  Map<String, Set<ShareCategory>> _grants = {};
  List<SharedWithMe> _sharedWithMe = const [];
  Invite? _latestInvite;
  bool _loading = false;
  bool _busy = false;
  String? _error;

  HouseholdProvider({required this._service, required this._auth}) {
    _auth.addListener(_onAuthChanged);
  }

  Household? get household => _household;
  List<Invite> get invites => _invites;
  List<SharedWithMe> get sharedWithMe => _sharedWithMe;

  /// The most recently created invite, the only place its plain code exists.
  Invite? get latestInvite => _latestInvite;
  bool get isLoading => _loading;
  bool get isBusy => _busy;
  String? get error => _error;
  bool get hasLoaded => _household != null;

  /// Members other than me, in the order the server gave them.
  List<Member> get otherMembers {
    final myPersonId = _auth.user?.person.id;
    final members = _household?.members ?? const [];
    return [
      for (final m in members)
        if (m.personId != myPersonId) m,
    ];
  }

  Set<ShareCategory> grantsFor(String memberPersonId) =>
      _grants[memberPersonId] ?? const {};

  /// Loads everything the You page shows. Admin-only lists are skipped for
  /// members rather than left to fail.
  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      final results = await Future.wait<Object>([
        _service.getHousehold(),
        _service.getShares(),
        _service.sharedWithMe(),
        if (_auth.isAdmin) _service.listInvites(),
      ]);
      _household = results[0] as Household;
      _grants = {
        for (final g in results[1] as List<ShareGrant>)
          g.memberPersonId: Set.of(g.categories),
      };
      _sharedWithMe = results[2] as List<SharedWithMe>;
      _invites = _auth.isAdmin ? results[3] as List<Invite> : const [];
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  Future<bool> rename(String name) => _run(() async {
    await _service.rename(name);
    final current = _household;
    if (current != null) {
      _household = Household(
        id: current.id,
        name: name.trim(),
        members: current.members,
      );
    }
  });

  Future<Invite?> createInvite() async {
    Invite? created;
    final ok = await _run(() async {
      created = await _service.createInvite();
      _latestInvite = created;
      if (_auth.isAdmin) _invites = await _service.listInvites();
    });
    return ok ? created : null;
  }

  Future<bool> deleteInvite(String id) => _run(() async {
    await _service.deleteInvite(id);
    _invites = [
      for (final i in _invites)
        if (i.id != id) i,
    ];
    if (_latestInvite?.id == id) _latestInvite = null;
  });

  /// Flips one category for one member. Optimistic: the switch moves at once
  /// and moves back if the server refuses.
  Future<bool> setGrant(
    String memberPersonId,
    ShareCategory category,
    bool enabled,
  ) async {
    final before = Set.of(grantsFor(memberPersonId));
    final after = Set.of(before);
    if (enabled) {
      after.add(category);
    } else {
      after.remove(category);
    }
    _grants[memberPersonId] = after;
    _error = null;
    notify();
    try {
      await _service.putShares(memberPersonId, after);
      return true;
    } catch (e) {
      _grants[memberPersonId] = before;
      _error = describeError(e);
      notify();
      return false;
    }
  }

  void clearError() {
    if (_error == null) return;
    _error = null;
    notify();
  }

  Future<bool> _run(Future<void> Function() action) async {
    _busy = true;
    _error = null;
    notify();
    try {
      await action();
      return true;
    } catch (e) {
      _error = describeError(e);
      return false;
    } finally {
      _busy = false;
      notify();
    }
  }

  void _onAuthChanged() {
    if (_auth.isAuthenticated || _household == null) return;
    _household = null;
    _invites = const [];
    _grants = {};
    _sharedWithMe = const [];
    _latestInvite = null;
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
