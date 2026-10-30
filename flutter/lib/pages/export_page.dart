import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../models/enums.dart';
import '../models/export.dart';
import '../services/export_service.dart';
import '../services/pdf_saver/pdf_saver.dart';
import '../theme/app_theme.dart';
import '../theme/tokens.dart';
import '../utils/api_error.dart';
import '../widgets/copyable_value.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';
import '../widgets/subpage_app_bar.dart';

typedef FileSaver =
    Future<void> Function(Uint8List bytes, String fileName, String mimeType);

/// Take your data with you: a redacted bundle to paste into a stronger model
/// you choose, or everything as JSON.
class ExportPage extends StatefulWidget {
  /// Replaceable so tests can see what would be saved.
  final FileSaver save;

  const ExportPage({super.key, this.save = saveFile});

  static const String bundleIntro =
      'When a question needs a model this server does not run, build a summary here, read it, and paste it in yourself. Nothing is sent anywhere by this page.';
  static const String fullIntro =
      'Everything you have here, as one file you keep: daily values, check-ins with notes, lab panels, insights and what assistants remember.';
  static const String notesWarning =
      'Notes can mention people and places. Read the preview before you share it.';

  static String categoryLabel(ShareCategory c) => switch (c) {
    ShareCategory.vitals => 'Heart and recovery',
    ShareCategory.labs => 'Blood tests',
    ShareCategory.checkins => 'Check-ins',
    _ => c.displayName,
  };

  @override
  State<ExportPage> createState() => _ExportPageState();
}

class _ExportPageState extends State<ExportPage> {
  BundleOptions _options = BundleOptions.defaults;
  PrivacyBundle? _preview;
  bool _busy = false;
  String? _error;

  void _change(BundleOptions next) => setState(() {
    _options = next;
    _preview = null; // a preview always matches the options shown
    _error = null;
  });

  Future<void> _run(Future<void> Function(ExportService s) action) async {
    final service = context.read<ExportService>();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await action(service);
    } catch (e) {
      if (mounted) {
        setState(
          () => _error = e is ApiException
              ? e.message
              : 'That did not work. Try again.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _previewBundle() => _run((s) async {
    final bundle = await s.bundle(_options, export: false);
    if (mounted) setState(() => _preview = bundle);
  });

  Future<void> _copyBundle() => _run((s) async {
    final bundle = await s.bundle(_options, export: true);
    if (!mounted) return;
    setState(() => _preview = bundle);
    await copyToClipboard(context, bundle.text);
  });

  Future<void> _saveBundle() => _run((s) async {
    final bundle = await s.bundle(_options, export: true);
    await widget.save(
      Uint8List.fromList(utf8.encode(bundle.text)),
      'health-summary-${_stamp()}.md',
      'text/markdown',
    );
    if (mounted) setState(() => _preview = bundle);
  });

  Future<void> _saveFull() => _run((s) async {
    final bytes = await s.full();
    await widget.save(
      bytes,
      'health-export-${_stamp()}.json',
      'application/json',
    );
  });

  static String _stamp() => DateFormat('yyyy-MM-dd').format(DateTime.now());

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final o = _options;
    return Scaffold(
      appBar: const SubpageAppBar(fallback: '/you'),
      body: SafeArea(
        child: PageBody(
          children: [
            const PageHeading(title: 'Export'),
            const SectionHeader(title: 'Summary for another model'),
            SurfaceCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    ExportPage.bundleIntro,
                    style: theme.textTheme.bodyMedium,
                  ),
                  const SizedBox(height: Space.x4),
                  Text('What to include', style: theme.textTheme.labelLarge),
                  const SizedBox(height: Space.x2),
                  Wrap(
                    spacing: Space.x2,
                    runSpacing: Space.x2,
                    children: [
                      for (final c in BundleOptions.offered)
                        FilterChip(
                          label: Text(ExportPage.categoryLabel(c)),
                          selected: o.categories.contains(c),
                          onSelected: _busy
                              ? null
                              : (on) => _change(
                                  o.copyWith(
                                    categories: on
                                        ? {...o.categories, c}
                                        : ({...o.categories}..remove(c)),
                                  ),
                                ),
                        ),
                    ],
                  ),
                  const SizedBox(height: Space.x4),
                  Text('How far back', style: theme.textTheme.labelLarge),
                  const SizedBox(height: Space.x2),
                  SegmentedButton<int>(
                    showSelectedIcon: false,
                    segments: const [
                      ButtonSegment(value: 30, label: Text('30 days')),
                      ButtonSegment(value: 90, label: Text('90 days')),
                      ButtonSegment(value: 180, label: Text('180 days')),
                    ],
                    selected: {o.days},
                    onSelectionChanged: _busy
                        ? null
                        : (s) => _change(o.copyWith(days: s.first)),
                  ),
                  const SizedBox(height: Space.x2),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Keep real dates'),
                    subtitle: const Text(
                      'Off: days are counted back from today.',
                    ),
                    value: o.keepDates,
                    onChanged: _busy
                        ? null
                        : (v) => _change(o.copyWith(keepDates: v)),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Include check-in notes'),
                    subtitle: o.includeNotes
                        ? const Text(ExportPage.notesWarning)
                        : const Text('Off: only the mood goes in.'),
                    value: o.includeNotes,
                    onChanged: _busy
                        ? null
                        : (v) => _change(o.copyWith(includeNotes: v)),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Include my name'),
                    subtitle: const Text(
                      'Off: an age band and sex at birth only.',
                    ),
                    value: o.includeName,
                    onChanged: _busy
                        ? null
                        : (v) => _change(o.copyWith(includeName: v)),
                  ),
                  const SizedBox(height: Space.x4),
                  Wrap(
                    spacing: Space.x3,
                    runSpacing: Space.x2,
                    children: [
                      FilledButton.tonal(
                        onPressed: _busy || o.categories.isEmpty
                            ? null
                            : _previewBundle,
                        child: const Text('Preview'),
                      ),
                      OutlinedButton.icon(
                        onPressed: _busy || _preview == null
                            ? null
                            : _copyBundle,
                        icon: const Icon(Icons.copy_outlined, size: 18),
                        label: const Text('Copy'),
                      ),
                      OutlinedButton.icon(
                        onPressed: _busy || _preview == null
                            ? null
                            : _saveBundle,
                        icon: const Icon(Icons.download_outlined, size: 18),
                        label: const Text('Save'),
                      ),
                    ],
                  ),
                  if (_busy) ...[
                    const SizedBox(height: Space.x3),
                    const LinearProgressIndicator(),
                  ],
                  if (_error != null) ...[
                    const SizedBox(height: Space.x3),
                    AttentionLine(_error!),
                  ],
                  if (_preview != null) ...[
                    const SizedBox(height: Space.x4),
                    Text(
                      'About ${_preview!.approximateTokens} tokens, ${_preview!.characters} characters. This is exactly what Copy and Save give you.',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                    const SizedBox(height: Space.x2),
                    Container(
                      key: const Key('bundle-preview'),
                      constraints: const BoxConstraints(maxHeight: 420),
                      padding: const EdgeInsets.all(Space.x4),
                      decoration: BoxDecoration(
                        color: theme.colorScheme.surfaceContainerHighest,
                        borderRadius: Radii.controlRadius,
                      ),
                      child: SingleChildScrollView(
                        child: SelectableText(
                          _preview!.text,
                          style: AppTheme.numeric(
                            context,
                            base: theme.textTheme.bodySmall,
                          ),
                        ),
                      ),
                    ),
                  ],
                ],
              ),
            ),
            const SectionHeader(title: 'Everything'),
            SurfaceCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(ExportPage.fullIntro, style: theme.textTheme.bodyMedium),
                  const SizedBox(height: Space.x4),
                  OutlinedButton.icon(
                    onPressed: _busy ? null : _saveFull,
                    icon: const Icon(Icons.download_outlined, size: 18),
                    label: const Text('Download everything (JSON)'),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
