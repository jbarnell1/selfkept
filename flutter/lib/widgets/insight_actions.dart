import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/insight.dart';
import '../providers/insights_provider.dart';
import 'dialogs.dart';

/// Sends a reaction to [insight] through the insights provider, so Today and
/// the archive agree at once, and says why when it does not land.
Future<void> reactToInsight(
  BuildContext context,
  Insight insight,
  InsightReaction reaction,
) => _run(context, (insights) => insights.react(insight, reaction));

/// Puts [insight] away, the same way.
Future<void> dismissInsight(BuildContext context, Insight insight) =>
    _run(context, (insights) => insights.dismiss(insight));

Future<void> _run(
  BuildContext context,
  Future<bool> Function(InsightsProvider insights) action,
) async {
  final insights = context.read<InsightsProvider>();
  final messenger = ScaffoldMessenger.of(context);
  final ok = await action(insights);
  final error = insights.error;
  if (!ok && error != null) showMessage(messenger, error);
}
