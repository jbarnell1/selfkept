import '../models/context.dart';
import '../models/household_overview.dart';
import '../models/lab_review.dart';
import '../models/systems.dart';
import '../models/weekly_review.dart';
import '../services/household_overview_service.dart';
import '../services/lab_review_service.dart';
import '../services/review_service.dart';
import '../services/systems_service.dart';
import '../utils/api_error.dart';
import 'person_read.dart';

/// The Systems list and, by key, each system's detail.
class SystemsProvider extends PersonRead<SystemsOverview> {
  final SystemsService _service;

  final Map<String, BodySystem> _details = {};
  final Map<String, String> _detailErrors = {};
  final Set<String> _detailNotShared = {};
  final Set<String> _detailLoading = {};

  SystemsProvider({required this._service, super.auth, super.viewing});

  @override
  Future<SystemsOverview> fetch(String? personId) =>
      _service.systems(personId: personId);

  BodySystem? detail(String key) => _details[key.toLowerCase()];
  String? detailError(String key) => _detailErrors[key.toLowerCase()];
  bool detailNotShared(String key) =>
      _detailNotShared.contains(key.toLowerCase());
  bool detailLoading(String key) => _detailLoading.contains(key.toLowerCase());

  /// The entry from the list, while the detail is on its way.
  BodySystem? summary(String key) {
    for (final s in data?.systems ?? const <BodySystem>[]) {
      if (s.key == key.toLowerCase()) return s;
    }
    return null;
  }

  Future<void> loadSystem(String key) async {
    final k = key.toLowerCase();
    final who = personId;
    _detailLoading.add(k);
    _detailErrors.remove(k);
    _detailNotShared.remove(k);
    notify();
    try {
      final system = await _service.system(k, personId: who);
      if (who != personId || isDisposed) return;
      _details[k] = system;
    } catch (e) {
      if (who != personId || isDisposed) return;
      if (e is ApiException && e.isNotFound) {
        _detailNotShared.add(k);
        _details.remove(k);
      } else {
        _detailErrors[k] = describeError(e);
      }
    }
    _detailLoading.remove(k);
    notify();
  }

  @override
  void clear() {
    _details.clear();
    _detailErrors.clear();
    _detailNotShared.clear();
    _detailLoading.clear();
  }
}

/// Personal patterns: nights after a tag against the rest.
class PatternsProvider extends PersonRead<Patterns> {
  final SystemsService _service;

  PatternsProvider({required this._service, super.auth, super.viewing});

  @override
  Future<Patterns> fetch(String? personId) =>
      _service.patterns(personId: personId);
}

/// Sleep read back night by night.
class SleepNightsProvider extends PersonRead<SleepNights> {
  final SystemsService _service;

  SleepNightsProvider({required this._service, super.auth, super.viewing});

  @override
  Future<SleepNights> fetch(String? personId) =>
      _service.sleep(personId: personId);
}

/// Workouts read back with their weekly totals.
class WorkoutsProvider extends PersonRead<Workouts> {
  final SystemsService _service;

  WorkoutsProvider({required this._service, super.auth, super.viewing});

  @override
  Future<Workouts> fetch(String? personId) =>
      _service.workouts(personId: personId);
}

/// Panels oldest first, with conditions, substances and weight.
class LabTimelineProvider extends PersonRead<LabTimeline> {
  final LabReviewService _service;

  LabTimelineProvider({required this._service, super.auth, super.viewing});

  @override
  Future<LabTimeline> fetch(String? personId) =>
      _service.timeline(personId: personId);
}

/// Everyone in the household at a glance. Not tied to the person switcher:
/// it is always the caller's view of the whole household.
class HouseholdOverviewProvider extends PersonRead<HouseholdOverview> {
  final HouseholdOverviewService _service;

  HouseholdOverviewProvider({required this._service, super.auth});

  @override
  Future<HouseholdOverview> fetch(String? personId) => _service.overview();
}

/// The weekly review, one week at a time.
class WeeklyReviewProvider extends PersonRead<WeeklyReview> {
  final ReviewService _service;

  /// The Monday asked for; null lets the server choose.
  DateTime? _weekOf;

  WeeklyReviewProvider({required this._service, super.auth, super.viewing});

  DateTime? get weekOf => _weekOf;

  @override
  Future<WeeklyReview> fetch(String? personId) =>
      _service.weekly(weekOf: _weekOf, personId: personId);

  /// The week before the one on screen.
  Future<void> previousWeek() => _step(-7);

  /// The week after the one on screen.
  Future<void> nextWeek() => _step(7);

  /// False when the next week would start after [today].
  bool canGoForward(DateTime today) {
    final shown = data?.weekOf;
    if (shown == null) return false;
    final next = DateTime(shown.year, shown.month, shown.day + 7);
    return !next.isAfter(DateTime(today.year, today.month, today.day));
  }

  Future<void> _step(int days) {
    final shown = data?.weekOf ?? _weekOf;
    if (shown == null) return Future.value();
    _weekOf = DateTime(shown.year, shown.month, shown.day + days);
    return load();
  }

  @override
  void clear() => _weekOf = null;
}
