import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/foundation.dart';

import '../utils/safe_notifier.dart';

/// Whether the server can be reached right now.
///
/// Two inputs: the device's network interface (connectivity_plus) and the real
/// outcome of API calls reported by the client. A phone with Wi-Fi but no
/// tailnet looks online to the OS and still cannot reach the server, so the
/// request outcome wins whenever it disagrees.
class Reachability extends ChangeNotifier with SafeNotifier {
  final Connectivity? _connectivity;
  StreamSubscription<List<ConnectivityResult>>? _subscription;

  bool _interfaceUp = true;
  bool _serverAnswering = true;

  Reachability({Connectivity? connectivity, bool listenToInterface = true})
    : _connectivity = listenToInterface
          ? (connectivity ?? Connectivity())
          : null {
    final c = _connectivity;
    if (c != null) {
      _subscription = c.onConnectivityChanged.listen(
        _onInterface,
        onError: (_) {},
      );
      unawaited(c.checkConnectivity().then(_onInterface).catchError((_) {}));
    }
  }

  /// True when the last API call succeeded and the interface is up.
  bool get isReachable => _interfaceUp && _serverAnswering;

  bool get interfaceUp => _interfaceUp;
  bool get serverAnswering => _serverAnswering;

  void _onInterface(List<ConnectivityResult> results) {
    final up = results.any((r) => r != ConnectivityResult.none);
    if (up == _interfaceUp) return;
    _interfaceUp = up;
    // A freshly restored interface deserves a fresh guess about the server.
    if (up) _serverAnswering = true;
    notify();
  }

  /// Called by the API client after any HTTP answer, including 4xx and 5xx:
  /// an answer means the server is there.
  void reportSuccess() {
    if (_serverAnswering) return;
    _serverAnswering = true;
    notify();
  }

  /// Called by the API client on a transport failure.
  void reportFailure() {
    if (!_serverAnswering) return;
    _serverAnswering = false;
    notify();
  }

  @visibleForTesting
  void debugSetInterface(bool up) => _onInterface(
    up ? const [ConnectivityResult.wifi] : const [ConnectivityResult.none],
  );

  @override
  void dispose() {
    unawaited(_subscription?.cancel());
    super.dispose();
  }
}
