// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'outbox_database.dart';

// ignore_for_file: type=lint
class $PendingSamplesTable extends PendingSamples
    with TableInfo<$PendingSamplesTable, PendingSampleRow> {
  @override
  final GeneratedDatabase attachedDatabase;
  final String? _alias;
  $PendingSamplesTable(this.attachedDatabase, [this._alias]);
  static const VerificationMeta _idMeta = const VerificationMeta('id');
  @override
  late final GeneratedColumn<int> id = GeneratedColumn<int>(
    'id',
    aliasedName,
    false,
    hasAutoIncrement: true,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
    defaultConstraints: GeneratedColumn.constraintIsAlways(
      'PRIMARY KEY AUTOINCREMENT',
    ),
  );
  static const VerificationMeta _sourceIdMeta = const VerificationMeta(
    'sourceId',
  );
  @override
  late final GeneratedColumn<String> sourceId = GeneratedColumn<String>(
    'source_id',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _sourceUidMeta = const VerificationMeta(
    'sourceUid',
  );
  @override
  late final GeneratedColumn<String> sourceUid = GeneratedColumn<String>(
    'source_uid',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _metricMeta = const VerificationMeta('metric');
  @override
  late final GeneratedColumn<String> metric = GeneratedColumn<String>(
    'metric',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _startMeta = const VerificationMeta('start');
  @override
  late final GeneratedColumn<DateTime> start = GeneratedColumn<DateTime>(
    'start',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _endMeta = const VerificationMeta('end');
  @override
  late final GeneratedColumn<DateTime> end = GeneratedColumn<DateTime>(
    'end',
    aliasedName,
    true,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _valueMeta = const VerificationMeta('value');
  @override
  late final GeneratedColumn<double> value = GeneratedColumn<double>(
    'value',
    aliasedName,
    false,
    type: DriftSqlType.double,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _unitMeta = const VerificationMeta('unit');
  @override
  late final GeneratedColumn<String> unit = GeneratedColumn<String>(
    'unit',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _metaMeta = const VerificationMeta('meta');
  @override
  late final GeneratedColumn<String> meta = GeneratedColumn<String>(
    'meta',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
    defaultValue: const Constant('{}'),
  );
  static const VerificationMeta _cursorAfterMeta = const VerificationMeta(
    'cursorAfter',
  );
  @override
  late final GeneratedColumn<String> cursorAfter = GeneratedColumn<String>(
    'cursor_after',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _createdMeta = const VerificationMeta(
    'created',
  );
  @override
  late final GeneratedColumn<DateTime> created = GeneratedColumn<DateTime>(
    'created',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  @override
  List<GeneratedColumn> get $columns => [
    id,
    sourceId,
    sourceUid,
    metric,
    start,
    end,
    value,
    unit,
    meta,
    cursorAfter,
    created,
  ];
  @override
  String get aliasedName => _alias ?? actualTableName;
  @override
  String get actualTableName => $name;
  static const String $name = 'pending_sample';
  @override
  VerificationContext validateIntegrity(
    Insertable<PendingSampleRow> instance, {
    bool isInserting = false,
  }) {
    final context = VerificationContext();
    final data = instance.toColumns(true);
    if (data.containsKey('id')) {
      context.handle(_idMeta, id.isAcceptableOrUnknown(data['id']!, _idMeta));
    }
    if (data.containsKey('source_id')) {
      context.handle(
        _sourceIdMeta,
        sourceId.isAcceptableOrUnknown(data['source_id']!, _sourceIdMeta),
      );
    } else if (isInserting) {
      context.missing(_sourceIdMeta);
    }
    if (data.containsKey('source_uid')) {
      context.handle(
        _sourceUidMeta,
        sourceUid.isAcceptableOrUnknown(data['source_uid']!, _sourceUidMeta),
      );
    } else if (isInserting) {
      context.missing(_sourceUidMeta);
    }
    if (data.containsKey('metric')) {
      context.handle(
        _metricMeta,
        metric.isAcceptableOrUnknown(data['metric']!, _metricMeta),
      );
    } else if (isInserting) {
      context.missing(_metricMeta);
    }
    if (data.containsKey('start')) {
      context.handle(
        _startMeta,
        start.isAcceptableOrUnknown(data['start']!, _startMeta),
      );
    } else if (isInserting) {
      context.missing(_startMeta);
    }
    if (data.containsKey('end')) {
      context.handle(
        _endMeta,
        end.isAcceptableOrUnknown(data['end']!, _endMeta),
      );
    }
    if (data.containsKey('value')) {
      context.handle(
        _valueMeta,
        value.isAcceptableOrUnknown(data['value']!, _valueMeta),
      );
    } else if (isInserting) {
      context.missing(_valueMeta);
    }
    if (data.containsKey('unit')) {
      context.handle(
        _unitMeta,
        unit.isAcceptableOrUnknown(data['unit']!, _unitMeta),
      );
    } else if (isInserting) {
      context.missing(_unitMeta);
    }
    if (data.containsKey('meta')) {
      context.handle(
        _metaMeta,
        meta.isAcceptableOrUnknown(data['meta']!, _metaMeta),
      );
    }
    if (data.containsKey('cursor_after')) {
      context.handle(
        _cursorAfterMeta,
        cursorAfter.isAcceptableOrUnknown(
          data['cursor_after']!,
          _cursorAfterMeta,
        ),
      );
    }
    if (data.containsKey('created')) {
      context.handle(
        _createdMeta,
        created.isAcceptableOrUnknown(data['created']!, _createdMeta),
      );
    } else if (isInserting) {
      context.missing(_createdMeta);
    }
    return context;
  }

  @override
  Set<GeneratedColumn> get $primaryKey => {id};
  @override
  PendingSampleRow map(Map<String, dynamic> data, {String? tablePrefix}) {
    final effectivePrefix = tablePrefix != null ? '$tablePrefix.' : '';
    return PendingSampleRow(
      id: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}id'],
      )!,
      sourceId: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}source_id'],
      )!,
      sourceUid: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}source_uid'],
      )!,
      metric: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}metric'],
      )!,
      start: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}start'],
      )!,
      end: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}end'],
      ),
      value: attachedDatabase.typeMapping.read(
        DriftSqlType.double,
        data['${effectivePrefix}value'],
      )!,
      unit: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}unit'],
      )!,
      meta: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}meta'],
      )!,
      cursorAfter: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}cursor_after'],
      ),
      created: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}created'],
      )!,
    );
  }

  @override
  $PendingSamplesTable createAlias(String alias) {
    return $PendingSamplesTable(attachedDatabase, alias);
  }
}

class PendingSampleRow extends DataClass
    implements Insertable<PendingSampleRow> {
  final int id;

  /// Server source the row belongs to.
  final String sourceId;
  final String sourceUid;
  final String metric;
  final DateTime start;
  final DateTime? end;
  final double value;
  final String unit;

  /// JSON object, `{}` when there is nothing to say.
  final String meta;

  /// Collector cursor after the collect that produced this row. Sent to the
  /// server once every row from that collect has been accepted.
  final String? cursorAfter;
  final DateTime created;
  const PendingSampleRow({
    required this.id,
    required this.sourceId,
    required this.sourceUid,
    required this.metric,
    required this.start,
    this.end,
    required this.value,
    required this.unit,
    required this.meta,
    this.cursorAfter,
    required this.created,
  });
  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    map['id'] = Variable<int>(id);
    map['source_id'] = Variable<String>(sourceId);
    map['source_uid'] = Variable<String>(sourceUid);
    map['metric'] = Variable<String>(metric);
    map['start'] = Variable<DateTime>(start);
    if (!nullToAbsent || end != null) {
      map['end'] = Variable<DateTime>(end);
    }
    map['value'] = Variable<double>(value);
    map['unit'] = Variable<String>(unit);
    map['meta'] = Variable<String>(meta);
    if (!nullToAbsent || cursorAfter != null) {
      map['cursor_after'] = Variable<String>(cursorAfter);
    }
    map['created'] = Variable<DateTime>(created);
    return map;
  }

  PendingSamplesCompanion toCompanion(bool nullToAbsent) {
    return PendingSamplesCompanion(
      id: Value(id),
      sourceId: Value(sourceId),
      sourceUid: Value(sourceUid),
      metric: Value(metric),
      start: Value(start),
      end: end == null && nullToAbsent ? const Value.absent() : Value(end),
      value: Value(value),
      unit: Value(unit),
      meta: Value(meta),
      cursorAfter: cursorAfter == null && nullToAbsent
          ? const Value.absent()
          : Value(cursorAfter),
      created: Value(created),
    );
  }

  factory PendingSampleRow.fromJson(
    Map<String, dynamic> json, {
    ValueSerializer? serializer,
  }) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return PendingSampleRow(
      id: serializer.fromJson<int>(json['id']),
      sourceId: serializer.fromJson<String>(json['sourceId']),
      sourceUid: serializer.fromJson<String>(json['sourceUid']),
      metric: serializer.fromJson<String>(json['metric']),
      start: serializer.fromJson<DateTime>(json['start']),
      end: serializer.fromJson<DateTime?>(json['end']),
      value: serializer.fromJson<double>(json['value']),
      unit: serializer.fromJson<String>(json['unit']),
      meta: serializer.fromJson<String>(json['meta']),
      cursorAfter: serializer.fromJson<String?>(json['cursorAfter']),
      created: serializer.fromJson<DateTime>(json['created']),
    );
  }
  @override
  Map<String, dynamic> toJson({ValueSerializer? serializer}) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return <String, dynamic>{
      'id': serializer.toJson<int>(id),
      'sourceId': serializer.toJson<String>(sourceId),
      'sourceUid': serializer.toJson<String>(sourceUid),
      'metric': serializer.toJson<String>(metric),
      'start': serializer.toJson<DateTime>(start),
      'end': serializer.toJson<DateTime?>(end),
      'value': serializer.toJson<double>(value),
      'unit': serializer.toJson<String>(unit),
      'meta': serializer.toJson<String>(meta),
      'cursorAfter': serializer.toJson<String?>(cursorAfter),
      'created': serializer.toJson<DateTime>(created),
    };
  }

  PendingSampleRow copyWith({
    int? id,
    String? sourceId,
    String? sourceUid,
    String? metric,
    DateTime? start,
    Value<DateTime?> end = const Value.absent(),
    double? value,
    String? unit,
    String? meta,
    Value<String?> cursorAfter = const Value.absent(),
    DateTime? created,
  }) => PendingSampleRow(
    id: id ?? this.id,
    sourceId: sourceId ?? this.sourceId,
    sourceUid: sourceUid ?? this.sourceUid,
    metric: metric ?? this.metric,
    start: start ?? this.start,
    end: end.present ? end.value : this.end,
    value: value ?? this.value,
    unit: unit ?? this.unit,
    meta: meta ?? this.meta,
    cursorAfter: cursorAfter.present ? cursorAfter.value : this.cursorAfter,
    created: created ?? this.created,
  );
  PendingSampleRow copyWithCompanion(PendingSamplesCompanion data) {
    return PendingSampleRow(
      id: data.id.present ? data.id.value : this.id,
      sourceId: data.sourceId.present ? data.sourceId.value : this.sourceId,
      sourceUid: data.sourceUid.present ? data.sourceUid.value : this.sourceUid,
      metric: data.metric.present ? data.metric.value : this.metric,
      start: data.start.present ? data.start.value : this.start,
      end: data.end.present ? data.end.value : this.end,
      value: data.value.present ? data.value.value : this.value,
      unit: data.unit.present ? data.unit.value : this.unit,
      meta: data.meta.present ? data.meta.value : this.meta,
      cursorAfter: data.cursorAfter.present
          ? data.cursorAfter.value
          : this.cursorAfter,
      created: data.created.present ? data.created.value : this.created,
    );
  }

  @override
  String toString() {
    return (StringBuffer('PendingSampleRow(')
          ..write('id: $id, ')
          ..write('sourceId: $sourceId, ')
          ..write('sourceUid: $sourceUid, ')
          ..write('metric: $metric, ')
          ..write('start: $start, ')
          ..write('end: $end, ')
          ..write('value: $value, ')
          ..write('unit: $unit, ')
          ..write('meta: $meta, ')
          ..write('cursorAfter: $cursorAfter, ')
          ..write('created: $created')
          ..write(')'))
        .toString();
  }

  @override
  int get hashCode => Object.hash(
    id,
    sourceId,
    sourceUid,
    metric,
    start,
    end,
    value,
    unit,
    meta,
    cursorAfter,
    created,
  );
  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      (other is PendingSampleRow &&
          other.id == this.id &&
          other.sourceId == this.sourceId &&
          other.sourceUid == this.sourceUid &&
          other.metric == this.metric &&
          other.start == this.start &&
          other.end == this.end &&
          other.value == this.value &&
          other.unit == this.unit &&
          other.meta == this.meta &&
          other.cursorAfter == this.cursorAfter &&
          other.created == this.created);
}

class PendingSamplesCompanion extends UpdateCompanion<PendingSampleRow> {
  final Value<int> id;
  final Value<String> sourceId;
  final Value<String> sourceUid;
  final Value<String> metric;
  final Value<DateTime> start;
  final Value<DateTime?> end;
  final Value<double> value;
  final Value<String> unit;
  final Value<String> meta;
  final Value<String?> cursorAfter;
  final Value<DateTime> created;
  const PendingSamplesCompanion({
    this.id = const Value.absent(),
    this.sourceId = const Value.absent(),
    this.sourceUid = const Value.absent(),
    this.metric = const Value.absent(),
    this.start = const Value.absent(),
    this.end = const Value.absent(),
    this.value = const Value.absent(),
    this.unit = const Value.absent(),
    this.meta = const Value.absent(),
    this.cursorAfter = const Value.absent(),
    this.created = const Value.absent(),
  });
  PendingSamplesCompanion.insert({
    this.id = const Value.absent(),
    required String sourceId,
    required String sourceUid,
    required String metric,
    required DateTime start,
    this.end = const Value.absent(),
    required double value,
    required String unit,
    this.meta = const Value.absent(),
    this.cursorAfter = const Value.absent(),
    required DateTime created,
  }) : sourceId = Value(sourceId),
       sourceUid = Value(sourceUid),
       metric = Value(metric),
       start = Value(start),
       value = Value(value),
       unit = Value(unit),
       created = Value(created);
  static Insertable<PendingSampleRow> custom({
    Expression<int>? id,
    Expression<String>? sourceId,
    Expression<String>? sourceUid,
    Expression<String>? metric,
    Expression<DateTime>? start,
    Expression<DateTime>? end,
    Expression<double>? value,
    Expression<String>? unit,
    Expression<String>? meta,
    Expression<String>? cursorAfter,
    Expression<DateTime>? created,
  }) {
    return RawValuesInsertable({
      if (id != null) 'id': id,
      if (sourceId != null) 'source_id': sourceId,
      if (sourceUid != null) 'source_uid': sourceUid,
      if (metric != null) 'metric': metric,
      if (start != null) 'start': start,
      if (end != null) 'end': end,
      if (value != null) 'value': value,
      if (unit != null) 'unit': unit,
      if (meta != null) 'meta': meta,
      if (cursorAfter != null) 'cursor_after': cursorAfter,
      if (created != null) 'created': created,
    });
  }

  PendingSamplesCompanion copyWith({
    Value<int>? id,
    Value<String>? sourceId,
    Value<String>? sourceUid,
    Value<String>? metric,
    Value<DateTime>? start,
    Value<DateTime?>? end,
    Value<double>? value,
    Value<String>? unit,
    Value<String>? meta,
    Value<String?>? cursorAfter,
    Value<DateTime>? created,
  }) {
    return PendingSamplesCompanion(
      id: id ?? this.id,
      sourceId: sourceId ?? this.sourceId,
      sourceUid: sourceUid ?? this.sourceUid,
      metric: metric ?? this.metric,
      start: start ?? this.start,
      end: end ?? this.end,
      value: value ?? this.value,
      unit: unit ?? this.unit,
      meta: meta ?? this.meta,
      cursorAfter: cursorAfter ?? this.cursorAfter,
      created: created ?? this.created,
    );
  }

  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    if (id.present) {
      map['id'] = Variable<int>(id.value);
    }
    if (sourceId.present) {
      map['source_id'] = Variable<String>(sourceId.value);
    }
    if (sourceUid.present) {
      map['source_uid'] = Variable<String>(sourceUid.value);
    }
    if (metric.present) {
      map['metric'] = Variable<String>(metric.value);
    }
    if (start.present) {
      map['start'] = Variable<DateTime>(start.value);
    }
    if (end.present) {
      map['end'] = Variable<DateTime>(end.value);
    }
    if (value.present) {
      map['value'] = Variable<double>(value.value);
    }
    if (unit.present) {
      map['unit'] = Variable<String>(unit.value);
    }
    if (meta.present) {
      map['meta'] = Variable<String>(meta.value);
    }
    if (cursorAfter.present) {
      map['cursor_after'] = Variable<String>(cursorAfter.value);
    }
    if (created.present) {
      map['created'] = Variable<DateTime>(created.value);
    }
    return map;
  }

  @override
  String toString() {
    return (StringBuffer('PendingSamplesCompanion(')
          ..write('id: $id, ')
          ..write('sourceId: $sourceId, ')
          ..write('sourceUid: $sourceUid, ')
          ..write('metric: $metric, ')
          ..write('start: $start, ')
          ..write('end: $end, ')
          ..write('value: $value, ')
          ..write('unit: $unit, ')
          ..write('meta: $meta, ')
          ..write('cursorAfter: $cursorAfter, ')
          ..write('created: $created')
          ..write(')'))
        .toString();
  }
}

class $PendingWorkoutsTable extends PendingWorkouts
    with TableInfo<$PendingWorkoutsTable, PendingWorkoutRow> {
  @override
  final GeneratedDatabase attachedDatabase;
  final String? _alias;
  $PendingWorkoutsTable(this.attachedDatabase, [this._alias]);
  static const VerificationMeta _idMeta = const VerificationMeta('id');
  @override
  late final GeneratedColumn<int> id = GeneratedColumn<int>(
    'id',
    aliasedName,
    false,
    hasAutoIncrement: true,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
    defaultConstraints: GeneratedColumn.constraintIsAlways(
      'PRIMARY KEY AUTOINCREMENT',
    ),
  );
  static const VerificationMeta _sourceIdMeta = const VerificationMeta(
    'sourceId',
  );
  @override
  late final GeneratedColumn<String> sourceId = GeneratedColumn<String>(
    'source_id',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _sourceUidMeta = const VerificationMeta(
    'sourceUid',
  );
  @override
  late final GeneratedColumn<String> sourceUid = GeneratedColumn<String>(
    'source_uid',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _typeMeta = const VerificationMeta('type');
  @override
  late final GeneratedColumn<String> type = GeneratedColumn<String>(
    'type',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _startMeta = const VerificationMeta('start');
  @override
  late final GeneratedColumn<DateTime> start = GeneratedColumn<DateTime>(
    'start',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _endMeta = const VerificationMeta('end');
  @override
  late final GeneratedColumn<DateTime> end = GeneratedColumn<DateTime>(
    'end',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _distanceMMeta = const VerificationMeta(
    'distanceM',
  );
  @override
  late final GeneratedColumn<double> distanceM = GeneratedColumn<double>(
    'distance_m',
    aliasedName,
    true,
    type: DriftSqlType.double,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _energyKcalMeta = const VerificationMeta(
    'energyKcal',
  );
  @override
  late final GeneratedColumn<double> energyKcal = GeneratedColumn<double>(
    'energy_kcal',
    aliasedName,
    true,
    type: DriftSqlType.double,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _avgHrMeta = const VerificationMeta('avgHr');
  @override
  late final GeneratedColumn<double> avgHr = GeneratedColumn<double>(
    'avg_hr',
    aliasedName,
    true,
    type: DriftSqlType.double,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _maxHrMeta = const VerificationMeta('maxHr');
  @override
  late final GeneratedColumn<double> maxHr = GeneratedColumn<double>(
    'max_hr',
    aliasedName,
    true,
    type: DriftSqlType.double,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _routeGeoJsonMeta = const VerificationMeta(
    'routeGeoJson',
  );
  @override
  late final GeneratedColumn<String> routeGeoJson = GeneratedColumn<String>(
    'route_geo_json',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _metaMeta = const VerificationMeta('meta');
  @override
  late final GeneratedColumn<String> meta = GeneratedColumn<String>(
    'meta',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
    defaultValue: const Constant('{}'),
  );
  static const VerificationMeta _cursorAfterMeta = const VerificationMeta(
    'cursorAfter',
  );
  @override
  late final GeneratedColumn<String> cursorAfter = GeneratedColumn<String>(
    'cursor_after',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _createdMeta = const VerificationMeta(
    'created',
  );
  @override
  late final GeneratedColumn<DateTime> created = GeneratedColumn<DateTime>(
    'created',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  @override
  List<GeneratedColumn> get $columns => [
    id,
    sourceId,
    sourceUid,
    type,
    start,
    end,
    distanceM,
    energyKcal,
    avgHr,
    maxHr,
    routeGeoJson,
    meta,
    cursorAfter,
    created,
  ];
  @override
  String get aliasedName => _alias ?? actualTableName;
  @override
  String get actualTableName => $name;
  static const String $name = 'pending_workout';
  @override
  VerificationContext validateIntegrity(
    Insertable<PendingWorkoutRow> instance, {
    bool isInserting = false,
  }) {
    final context = VerificationContext();
    final data = instance.toColumns(true);
    if (data.containsKey('id')) {
      context.handle(_idMeta, id.isAcceptableOrUnknown(data['id']!, _idMeta));
    }
    if (data.containsKey('source_id')) {
      context.handle(
        _sourceIdMeta,
        sourceId.isAcceptableOrUnknown(data['source_id']!, _sourceIdMeta),
      );
    } else if (isInserting) {
      context.missing(_sourceIdMeta);
    }
    if (data.containsKey('source_uid')) {
      context.handle(
        _sourceUidMeta,
        sourceUid.isAcceptableOrUnknown(data['source_uid']!, _sourceUidMeta),
      );
    } else if (isInserting) {
      context.missing(_sourceUidMeta);
    }
    if (data.containsKey('type')) {
      context.handle(
        _typeMeta,
        type.isAcceptableOrUnknown(data['type']!, _typeMeta),
      );
    } else if (isInserting) {
      context.missing(_typeMeta);
    }
    if (data.containsKey('start')) {
      context.handle(
        _startMeta,
        start.isAcceptableOrUnknown(data['start']!, _startMeta),
      );
    } else if (isInserting) {
      context.missing(_startMeta);
    }
    if (data.containsKey('end')) {
      context.handle(
        _endMeta,
        end.isAcceptableOrUnknown(data['end']!, _endMeta),
      );
    } else if (isInserting) {
      context.missing(_endMeta);
    }
    if (data.containsKey('distance_m')) {
      context.handle(
        _distanceMMeta,
        distanceM.isAcceptableOrUnknown(data['distance_m']!, _distanceMMeta),
      );
    }
    if (data.containsKey('energy_kcal')) {
      context.handle(
        _energyKcalMeta,
        energyKcal.isAcceptableOrUnknown(data['energy_kcal']!, _energyKcalMeta),
      );
    }
    if (data.containsKey('avg_hr')) {
      context.handle(
        _avgHrMeta,
        avgHr.isAcceptableOrUnknown(data['avg_hr']!, _avgHrMeta),
      );
    }
    if (data.containsKey('max_hr')) {
      context.handle(
        _maxHrMeta,
        maxHr.isAcceptableOrUnknown(data['max_hr']!, _maxHrMeta),
      );
    }
    if (data.containsKey('route_geo_json')) {
      context.handle(
        _routeGeoJsonMeta,
        routeGeoJson.isAcceptableOrUnknown(
          data['route_geo_json']!,
          _routeGeoJsonMeta,
        ),
      );
    }
    if (data.containsKey('meta')) {
      context.handle(
        _metaMeta,
        meta.isAcceptableOrUnknown(data['meta']!, _metaMeta),
      );
    }
    if (data.containsKey('cursor_after')) {
      context.handle(
        _cursorAfterMeta,
        cursorAfter.isAcceptableOrUnknown(
          data['cursor_after']!,
          _cursorAfterMeta,
        ),
      );
    }
    if (data.containsKey('created')) {
      context.handle(
        _createdMeta,
        created.isAcceptableOrUnknown(data['created']!, _createdMeta),
      );
    } else if (isInserting) {
      context.missing(_createdMeta);
    }
    return context;
  }

  @override
  Set<GeneratedColumn> get $primaryKey => {id};
  @override
  PendingWorkoutRow map(Map<String, dynamic> data, {String? tablePrefix}) {
    final effectivePrefix = tablePrefix != null ? '$tablePrefix.' : '';
    return PendingWorkoutRow(
      id: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}id'],
      )!,
      sourceId: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}source_id'],
      )!,
      sourceUid: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}source_uid'],
      )!,
      type: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}type'],
      )!,
      start: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}start'],
      )!,
      end: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}end'],
      )!,
      distanceM: attachedDatabase.typeMapping.read(
        DriftSqlType.double,
        data['${effectivePrefix}distance_m'],
      ),
      energyKcal: attachedDatabase.typeMapping.read(
        DriftSqlType.double,
        data['${effectivePrefix}energy_kcal'],
      ),
      avgHr: attachedDatabase.typeMapping.read(
        DriftSqlType.double,
        data['${effectivePrefix}avg_hr'],
      ),
      maxHr: attachedDatabase.typeMapping.read(
        DriftSqlType.double,
        data['${effectivePrefix}max_hr'],
      ),
      routeGeoJson: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}route_geo_json'],
      ),
      meta: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}meta'],
      )!,
      cursorAfter: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}cursor_after'],
      ),
      created: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}created'],
      )!,
    );
  }

  @override
  $PendingWorkoutsTable createAlias(String alias) {
    return $PendingWorkoutsTable(attachedDatabase, alias);
  }
}

class PendingWorkoutRow extends DataClass
    implements Insertable<PendingWorkoutRow> {
  final int id;
  final String sourceId;
  final String sourceUid;
  final String type;
  final DateTime start;
  final DateTime end;
  final double? distanceM;
  final double? energyKcal;
  final double? avgHr;
  final double? maxHr;

  /// JSON object or null.
  final String? routeGeoJson;
  final String meta;
  final String? cursorAfter;
  final DateTime created;
  const PendingWorkoutRow({
    required this.id,
    required this.sourceId,
    required this.sourceUid,
    required this.type,
    required this.start,
    required this.end,
    this.distanceM,
    this.energyKcal,
    this.avgHr,
    this.maxHr,
    this.routeGeoJson,
    required this.meta,
    this.cursorAfter,
    required this.created,
  });
  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    map['id'] = Variable<int>(id);
    map['source_id'] = Variable<String>(sourceId);
    map['source_uid'] = Variable<String>(sourceUid);
    map['type'] = Variable<String>(type);
    map['start'] = Variable<DateTime>(start);
    map['end'] = Variable<DateTime>(end);
    if (!nullToAbsent || distanceM != null) {
      map['distance_m'] = Variable<double>(distanceM);
    }
    if (!nullToAbsent || energyKcal != null) {
      map['energy_kcal'] = Variable<double>(energyKcal);
    }
    if (!nullToAbsent || avgHr != null) {
      map['avg_hr'] = Variable<double>(avgHr);
    }
    if (!nullToAbsent || maxHr != null) {
      map['max_hr'] = Variable<double>(maxHr);
    }
    if (!nullToAbsent || routeGeoJson != null) {
      map['route_geo_json'] = Variable<String>(routeGeoJson);
    }
    map['meta'] = Variable<String>(meta);
    if (!nullToAbsent || cursorAfter != null) {
      map['cursor_after'] = Variable<String>(cursorAfter);
    }
    map['created'] = Variable<DateTime>(created);
    return map;
  }

  PendingWorkoutsCompanion toCompanion(bool nullToAbsent) {
    return PendingWorkoutsCompanion(
      id: Value(id),
      sourceId: Value(sourceId),
      sourceUid: Value(sourceUid),
      type: Value(type),
      start: Value(start),
      end: Value(end),
      distanceM: distanceM == null && nullToAbsent
          ? const Value.absent()
          : Value(distanceM),
      energyKcal: energyKcal == null && nullToAbsent
          ? const Value.absent()
          : Value(energyKcal),
      avgHr: avgHr == null && nullToAbsent
          ? const Value.absent()
          : Value(avgHr),
      maxHr: maxHr == null && nullToAbsent
          ? const Value.absent()
          : Value(maxHr),
      routeGeoJson: routeGeoJson == null && nullToAbsent
          ? const Value.absent()
          : Value(routeGeoJson),
      meta: Value(meta),
      cursorAfter: cursorAfter == null && nullToAbsent
          ? const Value.absent()
          : Value(cursorAfter),
      created: Value(created),
    );
  }

  factory PendingWorkoutRow.fromJson(
    Map<String, dynamic> json, {
    ValueSerializer? serializer,
  }) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return PendingWorkoutRow(
      id: serializer.fromJson<int>(json['id']),
      sourceId: serializer.fromJson<String>(json['sourceId']),
      sourceUid: serializer.fromJson<String>(json['sourceUid']),
      type: serializer.fromJson<String>(json['type']),
      start: serializer.fromJson<DateTime>(json['start']),
      end: serializer.fromJson<DateTime>(json['end']),
      distanceM: serializer.fromJson<double?>(json['distanceM']),
      energyKcal: serializer.fromJson<double?>(json['energyKcal']),
      avgHr: serializer.fromJson<double?>(json['avgHr']),
      maxHr: serializer.fromJson<double?>(json['maxHr']),
      routeGeoJson: serializer.fromJson<String?>(json['routeGeoJson']),
      meta: serializer.fromJson<String>(json['meta']),
      cursorAfter: serializer.fromJson<String?>(json['cursorAfter']),
      created: serializer.fromJson<DateTime>(json['created']),
    );
  }
  @override
  Map<String, dynamic> toJson({ValueSerializer? serializer}) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return <String, dynamic>{
      'id': serializer.toJson<int>(id),
      'sourceId': serializer.toJson<String>(sourceId),
      'sourceUid': serializer.toJson<String>(sourceUid),
      'type': serializer.toJson<String>(type),
      'start': serializer.toJson<DateTime>(start),
      'end': serializer.toJson<DateTime>(end),
      'distanceM': serializer.toJson<double?>(distanceM),
      'energyKcal': serializer.toJson<double?>(energyKcal),
      'avgHr': serializer.toJson<double?>(avgHr),
      'maxHr': serializer.toJson<double?>(maxHr),
      'routeGeoJson': serializer.toJson<String?>(routeGeoJson),
      'meta': serializer.toJson<String>(meta),
      'cursorAfter': serializer.toJson<String?>(cursorAfter),
      'created': serializer.toJson<DateTime>(created),
    };
  }

  PendingWorkoutRow copyWith({
    int? id,
    String? sourceId,
    String? sourceUid,
    String? type,
    DateTime? start,
    DateTime? end,
    Value<double?> distanceM = const Value.absent(),
    Value<double?> energyKcal = const Value.absent(),
    Value<double?> avgHr = const Value.absent(),
    Value<double?> maxHr = const Value.absent(),
    Value<String?> routeGeoJson = const Value.absent(),
    String? meta,
    Value<String?> cursorAfter = const Value.absent(),
    DateTime? created,
  }) => PendingWorkoutRow(
    id: id ?? this.id,
    sourceId: sourceId ?? this.sourceId,
    sourceUid: sourceUid ?? this.sourceUid,
    type: type ?? this.type,
    start: start ?? this.start,
    end: end ?? this.end,
    distanceM: distanceM.present ? distanceM.value : this.distanceM,
    energyKcal: energyKcal.present ? energyKcal.value : this.energyKcal,
    avgHr: avgHr.present ? avgHr.value : this.avgHr,
    maxHr: maxHr.present ? maxHr.value : this.maxHr,
    routeGeoJson: routeGeoJson.present ? routeGeoJson.value : this.routeGeoJson,
    meta: meta ?? this.meta,
    cursorAfter: cursorAfter.present ? cursorAfter.value : this.cursorAfter,
    created: created ?? this.created,
  );
  PendingWorkoutRow copyWithCompanion(PendingWorkoutsCompanion data) {
    return PendingWorkoutRow(
      id: data.id.present ? data.id.value : this.id,
      sourceId: data.sourceId.present ? data.sourceId.value : this.sourceId,
      sourceUid: data.sourceUid.present ? data.sourceUid.value : this.sourceUid,
      type: data.type.present ? data.type.value : this.type,
      start: data.start.present ? data.start.value : this.start,
      end: data.end.present ? data.end.value : this.end,
      distanceM: data.distanceM.present ? data.distanceM.value : this.distanceM,
      energyKcal: data.energyKcal.present
          ? data.energyKcal.value
          : this.energyKcal,
      avgHr: data.avgHr.present ? data.avgHr.value : this.avgHr,
      maxHr: data.maxHr.present ? data.maxHr.value : this.maxHr,
      routeGeoJson: data.routeGeoJson.present
          ? data.routeGeoJson.value
          : this.routeGeoJson,
      meta: data.meta.present ? data.meta.value : this.meta,
      cursorAfter: data.cursorAfter.present
          ? data.cursorAfter.value
          : this.cursorAfter,
      created: data.created.present ? data.created.value : this.created,
    );
  }

  @override
  String toString() {
    return (StringBuffer('PendingWorkoutRow(')
          ..write('id: $id, ')
          ..write('sourceId: $sourceId, ')
          ..write('sourceUid: $sourceUid, ')
          ..write('type: $type, ')
          ..write('start: $start, ')
          ..write('end: $end, ')
          ..write('distanceM: $distanceM, ')
          ..write('energyKcal: $energyKcal, ')
          ..write('avgHr: $avgHr, ')
          ..write('maxHr: $maxHr, ')
          ..write('routeGeoJson: $routeGeoJson, ')
          ..write('meta: $meta, ')
          ..write('cursorAfter: $cursorAfter, ')
          ..write('created: $created')
          ..write(')'))
        .toString();
  }

  @override
  int get hashCode => Object.hash(
    id,
    sourceId,
    sourceUid,
    type,
    start,
    end,
    distanceM,
    energyKcal,
    avgHr,
    maxHr,
    routeGeoJson,
    meta,
    cursorAfter,
    created,
  );
  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      (other is PendingWorkoutRow &&
          other.id == this.id &&
          other.sourceId == this.sourceId &&
          other.sourceUid == this.sourceUid &&
          other.type == this.type &&
          other.start == this.start &&
          other.end == this.end &&
          other.distanceM == this.distanceM &&
          other.energyKcal == this.energyKcal &&
          other.avgHr == this.avgHr &&
          other.maxHr == this.maxHr &&
          other.routeGeoJson == this.routeGeoJson &&
          other.meta == this.meta &&
          other.cursorAfter == this.cursorAfter &&
          other.created == this.created);
}

class PendingWorkoutsCompanion extends UpdateCompanion<PendingWorkoutRow> {
  final Value<int> id;
  final Value<String> sourceId;
  final Value<String> sourceUid;
  final Value<String> type;
  final Value<DateTime> start;
  final Value<DateTime> end;
  final Value<double?> distanceM;
  final Value<double?> energyKcal;
  final Value<double?> avgHr;
  final Value<double?> maxHr;
  final Value<String?> routeGeoJson;
  final Value<String> meta;
  final Value<String?> cursorAfter;
  final Value<DateTime> created;
  const PendingWorkoutsCompanion({
    this.id = const Value.absent(),
    this.sourceId = const Value.absent(),
    this.sourceUid = const Value.absent(),
    this.type = const Value.absent(),
    this.start = const Value.absent(),
    this.end = const Value.absent(),
    this.distanceM = const Value.absent(),
    this.energyKcal = const Value.absent(),
    this.avgHr = const Value.absent(),
    this.maxHr = const Value.absent(),
    this.routeGeoJson = const Value.absent(),
    this.meta = const Value.absent(),
    this.cursorAfter = const Value.absent(),
    this.created = const Value.absent(),
  });
  PendingWorkoutsCompanion.insert({
    this.id = const Value.absent(),
    required String sourceId,
    required String sourceUid,
    required String type,
    required DateTime start,
    required DateTime end,
    this.distanceM = const Value.absent(),
    this.energyKcal = const Value.absent(),
    this.avgHr = const Value.absent(),
    this.maxHr = const Value.absent(),
    this.routeGeoJson = const Value.absent(),
    this.meta = const Value.absent(),
    this.cursorAfter = const Value.absent(),
    required DateTime created,
  }) : sourceId = Value(sourceId),
       sourceUid = Value(sourceUid),
       type = Value(type),
       start = Value(start),
       end = Value(end),
       created = Value(created);
  static Insertable<PendingWorkoutRow> custom({
    Expression<int>? id,
    Expression<String>? sourceId,
    Expression<String>? sourceUid,
    Expression<String>? type,
    Expression<DateTime>? start,
    Expression<DateTime>? end,
    Expression<double>? distanceM,
    Expression<double>? energyKcal,
    Expression<double>? avgHr,
    Expression<double>? maxHr,
    Expression<String>? routeGeoJson,
    Expression<String>? meta,
    Expression<String>? cursorAfter,
    Expression<DateTime>? created,
  }) {
    return RawValuesInsertable({
      if (id != null) 'id': id,
      if (sourceId != null) 'source_id': sourceId,
      if (sourceUid != null) 'source_uid': sourceUid,
      if (type != null) 'type': type,
      if (start != null) 'start': start,
      if (end != null) 'end': end,
      if (distanceM != null) 'distance_m': distanceM,
      if (energyKcal != null) 'energy_kcal': energyKcal,
      if (avgHr != null) 'avg_hr': avgHr,
      if (maxHr != null) 'max_hr': maxHr,
      if (routeGeoJson != null) 'route_geo_json': routeGeoJson,
      if (meta != null) 'meta': meta,
      if (cursorAfter != null) 'cursor_after': cursorAfter,
      if (created != null) 'created': created,
    });
  }

  PendingWorkoutsCompanion copyWith({
    Value<int>? id,
    Value<String>? sourceId,
    Value<String>? sourceUid,
    Value<String>? type,
    Value<DateTime>? start,
    Value<DateTime>? end,
    Value<double?>? distanceM,
    Value<double?>? energyKcal,
    Value<double?>? avgHr,
    Value<double?>? maxHr,
    Value<String?>? routeGeoJson,
    Value<String>? meta,
    Value<String?>? cursorAfter,
    Value<DateTime>? created,
  }) {
    return PendingWorkoutsCompanion(
      id: id ?? this.id,
      sourceId: sourceId ?? this.sourceId,
      sourceUid: sourceUid ?? this.sourceUid,
      type: type ?? this.type,
      start: start ?? this.start,
      end: end ?? this.end,
      distanceM: distanceM ?? this.distanceM,
      energyKcal: energyKcal ?? this.energyKcal,
      avgHr: avgHr ?? this.avgHr,
      maxHr: maxHr ?? this.maxHr,
      routeGeoJson: routeGeoJson ?? this.routeGeoJson,
      meta: meta ?? this.meta,
      cursorAfter: cursorAfter ?? this.cursorAfter,
      created: created ?? this.created,
    );
  }

  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    if (id.present) {
      map['id'] = Variable<int>(id.value);
    }
    if (sourceId.present) {
      map['source_id'] = Variable<String>(sourceId.value);
    }
    if (sourceUid.present) {
      map['source_uid'] = Variable<String>(sourceUid.value);
    }
    if (type.present) {
      map['type'] = Variable<String>(type.value);
    }
    if (start.present) {
      map['start'] = Variable<DateTime>(start.value);
    }
    if (end.present) {
      map['end'] = Variable<DateTime>(end.value);
    }
    if (distanceM.present) {
      map['distance_m'] = Variable<double>(distanceM.value);
    }
    if (energyKcal.present) {
      map['energy_kcal'] = Variable<double>(energyKcal.value);
    }
    if (avgHr.present) {
      map['avg_hr'] = Variable<double>(avgHr.value);
    }
    if (maxHr.present) {
      map['max_hr'] = Variable<double>(maxHr.value);
    }
    if (routeGeoJson.present) {
      map['route_geo_json'] = Variable<String>(routeGeoJson.value);
    }
    if (meta.present) {
      map['meta'] = Variable<String>(meta.value);
    }
    if (cursorAfter.present) {
      map['cursor_after'] = Variable<String>(cursorAfter.value);
    }
    if (created.present) {
      map['created'] = Variable<DateTime>(created.value);
    }
    return map;
  }

  @override
  String toString() {
    return (StringBuffer('PendingWorkoutsCompanion(')
          ..write('id: $id, ')
          ..write('sourceId: $sourceId, ')
          ..write('sourceUid: $sourceUid, ')
          ..write('type: $type, ')
          ..write('start: $start, ')
          ..write('end: $end, ')
          ..write('distanceM: $distanceM, ')
          ..write('energyKcal: $energyKcal, ')
          ..write('avgHr: $avgHr, ')
          ..write('maxHr: $maxHr, ')
          ..write('routeGeoJson: $routeGeoJson, ')
          ..write('meta: $meta, ')
          ..write('cursorAfter: $cursorAfter, ')
          ..write('created: $created')
          ..write(')'))
        .toString();
  }
}

class $SyncStatesTable extends SyncStates
    with TableInfo<$SyncStatesTable, SyncStateRow> {
  @override
  final GeneratedDatabase attachedDatabase;
  final String? _alias;
  $SyncStatesTable(this.attachedDatabase, [this._alias]);
  static const VerificationMeta _keyMeta = const VerificationMeta('key');
  @override
  late final GeneratedColumn<String> key = GeneratedColumn<String>(
    'key',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _valueMeta = const VerificationMeta('value');
  @override
  late final GeneratedColumn<String> value = GeneratedColumn<String>(
    'value',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  @override
  List<GeneratedColumn> get $columns => [key, value];
  @override
  String get aliasedName => _alias ?? actualTableName;
  @override
  String get actualTableName => $name;
  static const String $name = 'sync_state';
  @override
  VerificationContext validateIntegrity(
    Insertable<SyncStateRow> instance, {
    bool isInserting = false,
  }) {
    final context = VerificationContext();
    final data = instance.toColumns(true);
    if (data.containsKey('key')) {
      context.handle(
        _keyMeta,
        key.isAcceptableOrUnknown(data['key']!, _keyMeta),
      );
    } else if (isInserting) {
      context.missing(_keyMeta);
    }
    if (data.containsKey('value')) {
      context.handle(
        _valueMeta,
        value.isAcceptableOrUnknown(data['value']!, _valueMeta),
      );
    }
    return context;
  }

  @override
  Set<GeneratedColumn> get $primaryKey => {key};
  @override
  SyncStateRow map(Map<String, dynamic> data, {String? tablePrefix}) {
    final effectivePrefix = tablePrefix != null ? '$tablePrefix.' : '';
    return SyncStateRow(
      key: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}key'],
      )!,
      value: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}value'],
      ),
    );
  }

  @override
  $SyncStatesTable createAlias(String alias) {
    return $SyncStatesTable(attachedDatabase, alias);
  }
}

class SyncStateRow extends DataClass implements Insertable<SyncStateRow> {
  final String key;
  final String? value;
  const SyncStateRow({required this.key, this.value});
  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    map['key'] = Variable<String>(key);
    if (!nullToAbsent || value != null) {
      map['value'] = Variable<String>(value);
    }
    return map;
  }

  SyncStatesCompanion toCompanion(bool nullToAbsent) {
    return SyncStatesCompanion(
      key: Value(key),
      value: value == null && nullToAbsent
          ? const Value.absent()
          : Value(value),
    );
  }

  factory SyncStateRow.fromJson(
    Map<String, dynamic> json, {
    ValueSerializer? serializer,
  }) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return SyncStateRow(
      key: serializer.fromJson<String>(json['key']),
      value: serializer.fromJson<String?>(json['value']),
    );
  }
  @override
  Map<String, dynamic> toJson({ValueSerializer? serializer}) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return <String, dynamic>{
      'key': serializer.toJson<String>(key),
      'value': serializer.toJson<String?>(value),
    };
  }

  SyncStateRow copyWith({
    String? key,
    Value<String?> value = const Value.absent(),
  }) => SyncStateRow(
    key: key ?? this.key,
    value: value.present ? value.value : this.value,
  );
  SyncStateRow copyWithCompanion(SyncStatesCompanion data) {
    return SyncStateRow(
      key: data.key.present ? data.key.value : this.key,
      value: data.value.present ? data.value.value : this.value,
    );
  }

  @override
  String toString() {
    return (StringBuffer('SyncStateRow(')
          ..write('key: $key, ')
          ..write('value: $value')
          ..write(')'))
        .toString();
  }

  @override
  int get hashCode => Object.hash(key, value);
  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      (other is SyncStateRow &&
          other.key == this.key &&
          other.value == this.value);
}

class SyncStatesCompanion extends UpdateCompanion<SyncStateRow> {
  final Value<String> key;
  final Value<String?> value;
  final Value<int> rowid;
  const SyncStatesCompanion({
    this.key = const Value.absent(),
    this.value = const Value.absent(),
    this.rowid = const Value.absent(),
  });
  SyncStatesCompanion.insert({
    required String key,
    this.value = const Value.absent(),
    this.rowid = const Value.absent(),
  }) : key = Value(key);
  static Insertable<SyncStateRow> custom({
    Expression<String>? key,
    Expression<String>? value,
    Expression<int>? rowid,
  }) {
    return RawValuesInsertable({
      if (key != null) 'key': key,
      if (value != null) 'value': value,
      if (rowid != null) 'rowid': rowid,
    });
  }

  SyncStatesCompanion copyWith({
    Value<String>? key,
    Value<String?>? value,
    Value<int>? rowid,
  }) {
    return SyncStatesCompanion(
      key: key ?? this.key,
      value: value ?? this.value,
      rowid: rowid ?? this.rowid,
    );
  }

  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    if (key.present) {
      map['key'] = Variable<String>(key.value);
    }
    if (value.present) {
      map['value'] = Variable<String>(value.value);
    }
    if (rowid.present) {
      map['rowid'] = Variable<int>(rowid.value);
    }
    return map;
  }

  @override
  String toString() {
    return (StringBuffer('SyncStatesCompanion(')
          ..write('key: $key, ')
          ..write('value: $value, ')
          ..write('rowid: $rowid')
          ..write(')'))
        .toString();
  }
}

abstract class _$OutboxDatabase extends GeneratedDatabase {
  _$OutboxDatabase(QueryExecutor e) : super(e);
  $OutboxDatabaseManager get managers => $OutboxDatabaseManager(this);
  late final $PendingSamplesTable pendingSamples = $PendingSamplesTable(this);
  late final $PendingWorkoutsTable pendingWorkouts = $PendingWorkoutsTable(
    this,
  );
  late final $SyncStatesTable syncStates = $SyncStatesTable(this);
  @override
  Iterable<TableInfo<Table, Object?>> get allTables =>
      allSchemaEntities.whereType<TableInfo<Table, Object?>>();
  @override
  List<DatabaseSchemaEntity> get allSchemaEntities => [
    pendingSamples,
    pendingWorkouts,
    syncStates,
  ];
  @override
  DriftDatabaseOptions get options =>
      const DriftDatabaseOptions(storeDateTimeAsText: true);
}

typedef $$PendingSamplesTableCreateCompanionBuilder =
    PendingSamplesCompanion Function({
      Value<int> id,
      required String sourceId,
      required String sourceUid,
      required String metric,
      required DateTime start,
      Value<DateTime?> end,
      required double value,
      required String unit,
      Value<String> meta,
      Value<String?> cursorAfter,
      required DateTime created,
    });
typedef $$PendingSamplesTableUpdateCompanionBuilder =
    PendingSamplesCompanion Function({
      Value<int> id,
      Value<String> sourceId,
      Value<String> sourceUid,
      Value<String> metric,
      Value<DateTime> start,
      Value<DateTime?> end,
      Value<double> value,
      Value<String> unit,
      Value<String> meta,
      Value<String?> cursorAfter,
      Value<DateTime> created,
    });

class $$PendingSamplesTableFilterComposer
    extends Composer<_$OutboxDatabase, $PendingSamplesTable> {
  $$PendingSamplesTableFilterComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnFilters<int> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get sourceId => $composableBuilder(
    column: $table.sourceId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get sourceUid => $composableBuilder(
    column: $table.sourceUid,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get metric => $composableBuilder(
    column: $table.metric,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get start => $composableBuilder(
    column: $table.start,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get end => $composableBuilder(
    column: $table.end,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<double> get value => $composableBuilder(
    column: $table.value,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get unit => $composableBuilder(
    column: $table.unit,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get meta => $composableBuilder(
    column: $table.meta,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get cursorAfter => $composableBuilder(
    column: $table.cursorAfter,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get created => $composableBuilder(
    column: $table.created,
    builder: (column) => ColumnFilters(column),
  );
}

class $$PendingSamplesTableOrderingComposer
    extends Composer<_$OutboxDatabase, $PendingSamplesTable> {
  $$PendingSamplesTableOrderingComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnOrderings<int> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get sourceId => $composableBuilder(
    column: $table.sourceId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get sourceUid => $composableBuilder(
    column: $table.sourceUid,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get metric => $composableBuilder(
    column: $table.metric,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get start => $composableBuilder(
    column: $table.start,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get end => $composableBuilder(
    column: $table.end,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<double> get value => $composableBuilder(
    column: $table.value,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get unit => $composableBuilder(
    column: $table.unit,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get meta => $composableBuilder(
    column: $table.meta,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get cursorAfter => $composableBuilder(
    column: $table.cursorAfter,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get created => $composableBuilder(
    column: $table.created,
    builder: (column) => ColumnOrderings(column),
  );
}

class $$PendingSamplesTableAnnotationComposer
    extends Composer<_$OutboxDatabase, $PendingSamplesTable> {
  $$PendingSamplesTableAnnotationComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  GeneratedColumn<int> get id =>
      $composableBuilder(column: $table.id, builder: (column) => column);

  GeneratedColumn<String> get sourceId =>
      $composableBuilder(column: $table.sourceId, builder: (column) => column);

  GeneratedColumn<String> get sourceUid =>
      $composableBuilder(column: $table.sourceUid, builder: (column) => column);

  GeneratedColumn<String> get metric =>
      $composableBuilder(column: $table.metric, builder: (column) => column);

  GeneratedColumn<DateTime> get start =>
      $composableBuilder(column: $table.start, builder: (column) => column);

  GeneratedColumn<DateTime> get end =>
      $composableBuilder(column: $table.end, builder: (column) => column);

  GeneratedColumn<double> get value =>
      $composableBuilder(column: $table.value, builder: (column) => column);

  GeneratedColumn<String> get unit =>
      $composableBuilder(column: $table.unit, builder: (column) => column);

  GeneratedColumn<String> get meta =>
      $composableBuilder(column: $table.meta, builder: (column) => column);

  GeneratedColumn<String> get cursorAfter => $composableBuilder(
    column: $table.cursorAfter,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get created =>
      $composableBuilder(column: $table.created, builder: (column) => column);
}

class $$PendingSamplesTableTableManager
    extends
        RootTableManager<
          _$OutboxDatabase,
          $PendingSamplesTable,
          PendingSampleRow,
          $$PendingSamplesTableFilterComposer,
          $$PendingSamplesTableOrderingComposer,
          $$PendingSamplesTableAnnotationComposer,
          $$PendingSamplesTableCreateCompanionBuilder,
          $$PendingSamplesTableUpdateCompanionBuilder,
          (
            PendingSampleRow,
            BaseReferences<
              _$OutboxDatabase,
              $PendingSamplesTable,
              PendingSampleRow
            >,
          ),
          PendingSampleRow,
          PrefetchHooks Function()
        > {
  $$PendingSamplesTableTableManager(
    _$OutboxDatabase db,
    $PendingSamplesTable table,
  ) : super(
        TableManagerState(
          db: db,
          table: table,
          createFilteringComposer: () =>
              $$PendingSamplesTableFilterComposer($db: db, $table: table),
          createOrderingComposer: () =>
              $$PendingSamplesTableOrderingComposer($db: db, $table: table),
          createComputedFieldComposer: () =>
              $$PendingSamplesTableAnnotationComposer($db: db, $table: table),
          updateCompanionCallback:
              ({
                Value<int> id = const Value.absent(),
                Value<String> sourceId = const Value.absent(),
                Value<String> sourceUid = const Value.absent(),
                Value<String> metric = const Value.absent(),
                Value<DateTime> start = const Value.absent(),
                Value<DateTime?> end = const Value.absent(),
                Value<double> value = const Value.absent(),
                Value<String> unit = const Value.absent(),
                Value<String> meta = const Value.absent(),
                Value<String?> cursorAfter = const Value.absent(),
                Value<DateTime> created = const Value.absent(),
              }) => PendingSamplesCompanion(
                id: id,
                sourceId: sourceId,
                sourceUid: sourceUid,
                metric: metric,
                start: start,
                end: end,
                value: value,
                unit: unit,
                meta: meta,
                cursorAfter: cursorAfter,
                created: created,
              ),
          createCompanionCallback:
              ({
                Value<int> id = const Value.absent(),
                required String sourceId,
                required String sourceUid,
                required String metric,
                required DateTime start,
                Value<DateTime?> end = const Value.absent(),
                required double value,
                required String unit,
                Value<String> meta = const Value.absent(),
                Value<String?> cursorAfter = const Value.absent(),
                required DateTime created,
              }) => PendingSamplesCompanion.insert(
                id: id,
                sourceId: sourceId,
                sourceUid: sourceUid,
                metric: metric,
                start: start,
                end: end,
                value: value,
                unit: unit,
                meta: meta,
                cursorAfter: cursorAfter,
                created: created,
              ),
          withReferenceMapper: (p0) => p0
              .map(
                (e) => (
                  e.readTable<$PendingSamplesTable, PendingSampleRow>(table),
                  BaseReferences<
                    _$OutboxDatabase,
                    $PendingSamplesTable,
                    PendingSampleRow
                  >(db, table, e),
                ),
              )
              .toList(),
          prefetchHooksCallback: null,
        ),
      );
}

typedef $$PendingSamplesTableProcessedTableManager =
    ProcessedTableManager<
      _$OutboxDatabase,
      $PendingSamplesTable,
      PendingSampleRow,
      $$PendingSamplesTableFilterComposer,
      $$PendingSamplesTableOrderingComposer,
      $$PendingSamplesTableAnnotationComposer,
      $$PendingSamplesTableCreateCompanionBuilder,
      $$PendingSamplesTableUpdateCompanionBuilder,
      (
        PendingSampleRow,
        BaseReferences<
          _$OutboxDatabase,
          $PendingSamplesTable,
          PendingSampleRow
        >,
      ),
      PendingSampleRow,
      PrefetchHooks Function()
    >;
typedef $$PendingWorkoutsTableCreateCompanionBuilder =
    PendingWorkoutsCompanion Function({
      Value<int> id,
      required String sourceId,
      required String sourceUid,
      required String type,
      required DateTime start,
      required DateTime end,
      Value<double?> distanceM,
      Value<double?> energyKcal,
      Value<double?> avgHr,
      Value<double?> maxHr,
      Value<String?> routeGeoJson,
      Value<String> meta,
      Value<String?> cursorAfter,
      required DateTime created,
    });
typedef $$PendingWorkoutsTableUpdateCompanionBuilder =
    PendingWorkoutsCompanion Function({
      Value<int> id,
      Value<String> sourceId,
      Value<String> sourceUid,
      Value<String> type,
      Value<DateTime> start,
      Value<DateTime> end,
      Value<double?> distanceM,
      Value<double?> energyKcal,
      Value<double?> avgHr,
      Value<double?> maxHr,
      Value<String?> routeGeoJson,
      Value<String> meta,
      Value<String?> cursorAfter,
      Value<DateTime> created,
    });

class $$PendingWorkoutsTableFilterComposer
    extends Composer<_$OutboxDatabase, $PendingWorkoutsTable> {
  $$PendingWorkoutsTableFilterComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnFilters<int> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get sourceId => $composableBuilder(
    column: $table.sourceId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get sourceUid => $composableBuilder(
    column: $table.sourceUid,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get type => $composableBuilder(
    column: $table.type,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get start => $composableBuilder(
    column: $table.start,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get end => $composableBuilder(
    column: $table.end,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<double> get distanceM => $composableBuilder(
    column: $table.distanceM,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<double> get energyKcal => $composableBuilder(
    column: $table.energyKcal,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<double> get avgHr => $composableBuilder(
    column: $table.avgHr,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<double> get maxHr => $composableBuilder(
    column: $table.maxHr,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get routeGeoJson => $composableBuilder(
    column: $table.routeGeoJson,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get meta => $composableBuilder(
    column: $table.meta,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get cursorAfter => $composableBuilder(
    column: $table.cursorAfter,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get created => $composableBuilder(
    column: $table.created,
    builder: (column) => ColumnFilters(column),
  );
}

class $$PendingWorkoutsTableOrderingComposer
    extends Composer<_$OutboxDatabase, $PendingWorkoutsTable> {
  $$PendingWorkoutsTableOrderingComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnOrderings<int> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get sourceId => $composableBuilder(
    column: $table.sourceId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get sourceUid => $composableBuilder(
    column: $table.sourceUid,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get type => $composableBuilder(
    column: $table.type,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get start => $composableBuilder(
    column: $table.start,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get end => $composableBuilder(
    column: $table.end,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<double> get distanceM => $composableBuilder(
    column: $table.distanceM,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<double> get energyKcal => $composableBuilder(
    column: $table.energyKcal,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<double> get avgHr => $composableBuilder(
    column: $table.avgHr,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<double> get maxHr => $composableBuilder(
    column: $table.maxHr,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get routeGeoJson => $composableBuilder(
    column: $table.routeGeoJson,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get meta => $composableBuilder(
    column: $table.meta,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get cursorAfter => $composableBuilder(
    column: $table.cursorAfter,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get created => $composableBuilder(
    column: $table.created,
    builder: (column) => ColumnOrderings(column),
  );
}

class $$PendingWorkoutsTableAnnotationComposer
    extends Composer<_$OutboxDatabase, $PendingWorkoutsTable> {
  $$PendingWorkoutsTableAnnotationComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  GeneratedColumn<int> get id =>
      $composableBuilder(column: $table.id, builder: (column) => column);

  GeneratedColumn<String> get sourceId =>
      $composableBuilder(column: $table.sourceId, builder: (column) => column);

  GeneratedColumn<String> get sourceUid =>
      $composableBuilder(column: $table.sourceUid, builder: (column) => column);

  GeneratedColumn<String> get type =>
      $composableBuilder(column: $table.type, builder: (column) => column);

  GeneratedColumn<DateTime> get start =>
      $composableBuilder(column: $table.start, builder: (column) => column);

  GeneratedColumn<DateTime> get end =>
      $composableBuilder(column: $table.end, builder: (column) => column);

  GeneratedColumn<double> get distanceM =>
      $composableBuilder(column: $table.distanceM, builder: (column) => column);

  GeneratedColumn<double> get energyKcal => $composableBuilder(
    column: $table.energyKcal,
    builder: (column) => column,
  );

  GeneratedColumn<double> get avgHr =>
      $composableBuilder(column: $table.avgHr, builder: (column) => column);

  GeneratedColumn<double> get maxHr =>
      $composableBuilder(column: $table.maxHr, builder: (column) => column);

  GeneratedColumn<String> get routeGeoJson => $composableBuilder(
    column: $table.routeGeoJson,
    builder: (column) => column,
  );

  GeneratedColumn<String> get meta =>
      $composableBuilder(column: $table.meta, builder: (column) => column);

  GeneratedColumn<String> get cursorAfter => $composableBuilder(
    column: $table.cursorAfter,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get created =>
      $composableBuilder(column: $table.created, builder: (column) => column);
}

class $$PendingWorkoutsTableTableManager
    extends
        RootTableManager<
          _$OutboxDatabase,
          $PendingWorkoutsTable,
          PendingWorkoutRow,
          $$PendingWorkoutsTableFilterComposer,
          $$PendingWorkoutsTableOrderingComposer,
          $$PendingWorkoutsTableAnnotationComposer,
          $$PendingWorkoutsTableCreateCompanionBuilder,
          $$PendingWorkoutsTableUpdateCompanionBuilder,
          (
            PendingWorkoutRow,
            BaseReferences<
              _$OutboxDatabase,
              $PendingWorkoutsTable,
              PendingWorkoutRow
            >,
          ),
          PendingWorkoutRow,
          PrefetchHooks Function()
        > {
  $$PendingWorkoutsTableTableManager(
    _$OutboxDatabase db,
    $PendingWorkoutsTable table,
  ) : super(
        TableManagerState(
          db: db,
          table: table,
          createFilteringComposer: () =>
              $$PendingWorkoutsTableFilterComposer($db: db, $table: table),
          createOrderingComposer: () =>
              $$PendingWorkoutsTableOrderingComposer($db: db, $table: table),
          createComputedFieldComposer: () =>
              $$PendingWorkoutsTableAnnotationComposer($db: db, $table: table),
          updateCompanionCallback:
              ({
                Value<int> id = const Value.absent(),
                Value<String> sourceId = const Value.absent(),
                Value<String> sourceUid = const Value.absent(),
                Value<String> type = const Value.absent(),
                Value<DateTime> start = const Value.absent(),
                Value<DateTime> end = const Value.absent(),
                Value<double?> distanceM = const Value.absent(),
                Value<double?> energyKcal = const Value.absent(),
                Value<double?> avgHr = const Value.absent(),
                Value<double?> maxHr = const Value.absent(),
                Value<String?> routeGeoJson = const Value.absent(),
                Value<String> meta = const Value.absent(),
                Value<String?> cursorAfter = const Value.absent(),
                Value<DateTime> created = const Value.absent(),
              }) => PendingWorkoutsCompanion(
                id: id,
                sourceId: sourceId,
                sourceUid: sourceUid,
                type: type,
                start: start,
                end: end,
                distanceM: distanceM,
                energyKcal: energyKcal,
                avgHr: avgHr,
                maxHr: maxHr,
                routeGeoJson: routeGeoJson,
                meta: meta,
                cursorAfter: cursorAfter,
                created: created,
              ),
          createCompanionCallback:
              ({
                Value<int> id = const Value.absent(),
                required String sourceId,
                required String sourceUid,
                required String type,
                required DateTime start,
                required DateTime end,
                Value<double?> distanceM = const Value.absent(),
                Value<double?> energyKcal = const Value.absent(),
                Value<double?> avgHr = const Value.absent(),
                Value<double?> maxHr = const Value.absent(),
                Value<String?> routeGeoJson = const Value.absent(),
                Value<String> meta = const Value.absent(),
                Value<String?> cursorAfter = const Value.absent(),
                required DateTime created,
              }) => PendingWorkoutsCompanion.insert(
                id: id,
                sourceId: sourceId,
                sourceUid: sourceUid,
                type: type,
                start: start,
                end: end,
                distanceM: distanceM,
                energyKcal: energyKcal,
                avgHr: avgHr,
                maxHr: maxHr,
                routeGeoJson: routeGeoJson,
                meta: meta,
                cursorAfter: cursorAfter,
                created: created,
              ),
          withReferenceMapper: (p0) => p0
              .map(
                (e) => (
                  e.readTable<$PendingWorkoutsTable, PendingWorkoutRow>(table),
                  BaseReferences<
                    _$OutboxDatabase,
                    $PendingWorkoutsTable,
                    PendingWorkoutRow
                  >(db, table, e),
                ),
              )
              .toList(),
          prefetchHooksCallback: null,
        ),
      );
}

typedef $$PendingWorkoutsTableProcessedTableManager =
    ProcessedTableManager<
      _$OutboxDatabase,
      $PendingWorkoutsTable,
      PendingWorkoutRow,
      $$PendingWorkoutsTableFilterComposer,
      $$PendingWorkoutsTableOrderingComposer,
      $$PendingWorkoutsTableAnnotationComposer,
      $$PendingWorkoutsTableCreateCompanionBuilder,
      $$PendingWorkoutsTableUpdateCompanionBuilder,
      (
        PendingWorkoutRow,
        BaseReferences<
          _$OutboxDatabase,
          $PendingWorkoutsTable,
          PendingWorkoutRow
        >,
      ),
      PendingWorkoutRow,
      PrefetchHooks Function()
    >;
typedef $$SyncStatesTableCreateCompanionBuilder =
    SyncStatesCompanion Function({
      required String key,
      Value<String?> value,
      Value<int> rowid,
    });
typedef $$SyncStatesTableUpdateCompanionBuilder =
    SyncStatesCompanion Function({
      Value<String> key,
      Value<String?> value,
      Value<int> rowid,
    });

class $$SyncStatesTableFilterComposer
    extends Composer<_$OutboxDatabase, $SyncStatesTable> {
  $$SyncStatesTableFilterComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnFilters<String> get key => $composableBuilder(
    column: $table.key,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get value => $composableBuilder(
    column: $table.value,
    builder: (column) => ColumnFilters(column),
  );
}

class $$SyncStatesTableOrderingComposer
    extends Composer<_$OutboxDatabase, $SyncStatesTable> {
  $$SyncStatesTableOrderingComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnOrderings<String> get key => $composableBuilder(
    column: $table.key,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get value => $composableBuilder(
    column: $table.value,
    builder: (column) => ColumnOrderings(column),
  );
}

class $$SyncStatesTableAnnotationComposer
    extends Composer<_$OutboxDatabase, $SyncStatesTable> {
  $$SyncStatesTableAnnotationComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  GeneratedColumn<String> get key =>
      $composableBuilder(column: $table.key, builder: (column) => column);

  GeneratedColumn<String> get value =>
      $composableBuilder(column: $table.value, builder: (column) => column);
}

class $$SyncStatesTableTableManager
    extends
        RootTableManager<
          _$OutboxDatabase,
          $SyncStatesTable,
          SyncStateRow,
          $$SyncStatesTableFilterComposer,
          $$SyncStatesTableOrderingComposer,
          $$SyncStatesTableAnnotationComposer,
          $$SyncStatesTableCreateCompanionBuilder,
          $$SyncStatesTableUpdateCompanionBuilder,
          (
            SyncStateRow,
            BaseReferences<_$OutboxDatabase, $SyncStatesTable, SyncStateRow>,
          ),
          SyncStateRow,
          PrefetchHooks Function()
        > {
  $$SyncStatesTableTableManager(_$OutboxDatabase db, $SyncStatesTable table)
    : super(
        TableManagerState(
          db: db,
          table: table,
          createFilteringComposer: () =>
              $$SyncStatesTableFilterComposer($db: db, $table: table),
          createOrderingComposer: () =>
              $$SyncStatesTableOrderingComposer($db: db, $table: table),
          createComputedFieldComposer: () =>
              $$SyncStatesTableAnnotationComposer($db: db, $table: table),
          updateCompanionCallback:
              ({
                Value<String> key = const Value.absent(),
                Value<String?> value = const Value.absent(),
                Value<int> rowid = const Value.absent(),
              }) => SyncStatesCompanion(key: key, value: value, rowid: rowid),
          createCompanionCallback:
              ({
                required String key,
                Value<String?> value = const Value.absent(),
                Value<int> rowid = const Value.absent(),
              }) => SyncStatesCompanion.insert(
                key: key,
                value: value,
                rowid: rowid,
              ),
          withReferenceMapper: (p0) => p0
              .map(
                (e) => (
                  e.readTable<$SyncStatesTable, SyncStateRow>(table),
                  BaseReferences<
                    _$OutboxDatabase,
                    $SyncStatesTable,
                    SyncStateRow
                  >(db, table, e),
                ),
              )
              .toList(),
          prefetchHooksCallback: null,
        ),
      );
}

typedef $$SyncStatesTableProcessedTableManager =
    ProcessedTableManager<
      _$OutboxDatabase,
      $SyncStatesTable,
      SyncStateRow,
      $$SyncStatesTableFilterComposer,
      $$SyncStatesTableOrderingComposer,
      $$SyncStatesTableAnnotationComposer,
      $$SyncStatesTableCreateCompanionBuilder,
      $$SyncStatesTableUpdateCompanionBuilder,
      (
        SyncStateRow,
        BaseReferences<_$OutboxDatabase, $SyncStatesTable, SyncStateRow>,
      ),
      SyncStateRow,
      PrefetchHooks Function()
    >;

class $OutboxDatabaseManager {
  final _$OutboxDatabase _db;
  $OutboxDatabaseManager(this._db);
  $$PendingSamplesTableTableManager get pendingSamples =>
      $$PendingSamplesTableTableManager(_db, _db.pendingSamples);
  $$PendingWorkoutsTableTableManager get pendingWorkouts =>
      $$PendingWorkoutsTableTableManager(_db, _db.pendingWorkouts);
  $$SyncStatesTableTableManager get syncStates =>
      $$SyncStatesTableTableManager(_db, _db.syncStates);
}
