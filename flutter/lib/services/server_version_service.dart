import '../config/app_config.dart';
import '../models/system.dart';
import '../utils/api_error.dart';
import 'system_service.dart';

/// What the version handshake decided.
enum ServerCompatibility {
  /// The server and this client accept each other.
  ok,

  /// The server is older than this client needs. The owner updates the server.
  serverNeedsUpdate,

  /// The server no longer supports this client. The person updates the app.
  appNeedsUpdate,

  /// No answer at all. Wrong address, server down, or not on the tailnet.
  unreachable;

  static ServerCompatibility fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : unreachable;

  static ServerCompatibility fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final c in values) {
      if (c.name.toLowerCase() == lower) return c;
    }
    return unreachable;
  }

  String get displayName => switch (this) {
    ok => 'Compatible',
    serverNeedsUpdate => 'Server needs an update',
    appNeedsUpdate => 'App needs an update',
    unreachable => 'Not reachable',
  };
}

/// Outcome of a check, with the numbers a screen quotes.
class VersionCheckResult {
  final ServerCompatibility compatibility;
  final VersionInfo? info;
  final String appVersion;
  final String requiredServerVersion;
  final String? failure;

  const VersionCheckResult({
    required this.compatibility,
    this.info,
    this.appVersion = AppConfig.appVersion,
    this.requiredServerVersion = AppConfig.minServerVersion,
    this.failure,
  });

  bool get isOk => compatibility == ServerCompatibility.ok;
  String get serverVersion => info?.version ?? '';
  String get requiredAppVersion => info?.minClientVersion ?? '';
}

/// Asks the server which version it is and which clients it supports, before
/// anyone signs in. A self-hosted server can lag behind the app (the owner has
/// not pulled the new image) and an old app can lag behind the server. Both get
/// a clear screen instead of a page of broken requests.
///
/// A 'dev' build or a version string that is not X.Y.Z never blocks.
class ServerVersionService {
  final SystemService _system;
  final String appVersion;
  final String minServerVersion;

  ServerVersionService(
    this._system, {
    this.appVersion = AppConfig.appVersion,
    this.minServerVersion = AppConfig.minServerVersion,
  });

  Future<VersionCheckResult> check() async {
    final VersionInfo info;
    try {
      info = await _system.version();
    } on ApiException catch (e) {
      if (e.isNetwork) {
        return VersionCheckResult(
          compatibility: ServerCompatibility.unreachable,
          appVersion: appVersion,
          requiredServerVersion: minServerVersion,
          failure: e.message,
        );
      }
      // The server answered with an error: it exists, so let the person in
      // and let the next screen report whatever is wrong.
      return VersionCheckResult(
        compatibility: ServerCompatibility.ok,
        appVersion: appVersion,
        requiredServerVersion: minServerVersion,
        failure: e.message,
      );
    } catch (e) {
      return VersionCheckResult(
        compatibility: ServerCompatibility.unreachable,
        appVersion: appVersion,
        requiredServerVersion: minServerVersion,
        failure: describeError(e),
      );
    }
    return evaluate(info);
  }

  /// Pure decision, separate from the network call so it can be tested.
  VersionCheckResult evaluate(VersionInfo info) {
    final server = parseVersion(info.version);
    final app = parseVersion(appVersion);

    if (server != null &&
        compareVersions(server, parseVersion(minServerVersion)) < 0) {
      return VersionCheckResult(
        compatibility: ServerCompatibility.serverNeedsUpdate,
        info: info,
        appVersion: appVersion,
        requiredServerVersion: minServerVersion,
      );
    }

    final minClient = parseVersion(info.minClientVersion);
    if (app != null &&
        minClient != null &&
        compareVersions(app, minClient) < 0) {
      return VersionCheckResult(
        compatibility: ServerCompatibility.appNeedsUpdate,
        info: info,
        appVersion: appVersion,
        requiredServerVersion: minServerVersion,
      );
    }

    return VersionCheckResult(
      compatibility: ServerCompatibility.ok,
      info: info,
      appVersion: appVersion,
      requiredServerVersion: minServerVersion,
    );
  }

  /// "1.5.29", "v1.5.29" or "1.5.29+3" as [1, 5, 29]; null for anything else.
  static List<int>? parseVersion(String? value) {
    if (value == null) return null;
    final match = RegExp(
      r'^v?(\d{1,9})\.(\d{1,9})\.(\d{1,9})(?:[-+][0-9A-Za-z.+-]*)?$',
    ).firstMatch(value.trim());
    if (match == null) return null;
    return [for (var i = 1; i <= 3; i++) int.parse(match.group(i)!)];
  }

  /// Negative when [a] is older than [b]. A missing [b] counts as no minimum.
  static int compareVersions(List<int> a, List<int>? b) {
    if (b == null) return 1;
    for (var i = 0; i < 3; i++) {
      if (a[i] != b[i]) return a[i].compareTo(b[i]);
    }
    return 0;
  }
}
