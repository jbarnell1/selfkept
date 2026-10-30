import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../models/enums.dart';
import '../../models/person.dart';
import '../../providers/profile_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/key_value_row.dart';
import '../../widgets/page_body.dart';
import '../../widgets/status_views.dart';
import '../../widgets/surface_card.dart';

/// Your profile: name, sex at birth, birth date and units, with an editor.
class ProfileSection extends StatelessWidget {
  const ProfileSection({super.key});

  @override
  Widget build(BuildContext context) {
    final profile = context.watch<ProfileProvider>();
    final person = profile.person;
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(
          title: 'Profile',
          trailing: person == null
              ? null
              : TextButton(
                  onPressed: () => _ProfileEditor.show(context, person),
                  child: const Text('Edit'),
                ),
        ),
        SurfaceCard(
          child: person == null
              ? (profile.isLoading
                    ? const LoadingView()
                    : ErrorView(
                        title: 'Your profile did not load.',
                        detail: profile.error,
                        onRetry: profile.load,
                      ))
              : Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      person.displayName,
                      style: theme.textTheme.headlineMedium,
                    ),
                    const SizedBox(height: Space.x4),
                    KeyValueRow('Sex at birth', person.sexAtBirth.displayName),
                    KeyValueRow(
                      'Birth date',
                      person.birthDate == null
                          ? 'Not set'
                          : DateFormat.yMMMMd().format(person.birthDate!),
                      numeric: person.birthDate != null,
                    ),
                    KeyValueRow('Units', person.units.displayName),
                    KeyValueRow('Time zone', person.timeZone),
                  ],
                ),
        ),
      ],
    );
  }
}

/// Bottom sheet that edits display name, sex at birth, birth date and units.
class _ProfileEditor extends StatefulWidget {
  final Person person;

  const _ProfileEditor({required this.person});

  static Future<void> show(BuildContext context, Person person) {
    final profile = context.read<ProfileProvider>();
    return showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => ChangeNotifierProvider.value(
        value: profile,
        child: _ProfileEditor(person: person),
      ),
    );
  }

  @override
  State<_ProfileEditor> createState() => _ProfileEditorState();
}

class _ProfileEditorState extends State<_ProfileEditor> {
  late final TextEditingController _name;
  late SexAtBirth _sex;
  late Units _units;
  DateTime? _birthDate;

  @override
  void initState() {
    super.initState();
    _name = TextEditingController(text: widget.person.displayName);
    _sex = widget.person.sexAtBirth;
    _units = widget.person.units;
    _birthDate = widget.person.birthDate;
  }

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: _birthDate ?? DateTime(now.year - 30, now.month, now.day),
      firstDate: DateTime(now.year - 120),
      lastDate: now,
      helpText: 'Birth date',
    );
    if (picked != null) setState(() => _birthDate = picked);
  }

  Future<void> _save() async {
    final name = _name.text.trim();
    if (name.isEmpty) return;
    final profile = context.read<ProfileProvider>();
    final ok = await profile.save(
      widget.person.copyWith(
        displayName: name,
        sexAtBirth: _sex,
        units: _units,
        birthDate: _birthDate,
        clearBirthDate: _birthDate == null,
      ),
    );
    if (ok && mounted) Navigator.of(context).pop();
  }

  @override
  Widget build(BuildContext context) {
    final profile = context.watch<ProfileProvider>();
    final theme = Theme.of(context);
    final bottom = MediaQuery.viewInsetsOf(context).bottom;
    return Padding(
      padding: EdgeInsets.fromLTRB(
        Space.gutter,
        0,
        Space.gutter,
        Space.x6 + bottom,
      ),
      child: ListView(
        shrinkWrap: true,
        children: [
          Text('Edit profile', style: theme.textTheme.headlineSmall),
          const SizedBox(height: Space.x6),
          TextField(
            controller: _name,
            textCapitalization: TextCapitalization.words,
            decoration: const InputDecoration(labelText: 'Display name'),
          ),
          const SizedBox(height: Space.x6),
          Text('Sex at birth', style: theme.textTheme.labelMedium),
          const SizedBox(height: Space.x2),
          SegmentedButton<SexAtBirth>(
            segments: [
              for (final s in SexAtBirth.values)
                ButtonSegment(value: s, label: Text(s.displayName)),
            ],
            selected: {_sex},
            showSelectedIcon: false,
            onSelectionChanged: (v) => setState(() => _sex = v.first),
          ),
          const SizedBox(height: Space.x6),
          Text('Birth date', style: theme.textTheme.labelMedium),
          const SizedBox(height: Space.x2),
          Row(
            children: [
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: _pickDate,
                  icon: const Icon(Icons.calendar_today_outlined, size: 18),
                  label: Text(
                    _birthDate == null
                        ? 'Choose a date'
                        : DateFormat.yMMMMd().format(_birthDate!),
                  ),
                ),
              ),
              if (_birthDate != null)
                IconButton(
                  onPressed: () => setState(() => _birthDate = null),
                  icon: const Icon(Icons.close),
                  tooltip: 'Clear birth date',
                ),
            ],
          ),
          const SizedBox(height: Space.x6),
          Text('Units', style: theme.textTheme.labelMedium),
          const SizedBox(height: Space.x2),
          SegmentedButton<Units>(
            segments: [
              for (final u in Units.values)
                ButtonSegment(value: u, label: Text(u.displayName)),
            ],
            selected: {_units},
            showSelectedIcon: false,
            onSelectionChanged: (v) => setState(() => _units = v.first),
          ),
          AttentionLine(profile.error ?? ''),
          const SizedBox(height: Space.x8),
          FilledButton(
            onPressed: profile.isSaving ? null : _save,
            child: Text(profile.isSaving ? 'Saving' : 'Save'),
          ),
        ],
      ),
    );
  }
}
