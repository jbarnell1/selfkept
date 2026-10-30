import 'package:flutter/foundation.dart'
    show TargetPlatform, defaultTargetPlatform, kIsWeb, visibleForTesting;

/// Build-time configuration. Everything here is resolved from --dart-define or
/// from the platform; nothing is read from the network.
abstract final class AppConfig {
  /// Server origin baked in with --dart-define=API_BASE_URL=http://host:5000.
  /// Empty means "derive one from the platform" (see [defaultServerUrl]).
  static const String _definedServerUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: '',
  );

  /// Build name of this client (--dart-define=APP_VERSION=X.Y.Z). The server's
  /// minClientVersion is compared against it. 'dev' for local builds, which the
  /// handshake treats as always compatible.
  static const String appVersion = String.fromEnvironment(
    'APP_VERSION',
    defaultValue: 'dev',
  );

  /// Oldest server this client knows how to talk to.
  static const String minServerVersion = String.fromEnvironment(
    'MIN_SERVER_VERSION',
    defaultValue: '0.1.0',
  );

  /// Every API path is served under this prefix on the server origin.
  static const String apiPrefix = '/api';

  static const Duration requestTimeout = Duration(seconds: 20);
  static const Duration handshakeTimeout = Duration(seconds: 8);

  /// Lets tests exercise [defaultServerUrl] for a platform other than the host.
  @visibleForTesting
  static TargetPlatform? platformOverride;

  /// Lets tests exercise [defaultServerUrl] as if a define were present.
  @visibleForTesting
  static String? definedServerUrlOverride;

  /// Lets tests pretend to run on web or not.
  @visibleForTesting
  static bool? isWebOverride;

  static bool get _isWeb => isWebOverride ?? kIsWeb;
  static TargetPlatform get _platform =>
      platformOverride ?? defaultTargetPlatform;
  static String get _defined => definedServerUrlOverride ?? _definedServerUrl;

  /// Value of API_BASE_URL meaning "the origin that served this web page".
  /// The self-host image serves the web build from the API's wwwroot.
  static const String sameOrigin = 'same-origin';

  /// True when the build carries an explicit API_BASE_URL.
  static bool get hasDefinedServerUrl => _defined.trim().isNotEmpty;

  /// True for the self-host web build (API_BASE_URL=same-origin).
  static bool get isSameOriginBuild => _defined.trim() == sameOrigin;

  /// The server origin used when no in-app override is stored.
  ///
  /// Order: the --dart-define (a URL, or same-origin), then on web the origin
  /// that served the page, then the Android emulator loopback alias, then
  /// plain localhost.
  static String get defaultServerUrl {
    if (_isWeb) {
      if (hasDefinedServerUrl && !isSameOriginBuild) {
        return normalizeServerUrl(_defined);
      }
      return Uri.base.origin;
    }
    // Mobile: same-origin has no meaning, so it counts as unset and the
    // in-app server address (or the emulator default) applies.
    if (hasDefinedServerUrl && !isSameOriginBuild) {
      return normalizeServerUrl(_defined);
    }
    if (_platform == TargetPlatform.android) return 'http://10.0.2.2:5000';
    return 'http://localhost:5000';
  }

  /// Trims whitespace, adds a scheme when the user typed a bare host, and drops
  /// a trailing slash or an accidental /api suffix so paths can be appended.
  static String normalizeServerUrl(String raw) {
    var value = raw.trim();
    if (value.isEmpty) return value;
    if (!value.contains('://')) value = 'http://$value';
    while (value.endsWith('/')) {
      value = value.substring(0, value.length - 1);
    }
    if (value.endsWith(apiPrefix)) {
      value = value.substring(0, value.length - apiPrefix.length);
    }
    return value;
  }

  /// Full API base for a server origin.
  static String apiBaseFor(String serverUrl) =>
      '${normalizeServerUrl(serverUrl)}$apiPrefix';

  /// Value of the X-Client header. Web omits it and relies on the refresh cookie.
  static String get clientKind => _isWeb ? 'web' : 'mobile';

  @visibleForTesting
  static void resetOverrides() {
    platformOverride = null;
    definedServerUrlOverride = null;
    isWebOverride = null;
  }
}
