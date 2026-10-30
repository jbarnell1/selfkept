import 'package:flutter/foundation.dart';

import '../models/system.dart';
import '../services/server_version_service.dart';
import '../services/system_service.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// Where the app stands between launch and the first real screen.
enum StartupPhase {
  pending,
  ready,
  unreachable,
  serverNeedsUpdate,
  appNeedsUpdate;

  static StartupPhase fromInt(int value) =>
      value >= 0 && value < values.length ? values[value] : pending;

  static StartupPhase fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final p in values) {
      if (p.name.toLowerCase() == lower) return p;
    }
    return pending;
  }

  String get displayName => switch (this) {
    pending => 'Checking the server',
    ready => 'Ready',
    unreachable => 'Not reachable',
    serverNeedsUpdate => 'Server needs an update',
    appNeedsUpdate => 'App needs an update',
  };
}

/// Runs the version handshake, reads setup status and restores the session,
/// in that order. The splash page watches it; the router redirects on it.
class StartupProvider extends ChangeNotifier with SafeNotifier {
  final ServerVersionService _versions;
  final SystemService _system;
  final AuthProvider _auth;

  StartupPhase _phase = StartupPhase.pending;
  VersionCheckResult? _versionResult;
  SetupStatus? _setupStatus;
  bool _running = false;

  StartupProvider({
    required this._versions,
    required this._system,
    required this._auth,
  }) {
    _auth.addListener(_onAuthChanged);
  }

  StartupPhase get phase => _phase;
  bool get isReady => _phase == StartupPhase.ready;
  bool get isRunning => _running;
  VersionCheckResult? get versionResult => _versionResult;
  VersionInfo? get versionInfo => _versionResult?.info;
  SetupStatus? get setupStatus => _setupStatus;

  /// True until the first account exists on this server.
  bool get needsSetup => _setupStatus?.needsFirstUser ?? false;

  /// Checks the server and restores the session. Safe to call again after a
  /// failure or after the server address changes.
  Future<void> run() async {
    if (_running) return;
    _running = true;
    _phase = StartupPhase.pending;
    notify();
    try {
      final result = await _versions.check();
      _versionResult = result;
      switch (result.compatibility) {
        case ServerCompatibility.unreachable:
          _phase = StartupPhase.unreachable;
          return;
        case ServerCompatibility.serverNeedsUpdate:
          _phase = StartupPhase.serverNeedsUpdate;
          return;
        case ServerCompatibility.appNeedsUpdate:
          _phase = StartupPhase.appNeedsUpdate;
          return;
        case ServerCompatibility.ok:
          break;
      }
      try {
        _setupStatus = await _system.setupStatus();
      } catch (_) {
        // An older server without the endpoint: assume it is set up.
        _setupStatus = const SetupStatus(
          needsFirstUser: false,
          registrationOpen: false,
        );
      }
      if (!_auth.isBootstrapped) await _auth.bootstrap();
      _phase = StartupPhase.ready;
    } finally {
      _running = false;
      notify();
    }
  }

  void _onAuthChanged() {
    // Once anyone is signed in, the first account exists.
    if (_auth.isAuthenticated && needsSetup) {
      _setupStatus = SetupStatus(
        needsFirstUser: false,
        registrationOpen: _setupStatus?.registrationOpen ?? false,
      );
      notify();
    }
  }

  @override
  void dispose() {
    _auth.removeListener(_onAuthChanged);
    super.dispose();
  }
}
