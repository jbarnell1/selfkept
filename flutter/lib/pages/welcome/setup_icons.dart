import 'package:flutter/material.dart';

import '../../models/setup.dart';

/// Icon per setup answer. Nothing branded: a shape that says what it is.
IconData setupItemIcon(SetupItem item) => switch (item) {
  SetupItem.appleHealth => Icons.phone_iphone,
  SetupItem.healthConnect => Icons.phone_android,
  SetupItem.oura => Icons.circle_outlined,
  SetupItem.labs => Icons.document_scanner_outlined,
  SetupItem.household => Icons.group_outlined,
  SetupItem.assistant => Icons.forum_outlined,
};
