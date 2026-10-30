/// Reads values out of a decoded JSON map while accepting both camelCase and
/// PascalCase keys. The contract is camelCase, but every model tolerates the
/// other casing so a serializer change on the server never breaks a client.
extension type JsonReader(Map<String, dynamic> json) {
  static String _pascal(String key) =>
      key.isEmpty ? key : key[0].toUpperCase() + key.substring(1);

  /// Raw value under [key] in either casing, or null.
  dynamic raw(String key) {
    if (json.containsKey(key)) return json[key];
    final pascal = _pascal(key);
    if (json.containsKey(pascal)) return json[pascal];
    return null;
  }

  bool has(String key) =>
      json.containsKey(key) || json.containsKey(_pascal(key));

  String string(String key, {String fallback = ''}) {
    final value = raw(key);
    if (value == null) return fallback;
    return value.toString();
  }

  String? stringOrNull(String key) {
    final value = raw(key);
    if (value == null) return null;
    final text = value.toString();
    return text.isEmpty ? null : text;
  }

  bool boolean(String key, {bool fallback = false}) {
    final value = raw(key);
    if (value is bool) return value;
    if (value is String) {
      final lower = value.toLowerCase();
      if (lower == 'true') return true;
      if (lower == 'false') return false;
    }
    if (value is num) return value != 0;
    return fallback;
  }

  int integer(String key, {int fallback = 0}) {
    final value = raw(key);
    if (value is int) return value;
    if (value is num) return value.toInt();
    if (value is String) return int.tryParse(value) ?? fallback;
    return fallback;
  }

  DateTime? dateTime(String key) {
    final value = raw(key);
    if (value is DateTime) return value;
    if (value is String && value.isNotEmpty) return DateTime.tryParse(value);
    return null;
  }

  /// A nested object, or null when absent or not a map.
  Map<String, dynamic>? object(String key) {
    final value = raw(key);
    if (value is Map<String, dynamic>) return value;
    if (value is Map) return Map<String, dynamic>.from(value);
    return null;
  }

  /// A list of nested objects. Non-map entries are skipped.
  List<Map<String, dynamic>> objects(String key) {
    final value = raw(key);
    if (value is! List) return const [];
    return [
      for (final item in value)
        if (item is Map<String, dynamic>)
          item
        else if (item is Map)
          Map<String, dynamic>.from(item),
    ];
  }

  /// A list of strings. Non-null scalars are stringified.
  List<String> strings(String key) {
    final value = raw(key);
    if (value is! List) return const [];
    return [
      for (final item in value)
        if (item != null) item.toString(),
    ];
  }
}

/// Coerces a decoded response body to a JSON object map, tolerating the
/// `Map<dynamic, dynamic>` shape Dio can hand back.
Map<String, dynamic> asJsonMap(dynamic body) {
  if (body is Map<String, dynamic>) return body;
  if (body is Map) return Map<String, dynamic>.from(body);
  throw const FormatException('Expected a JSON object');
}

/// Coerces a decoded response body to a list of JSON object maps.
List<Map<String, dynamic>> asJsonList(dynamic body) {
  if (body is! List) throw const FormatException('Expected a JSON array');
  return [for (final item in body) asJsonMap(item)];
}

/// Reads a JSON number that may arrive as int, double or string.
double? readDouble(dynamic value) {
  if (value == null) return null;
  if (value is num) return value.toDouble();
  if (value is String) return double.tryParse(value);
  return null;
}
