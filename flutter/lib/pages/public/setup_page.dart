import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../brand/brand.dart';
import '../../providers/auth_provider.dart';
import '../../providers/startup_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/public_scaffold.dart';
import '../../widgets/server_address_field.dart';
import '../../widgets/status_views.dart';

/// Shown once per server, while no account exists. Registers the first
/// account and says plainly that it becomes the admin.
class SetupPage extends StatefulWidget {
  const SetupPage({super.key});

  @override
  State<SetupPage> createState() => _SetupPageState();
}

class _SetupPageState extends State<SetupPage> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _showServer = false;
  bool _obscure = true;

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_form.currentState?.validate() ?? false)) return;
    await context.read<AuthProvider>().register(
      email: _email.text,
      password: _password.text,
      displayName: _name.text,
    );
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final theme = Theme.of(context);
    final colors = AppColors.of(context);

    return PublicScaffold(
      title: 'Set up ${Brand.shortName}.',
      lead:
          'This server has no accounts yet. The first one you create becomes '
          'the admin: it names the household and invites everyone else.',
      footer: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextButton.icon(
            onPressed: () => setState(() => _showServer = !_showServer),
            icon: Icon(
              _showServer ? Icons.expand_less : Icons.dns_outlined,
              size: 18,
            ),
            label: Text(_showServer ? 'Hide server address' : 'Server address'),
          ),
          AnimatedSize(
            duration: Motion.standard,
            curve: Motion.curve,
            alignment: Alignment.topCenter,
            child: _showServer
                ? Padding(
                    padding: const EdgeInsets.only(top: Space.x3),
                    child: ServerAddressField(
                      onSaved: () => context.read<StartupProvider>().run(),
                    ),
                  )
                : const SizedBox(width: double.infinity, height: 0),
          ),
        ],
      ),
      children: [
        Form(
          key: _form,
          child: AutofillGroup(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                TextFormField(
                  controller: _name,
                  textInputAction: TextInputAction.next,
                  autofillHints: const [AutofillHints.name],
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(labelText: 'Your name'),
                  validator: (v) => (v == null || v.trim().isEmpty)
                      ? 'A name, so the household knows you.'
                      : null,
                ),
                const SizedBox(height: Space.x4),
                TextFormField(
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                  textInputAction: TextInputAction.next,
                  autofillHints: const [AutofillHints.email],
                  autocorrect: false,
                  decoration: const InputDecoration(labelText: 'Email'),
                  validator: _validateEmail,
                ),
                const SizedBox(height: Space.x4),
                TextFormField(
                  controller: _password,
                  obscureText: _obscure,
                  textInputAction: TextInputAction.done,
                  autofillHints: const [AutofillHints.newPassword],
                  onFieldSubmitted: (_) => _submit(),
                  decoration: InputDecoration(
                    labelText: 'Password',
                    helperText: 'At least 10 characters.',
                    suffixIcon: IconButton(
                      onPressed: () => setState(() => _obscure = !_obscure),
                      icon: Icon(
                        _obscure
                            ? Icons.visibility_outlined
                            : Icons.visibility_off_outlined,
                      ),
                      tooltip: _obscure ? 'Show password' : 'Hide password',
                    ),
                  ),
                  validator: _validatePassword,
                ),
              ],
            ),
          ),
        ),
        AttentionLine(auth.errorMessage ?? ''),
        const SizedBox(height: Space.x6),
        FilledButton(
          onPressed: auth.isLoading ? null : _submit,
          child: auth.isLoading
              ? SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(
                    strokeWidth: 2,
                    color: theme.colorScheme.onPrimary,
                  ),
                )
              : const Text('Create the admin account'),
        ),
        const SizedBox(height: Space.x4),
        Row(
          children: [
            Icon(Icons.shield_outlined, size: 16, color: colors.band),
            const SizedBox(width: Space.x2),
            Expanded(
              child: Text(
                'Everything stays on this server. Nothing is sent anywhere else.',
                style: theme.textTheme.bodySmall,
              ),
            ),
          ],
        ),
      ],
    );
  }
}

String? _validateEmail(String? value) {
  final v = value?.trim() ?? '';
  if (v.isEmpty) return 'An email address to sign in with.';
  if (!v.contains('@') || v.startsWith('@') || v.endsWith('@')) {
    return 'That does not look like an email address.';
  }
  return null;
}

String? _validatePassword(String? value) {
  if (value == null || value.length < 10) return 'Use at least 10 characters.';
  return null;
}
