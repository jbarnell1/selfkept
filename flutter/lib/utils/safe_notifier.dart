import 'package:flutter/foundation.dart';

/// A [ChangeNotifier] that ignores notifications after it is disposed, so an
/// awaited call that finishes after its page has gone cannot throw.
mixin SafeNotifier on ChangeNotifier {
  bool _disposed = false;

  bool get isDisposed => _disposed;

  /// [notifyListeners], unless disposed.
  @protected
  void notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
