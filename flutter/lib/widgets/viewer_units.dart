import 'package:flutter/widgets.dart';
import 'package:provider/provider.dart';

import '../models/enums.dart';
import '../providers/auth_provider.dart';

/// The signed-in person's unit system, rebuilding when they change it.
/// Stored values stay metric; this only decides how they are shown.
Units viewerUnitsOf(BuildContext context) =>
    context.select<AuthProvider, Units>(
      (auth) => auth.user?.person.units ?? Units.metric,
    );

/// The same, read once, for callbacks.
Units readViewerUnits(BuildContext context) =>
    context.read<AuthProvider>().user?.person.units ?? Units.metric;
