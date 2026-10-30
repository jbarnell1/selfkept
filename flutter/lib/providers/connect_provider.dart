import 'package:flutter/foundation.dart';

import '../models/connect.dart';
import '../services/connect_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// Connected assistants, access tokens and what assistants remember.
class ConnectProvider extends ChangeNotifier with SafeNotifier {
  final ConnectService _service;
  final AuthProvider? _auth;

  Connections? _connections;
  List<AiMemoryItem> _memory = const [];
  bool _loading = false;
  String? _error;

  /// [auth], when given, clears everything on sign-out so one person's
  /// assistants and notes are never shown to the next.
  ConnectProvider(this._service, {this._auth}) {
    _auth?.addListener(_onAuthChanged);
  }

  /// Null until the first load succeeds.
  Connections? get connections => _connections;
  List<AiMemoryItem> get memory => _memory;
  bool get loading => _loading;
  bool get hasLoaded => _connections != null;
  String? get error => _error;

  /// Connections and memory, fetched together.
  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      final results = await Future.wait<Object>([
        _service.connections(),
        _service.memory(),
      ]);
      _connections = results[0] as Connections;
      _memory = results[1] as List<AiMemoryItem>;
    } catch (e) {
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Makes a token and reloads the list. Null when it failed; [error] says
  /// why. The secret in the result is not kept anywhere.
  Future<CreatedToken?> createToken(
    String name, {
    bool allowMemoryWrite = true,
  }) async {
    final CreatedToken created;
    try {
      created = await _service.createToken(
        name,
        allowMemoryWrite: allowMemoryWrite,
      );
    } catch (e) {
      _error = describeError(e);
      notify();
      return null;
    }
    await load();
    return created;
  }

  Future<bool> revokeToken(String id) => _then(() => _service.revokeToken(id));

  Future<bool> revokeAssistant(String clientId) =>
      _then(() => _service.revokeAssistant(clientId));

  Future<bool> deleteMemory(String id) =>
      _then(() => _service.deleteMemory(id));

  /// Runs [action], then reloads. False, with [error] set, when it failed.
  Future<bool> _then(Future<void> Function() action) async {
    try {
      await action();
    } catch (e) {
      _error = describeError(e);
      notify();
      return false;
    }
    await load();
    return true;
  }

  void _onAuthChanged() {
    if (_auth?.isAuthenticated ?? true) return;
    if (_connections == null && _memory.isEmpty) return;
    _connections = null;
    _memory = const [];
    _error = null;
    notify();
  }

  @override
  void dispose() {
    _auth?.removeListener(_onAuthChanged);
    super.dispose();
  }
}
