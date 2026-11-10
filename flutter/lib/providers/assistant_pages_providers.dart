import 'package:flutter/foundation.dart';

import '../models/assistant.dart';
import '../models/connect.dart';
import '../services/assistant_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';

// Page-level providers for the assistant's smaller screens. Each lives as
// long as its page.

/// "Things worth trying": the playbook entries that apply today.
class AssistantPlaybookProvider extends ChangeNotifier with SafeNotifier {
  final AssistantService _service;

  List<PlaybookEntry> _entries = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  String? _error;

  AssistantPlaybookProvider(this._service);

  List<PlaybookEntry> get entries => _entries;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get error => _error;

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      _entries = await _service.playbook();
      _hasLoaded = true;
    } catch (e) {
      debugPrint('Playbook did not load: $e');
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }
}

/// "What the assistant remembers".
class AssistantMemoryProvider extends ChangeNotifier with SafeNotifier {
  final AssistantService _service;

  List<AiMemoryItem> _items = const [];
  bool _loading = false;
  bool _hasLoaded = false;
  String? _error;
  String? _deleteError;

  AssistantMemoryProvider(this._service);

  List<AiMemoryItem> get items => _items;
  bool get isLoading => _loading;
  bool get hasLoaded => _hasLoaded;
  String? get error => _error;
  String? get deleteError => _deleteError;

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      _items = await _service.memory();
      _hasLoaded = true;
    } catch (e) {
      debugPrint('Memory did not load: $e');
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  Future<bool> delete(String id) async {
    _deleteError = null;
    try {
      await _service.deleteMemory(id);
      _items = [
        for (final m in _items)
          if (m.id != id) m,
      ];
      notify();
      return true;
    } catch (e) {
      _deleteError = describeError(e);
      notify();
      return false;
    }
  }
}

/// "Evening reminder". The ntfy address is never kept here once saved: the
/// server answers with it masked, and that is all this holds.
class AssistantNudgesProvider extends ChangeNotifier with SafeNotifier {
  final AssistantService _service;

  NudgeSettings? _settings;
  bool _loading = false;
  bool _saving = false;
  bool _testing = false;
  String? _error;
  String? _saveError;
  NudgeTestResult? _testResult;
  String? _testError;

  AssistantNudgesProvider(this._service);

  NudgeSettings? get settings => _settings;
  bool get isLoading => _loading;
  bool get isSaving => _saving;
  bool get isTesting => _testing;
  String? get error => _error;
  String? get saveError => _saveError;
  NudgeTestResult? get testResult => _testResult;

  /// A refusal before anything was sent (no address, too many tries).
  String? get testError => _testError;

  Future<void> load() async {
    _loading = true;
    _error = null;
    notify();
    try {
      _settings = await _service.nudges();
    } catch (e) {
      debugPrint('Reminder settings did not load: $e');
      _error = describeError(e);
    } finally {
      _loading = false;
      notify();
    }
  }

  /// Saves. True when the server took it; [saveError] holds its reason when
  /// not. The request body is never logged.
  Future<bool> save(NudgeUpdate update) async {
    _saving = true;
    _saveError = null;
    notify();
    try {
      _settings = await _service.saveNudges(update);
      return true;
    } catch (e) {
      _saveError = describeError(e);
      return false;
    } finally {
      _saving = false;
      notify();
    }
  }

  Future<void> test() async {
    _testing = true;
    _testResult = null;
    _testError = null;
    notify();
    try {
      _testResult = await _service.testNudge();
    } catch (e) {
      _testError = describeError(e);
    } finally {
      _testing = false;
      notify();
    }
  }
}

/// "Check the home model" (admin).
class AssistantModelCheckProvider extends ChangeNotifier with SafeNotifier {
  final AssistantService _service;

  ModelCheck? _result;
  bool _running = false;
  String? _error;

  AssistantModelCheckProvider(this._service);

  ModelCheck? get result => _result;
  bool get isRunning => _running;
  String? get error => _error;

  Future<void> run() async {
    _running = true;
    _error = null;
    notify();
    try {
      _result = await _service.modelCheck();
    } catch (e) {
      debugPrint('Model check failed: $e');
      _error = describeError(e);
    } finally {
      _running = false;
      notify();
    }
  }
}
