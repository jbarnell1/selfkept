import 'package:drift/drift.dart';

part 'outbox_database.g.dart';

/// Samples collected on this phone that the server has not accepted yet.
/// Rows leave the table only after a 200 from POST /ingest/batch.
@DataClassName('PendingSampleRow')
class PendingSamples extends Table {
  @override
  String get tableName => 'pending_sample';

  IntColumn get id => integer().autoIncrement()();

  /// Server source the row belongs to.
  TextColumn get sourceId => text()();
  TextColumn get sourceUid => text()();
  TextColumn get metric => text()();
  DateTimeColumn get start => dateTime()();
  DateTimeColumn get end => dateTime().nullable()();
  RealColumn get value => real()();
  TextColumn get unit => text()();

  /// JSON object, `{}` when there is nothing to say.
  TextColumn get meta => text().withDefault(const Constant('{}'))();

  /// Collector cursor after the collect that produced this row. Sent to the
  /// server once every row from that collect has been accepted.
  TextColumn get cursorAfter => text().nullable()();
  DateTimeColumn get created => dateTime()();
}

/// Workouts waiting for the server, same lifecycle as [PendingSamples].
@DataClassName('PendingWorkoutRow')
class PendingWorkouts extends Table {
  @override
  String get tableName => 'pending_workout';

  IntColumn get id => integer().autoIncrement()();
  TextColumn get sourceId => text()();
  TextColumn get sourceUid => text()();
  TextColumn get type => text()();
  DateTimeColumn get start => dateTime()();
  DateTimeColumn get end => dateTime()();
  RealColumn get distanceM => real().nullable()();
  RealColumn get energyKcal => real().nullable()();
  RealColumn get avgHr => real().nullable()();
  RealColumn get maxHr => real().nullable()();

  /// JSON object or null.
  TextColumn get routeGeoJson => text().nullable()();
  TextColumn get meta => text().withDefault(const Constant('{}'))();
  TextColumn get cursorAfter => text().nullable()();
  DateTimeColumn get created => dateTime()();
}

/// Small key-value store for collector state: the local source id and the
/// last collected cursor. Kept beside the outbox so both survive together.
@DataClassName('SyncStateRow')
class SyncStates extends Table {
  @override
  String get tableName => 'sync_state';

  TextColumn get key => text()();
  TextColumn get value => text().nullable()();

  @override
  Set<Column<Object>> get primaryKey => {key};
}

@DriftDatabase(tables: [PendingSamples, PendingWorkouts, SyncStates])
class OutboxDatabase extends _$OutboxDatabase {
  OutboxDatabase(super.executor);

  @override
  int get schemaVersion => 1;
}
