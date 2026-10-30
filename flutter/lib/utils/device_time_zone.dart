import 'package:flutter_timezone/flutter_timezone.dart';

/// The device's IANA time zone (for example America/Chicago), or null when
/// the platform cannot say. Injected where it is used so tests need no plugin.
typedef DeviceTimeZone = Future<String?> Function();

/// Every IANA zone the platform knows, sorted. Injected like [DeviceTimeZone].
typedef TimeZoneCatalogue = Future<List<String>> Function();

Future<String?> deviceTimeZone() async {
  try {
    final zone = (await FlutterTimezone.getLocalTimezone()).identifier.trim();
    return zone.isEmpty ? null : zone;
  } catch (_) {
    return null;
  }
}

Future<List<String>> availableTimeZones() async {
  try {
    final zones = {
      for (final z in await FlutterTimezone.getAvailableTimezones())
        if (z.identifier.contains('/')) z.identifier,
    }.toList()..sort();
    if (zones.isNotEmpty) return zones;
  } catch (_) {
    // Fall through to the short list.
  }
  return commonTimeZones;
}

/// What the picker offers when the platform has no list of its own.
const List<String> commonTimeZones = [
  'Africa/Johannesburg',
  'America/Anchorage',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Mexico_City',
  'America/New_York',
  'America/Phoenix',
  'America/Sao_Paulo',
  'America/Toronto',
  'Asia/Dubai',
  'Asia/Kolkata',
  'Asia/Shanghai',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Australia/Sydney',
  'Europe/Berlin',
  'Europe/London',
  'Europe/Madrid',
  'Europe/Paris',
  'Pacific/Auckland',
  'Pacific/Honolulu',
  'UTC',
];
