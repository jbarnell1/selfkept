import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../models/lab.dart';
import '../providers/labs_provider.dart';
import '../services/labs_service.dart';
import '../services/ocr/ocr.dart';
import '../theme/tokens.dart';
import '../utils/api_error.dart';
import '../widgets/status_views.dart';
import '../widgets/subpage_app_bar.dart';
import '../widgets/dialogs.dart';

enum _Stage { choose, reading, sheet }

/// Add a panel: pick a photo or screenshot, read it on the phone, then check
/// every value in one form before anything is stored. Where no on-device
/// reading exists the form opens straight away, empty.
class LabEntryPage extends StatefulWidget {
  /// Replaceable so a test can choose whether scanning is available.
  final TextScanner? scanner;

  /// Candidates already read, as if a scan had just finished. For tests.
  @visibleForTesting
  final ParseResult? prefill;

  const LabEntryPage({super.key, this.scanner, this.prefill});

  static const String readingText = 'Reading the results...';
  static const String keepLabel = 'Keep these results';
  static const String unreadableText =
      'Could not read that image. You can type the values in.';
  static const String notANumber = 'Enter a number.';
  static const String needOneValue = 'Add at least one value.';
  static const String checkThisOne = 'Check this one';
  static const String notOnPanel = 'Not on this panel';

  /// Below this a scanned value is marked for a second look.
  static const double confidenceFloor = 0.7;

  /// A typed value as a number: thousands commas are dropped. Null when it is
  /// empty or not a number.
  static double? parseValue(String text) {
    final cleaned = text.trim().replaceAll(',', '');
    if (cleaned.isEmpty) return null;
    final value = double.tryParse(cleaned);
    if (value == null || !value.isFinite || value < 0) return null;
    return value;
  }

  @override
  State<LabEntryPage> createState() => _LabEntryPageState();
}

class _LabEntryPageState extends State<LabEntryPage> {
  late final TextScanner _scanner = widget.scanner ?? createTextScanner();
  final _formKey = GlobalKey<FormState>();
  final Map<String, TextEditingController> _controllers = {};
  final Set<String> _skipped = {};
  final Set<String> _edited = {};

  late _Stage _stage;
  Map<String, LabCandidate> _candidates = const {};
  Uint8List? _image;
  late DateTime _collectedOn;
  String _provider = 'manual';
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final now = DateTime.now();
    _collectedOn = DateTime(now.year, now.month, now.day);
    final prefill = widget.prefill;
    if (prefill != null) {
      _candidates = prefill.bestByMetric();
      _provider = 'rythm';
    }
    _stage = (_scanner.available && prefill == null)
        ? _Stage.choose
        : _Stage.sheet;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final labs = context.read<LabsProvider>();
      if (!labs.hasLoaded && !labs.loading) labs.load();
    });
  }

  @override
  void dispose() {
    for (final c in _controllers.values) {
      c.dispose();
    }
    super.dispose();
  }

  TextEditingController _controllerFor(LabMarker marker) {
    return _controllers.putIfAbsent(marker.metricKey, () {
      final candidate = _candidates[marker.metricKey];
      return TextEditingController(
        text: candidate == null ? '' : _editable(candidate.value),
      );
    });
  }

  static String _editable(double value) =>
      value == value.roundToDouble() ? '${value.round()}' : '$value';

  Future<void> _pick(ImageSource source) async {
    XFile? file;
    try {
      file = await ImagePicker().pickImage(
        source: source,
        maxWidth: 2000,
        imageQuality: 85,
      );
    } catch (_) {
      file = null;
    }
    if (file == null || !mounted) return;
    final labs = context.read<LabsService>();
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _stage = _Stage.reading);

    Map<String, LabCandidate> candidates = const {};
    Uint8List? bytes;
    var failed = false;
    try {
      bytes = await file.readAsBytes();
      final lines = await _scanner.linesFromImage(bytes);
      final parsed = await labs.parse(lines.join('\n'));
      candidates = parsed.bestByMetric();
    } catch (_) {
      failed = true;
    }
    if (!mounted) return;
    setState(() {
      _image = bytes;
      _provider = 'rythm';
      _candidates = candidates;
      for (final c in _controllers.values) {
        c.dispose();
      }
      _controllers.clear();
      _skipped.clear();
      _edited.clear();
      _stage = _Stage.sheet;
    });
    if (failed) {
      showMessage(messenger, LabEntryPage.unreadableText);
    }
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final picked = await showDatePicker(
      context: context,
      initialDate: _collectedOn.isAfter(today) ? today : _collectedOn,
      firstDate: DateTime(2000),
      lastDate: today,
    );
    if (picked == null || !mounted) return;
    setState(
      () => _collectedOn = DateTime(picked.year, picked.month, picked.day),
    );
  }

  Future<void> _submit(List<LabMarker> rows) async {
    final form = _formKey.currentState;
    if (form == null || !form.validate()) return;

    final results = <LabResultInput>[];
    for (final marker in rows) {
      if (_skipped.contains(marker.metricKey)) continue;
      final value = LabEntryPage.parseValue(
        _controllers[marker.metricKey]?.text ?? '',
      );
      if (value == null) continue;
      final candidate = _candidates[marker.metricKey];
      results.add(
        LabResultInput(
          metricKey: marker.metricKey,
          value: value,
          unit: _unitFor(marker),
          flag: candidate?.flag,
          rawText: candidate?.rawText,
        ),
      );
    }
    if (results.isEmpty) {
      setState(() => _error = LabEntryPage.needOneValue);
      return;
    }

    final service = context.read<LabsService>();
    final labs = context.read<LabsProvider>();
    final messenger = ScaffoldMessenger.of(context);
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await service.createReport(
        provider: _provider,
        collectedOn: _collectedOn,
        results: results,
        imageBase64: _image == null ? null : base64Encode(_image!),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = describeError(e);
      });
      return;
    }
    unawaited(labs.refresh());
    if (!mounted) return;
    _leave();
    showMessage(messenger, 'Kept.');
  }

  void _leave() {
    final navigator = Navigator.of(context);
    if (navigator.canPop()) {
      navigator.pop();
    } else {
      GoRouter.maybeOf(context)?.go('/labs');
    }
  }

  String _unitFor(LabMarker marker) {
    final unit = _candidates[marker.metricKey]?.unit;
    return (unit != null && unit.isNotEmpty) ? unit : marker.unit;
  }

  @override
  Widget build(BuildContext context) {
    final labs = context.watch<LabsProvider>();
    final rows = [
      for (final m in labs.markers)
        if (m.applicable && !m.isRatio) m,
    ];
    final sheet = _stage == _Stage.sheet;

    return Scaffold(
      appBar: const SubpageAppBar(
        fallback: '/labs',
        title: Text('Add results'),
      ),
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: SingleChildScrollView(
                padding: const EdgeInsets.fromLTRB(
                  Space.gutter,
                  Space.x6,
                  Space.gutter,
                  Space.x6,
                ),
                child: Align(
                  alignment: Alignment.topCenter,
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(
                      maxWidth: Space.maxContentWidth,
                    ),
                    child: switch (_stage) {
                      _Stage.choose => _ChooseStep(
                        onCamera: () => _pick(ImageSource.camera),
                        onGallery: () => _pick(ImageSource.gallery),
                        onType: () => setState(() => _stage = _Stage.sheet),
                      ),
                      _Stage.reading => const LoadingView(
                        message: LabEntryPage.readingText,
                      ),
                      _Stage.sheet => _buildSheet(context, labs, rows),
                    },
                  ),
                ),
              ),
            ),
            if (sheet) _buildBar(context, labs, rows),
          ],
        ),
      ),
    );
  }

  Widget _buildSheet(
    BuildContext context,
    LabsProvider labs,
    List<LabMarker> rows,
  ) {
    final theme = Theme.of(context);
    if (!labs.hasLoaded) {
      if (labs.error != null) {
        return ErrorView(
          title: 'Labs did not load.',
          detail: labs.error,
          onRetry: labs.load,
        );
      }
      return const LoadingView();
    }

    return Form(
      key: _formKey,
      autovalidateMode: AutovalidateMode.onUserInteraction,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          InkWell(
            onTap: _pickDate,
            borderRadius: Radii.controlRadius,
            child: InputDecorator(
              decoration: const InputDecoration(
                labelText: 'Collected on',
                suffixIcon: Icon(Icons.calendar_today_outlined, size: 18),
              ),
              child: Text(
                DateFormat.yMMMMd().format(_collectedOn),
                style: theme.textTheme.bodyLarge,
              ),
            ),
          ),
          const SizedBox(height: Space.x4),
          DropdownButtonFormField<String>(
            initialValue: _provider == 'rythm' ? 'rythm' : 'manual',
            decoration: const InputDecoration(labelText: 'Provider'),
            items: const [
              DropdownMenuItem(value: 'rythm', child: Text('Rythm')),
              DropdownMenuItem(value: 'manual', child: Text('Other')),
            ],
            onChanged: (v) => setState(() => _provider = v ?? 'manual'),
          ),
          for (final group in LabGroup.values)
            ..._groupRows(context, rows, group),
        ],
      ),
    );
  }

  List<Widget> _groupRows(
    BuildContext context,
    List<LabMarker> rows,
    LabGroup group,
  ) {
    final markers = [
      for (final m in rows)
        if (m.group == group) m,
    ];
    if (markers.isEmpty) return const [];
    final theme = Theme.of(context);
    return [
      Padding(
        padding: const EdgeInsets.only(top: Space.x6, bottom: Space.x2),
        child: Text(
          group.displayName,
          style: theme.textTheme.labelLarge?.copyWith(
            color: theme.colorScheme.secondary,
          ),
        ),
      ),
      for (final marker in markers) _row(context, marker),
    ];
  }

  Widget _row(BuildContext context, LabMarker marker) {
    final theme = Theme.of(context);
    final colors = AppColors.of(context);
    final key = marker.metricKey;
    final skipped = _skipped.contains(key);
    final candidate = _candidates[key];
    final flagged =
        candidate != null &&
        candidate.confidence < LabEntryPage.confidenceFloor &&
        !_edited.contains(key);
    final last = marker.latestValue;

    final amber = OutlineInputBorder(
      borderRadius: Radii.controlRadius,
      borderSide: BorderSide(color: colors.attention, width: 1),
    );

    return Padding(
      key: ValueKey('row-$key'),
      padding: const EdgeInsets.only(bottom: Space.x4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.only(bottom: Space.x1),
                  child: Text(
                    marker.displayName,
                    style: theme.textTheme.titleSmall?.copyWith(
                      decoration: skipped ? TextDecoration.lineThrough : null,
                      color: skipped
                          ? theme.colorScheme.onSurfaceVariant
                          : null,
                    ),
                  ),
                ),
                TextFormField(
                  key: ValueKey('field-$key'),
                  controller: _controllerFor(marker),
                  enabled: !skipped,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: InputDecoration(
                    suffixText: _unitFor(marker),
                    helperText: flagged ? LabEntryPage.checkThisOne : null,
                    enabledBorder: flagged ? amber : null,
                  ),
                  onChanged: (_) {
                    if (candidate != null && !_edited.contains(key)) {
                      setState(() => _edited.add(key));
                    }
                  },
                  validator: (text) {
                    if (text == null || text.trim().isEmpty) return null;
                    return LabEntryPage.parseValue(text) == null
                        ? LabEntryPage.notANumber
                        : null;
                  },
                ),
                if (last != null)
                  Padding(
                    padding: const EdgeInsets.only(top: Space.x1),
                    child: Text(
                      'Last time ${formatLabValue(last)} ${marker.unit}'.trim(),
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.only(top: Space.x6),
            child: IconButton(
              tooltip: skipped ? 'On this panel' : LabEntryPage.notOnPanel,
              onPressed: () => setState(() {
                if (!_skipped.remove(key)) _skipped.add(key);
              }),
              icon: Icon(
                skipped
                    ? Icons.add_circle_outline
                    : Icons.remove_circle_outline,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildBar(
    BuildContext context,
    LabsProvider labs,
    List<LabMarker> rows,
  ) {
    final colors = AppColors.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.surface,
        border: Border(top: BorderSide(color: colors.hairline)),
      ),
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: Space.maxContentWidth),
          child: Padding(
            padding: const EdgeInsets.all(Space.x4),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                FilledButton(
                  onPressed: (_saving || !labs.hasLoaded)
                      ? null
                      : () => _submit(rows),
                  child: Text(_saving ? 'Keeping...' : LabEntryPage.keepLabel),
                ),
                AttentionLine(_error ?? ''),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _ChooseStep extends StatelessWidget {
  final VoidCallback onCamera;
  final VoidCallback onGallery;
  final VoidCallback onType;

  const _ChooseStep({
    required this.onCamera,
    required this.onGallery,
    required this.onType,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    const big = EdgeInsets.symmetric(vertical: Space.x6, horizontal: Space.x4);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Add results', style: theme.textTheme.headlineMedium),
        const SizedBox(height: Space.x2),
        Text(
          'Take a photo of your results, or pick a screenshot. The reading happens on this phone.',
          style: theme.textTheme.bodyMedium?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: Space.x6),
        FilledButton.icon(
          style: FilledButton.styleFrom(padding: big),
          onPressed: onCamera,
          icon: const Icon(Icons.photo_camera_outlined),
          label: const Text('Take a photo'),
        ),
        const SizedBox(height: Space.x3),
        FilledButton.tonalIcon(
          style: FilledButton.styleFrom(padding: big),
          onPressed: onGallery,
          icon: const Icon(Icons.photo_library_outlined),
          label: const Text('Choose a screenshot'),
        ),
        const SizedBox(height: Space.x4),
        Center(
          child: TextButton(
            onPressed: onType,
            child: const Text('Type them in instead'),
          ),
        ),
      ],
    );
  }
}
