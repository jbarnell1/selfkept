import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/labs_service.dart';
import '../services/pdf_saver/pdf_saver.dart';
import 'dialogs.dart';

/// Fetches the last 90 days as a PDF for a clinician and hands it over: a new
/// tab on web, the share sheet on a phone.
class ClinicianSummaryButton extends StatefulWidget {
  /// Replaceable so a test can see the bytes without touching the platform.
  final Future<void> Function(Uint8List bytes, String fileName) save;

  const ClinicianSummaryButton({super.key, this.save = saveAndOpenPdf});

  static const String label = 'Clinician summary';
  static const String failureText = 'The summary did not download.';
  static const String fileName = 'summary.pdf';
  static const int days = 90;

  @override
  State<ClinicianSummaryButton> createState() => _ClinicianSummaryButtonState();
}

class _ClinicianSummaryButtonState extends State<ClinicianSummaryButton> {
  bool _busy = false;

  Future<void> _open() async {
    final labs = context.read<LabsService>();
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _busy = true);
    try {
      final now = DateTime.now();
      final bytes = await labs.summaryPdf(
        from: now.subtract(const Duration(days: ClinicianSummaryButton.days)),
        to: now,
      );
      await widget.save(bytes, ClinicianSummaryButton.fileName);
    } catch (_) {
      showMessage(messenger, ClinicianSummaryButton.failureText);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: OutlinedButton.icon(
        onPressed: _busy ? null : _open,
        icon: const Icon(Icons.picture_as_pdf, size: 18),
        label: const Text(ClinicianSummaryButton.label),
      ),
    );
  }
}
