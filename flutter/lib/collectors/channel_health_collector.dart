import 'dart:async';

import 'package:flutter/services.dart';

import '../models/metric.dart';
import '../models/source.dart';
import '../utils/json_reader.dart';
import 'health_collector.dart';

/// Talks to the native collector (HealthKitCollector.swift or
/// HealthConnectCollector.kt) over one method channel and one event channel.
///
/// Methods:
/// - `isAvailable()` -> bool
/// - `deviceInfo()` -> `{ name, platform, osVersion }`
/// - `requestPermissions({ metrics: [key] })` -> permission state string
/// - `collectSince({ cursor, backfillFrom, limit })` ->
///   `{ samples: [...], workouts: [...], cursor, hasMore }`
///
/// The event channel emits any value when the store has new data.
class ChannelHealthCollector implements HealthCollector {
  static const String methodChannelName = 'app.selfkept/health';
  static const String eventChannelName = 'app.selfkept/health/changes';

  /// Rows per page asked of the platform. Below the outbox batch size so one
  /// page flushes in one request.
  static const int pageLimit = 1500;

  final MethodChannel _methods;
  final EventChannel _events;
  @override
  final SourceKind kind;

  Stream<void>? _changes;

  ChannelHealthCollector({
    required this.kind,
    MethodChannel? methods,
    EventChannel? events,
  }) : _methods = methods ?? const MethodChannel(methodChannelName),
       _events = events ?? const EventChannel(eventChannelName);

  @override
  Future<bool> isAvailable() async {
    try {
      return await _methods.invokeMethod<bool>('isAvailable') ?? false;
    } on MissingPluginException {
      return false;
    } on PlatformException {
      return false;
    }
  }

  @override
  Future<String> deviceName() async {
    try {
      final info = await _methods.invokeMapMethod<String, dynamic>(
        'deviceInfo',
      );
      final name = info == null ? null : JsonReader(info).stringOrNull('name');
      return name ?? 'This phone';
    } on PlatformException {
      return 'This phone';
    } on MissingPluginException {
      return 'This phone';
    }
  }

  @override
  Future<PermissionState> requestPermissions(Set<MetricKey> metrics) async {
    try {
      final result = await _methods.invokeMethod<String>('requestPermissions', {
        'metrics': [for (final m in metrics) m.wireValue],
      });
      return PermissionState.fromString(result);
    } on MissingPluginException {
      return PermissionState.unavailable;
    }
  }

  @override
  Future<CollectResult> collectSince(
    String? cursor, {
    DateTime? backfillFrom,
  }) async {
    final raw = await _methods
        .invokeMapMethod<String, dynamic>('collectSince', {
          'cursor': cursor,
          'backfillFrom': backfillFrom?.toUtc().toIso8601String(),
          'limit': pageLimit,
        });
    if (raw == null) return CollectResult.empty;
    return CollectResult.fromJson(raw);
  }

  @override
  Stream<void> get changes => _changes ??= _events
      .receiveBroadcastStream()
      .map<void>((_) {})
      .handleError((Object _) {});
}
