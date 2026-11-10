import 'package:flutter/foundation.dart';

import '../utils/safe_notifier.dart';

/// One signal shared by the providers behind goals, targets, the journal,
/// the body readings and the strength log. A journal entry can meet a
/// target and a new goal changes Progress, so whoever saves something says
/// so here and the others reload what they have on screen.
class RecordChanges extends ChangeNotifier with SafeNotifier {
  Object? _origin;

  /// Who made the last change. A provider skips its own announcements.
  Object? get origin => _origin;

  void changed(Object origin) {
    _origin = origin;
    notify();
  }
}
