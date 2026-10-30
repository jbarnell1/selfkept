import 'package:flutter/material.dart';

/// A yes or no question. True only when the person picks [confirmLabel];
/// "Keep" or dismissing the dialog is false.
Future<bool> confirmAction(
  BuildContext context, {
  String? title,
  required String message,
  required String confirmLabel,
}) async {
  final answer = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: title == null ? null : Text(title),
      content: Text(message),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: const Text('Keep'),
        ),
        TextButton(
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
  return answer ?? false;
}

/// One short message at the bottom of the screen, replacing any already
/// showing. Takes the messenger so callers can capture it before an await.
void showMessage(ScaffoldMessengerState messenger, String text) {
  messenger
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(text)));
}
