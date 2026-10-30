import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../providers/auth_provider.dart';
import '../../theme/tokens.dart';
import '../../widgets/public_scaffold.dart';
import '../../widgets/status_views.dart';

/// Join an existing household with a one-time invite code and a new account.
class JoinPage extends StatefulWidget {
  /// Optional code carried in the URL (/join?code=...).
  final String? initialCode;

  const JoinPage({super.key, this.initialCode});

  @override
  State<JoinPage> createState() => _JoinPageState();
}

class _JoinPageState extends State<JoinPage> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _code;
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _obscure = true;

  @override
  void initState() {
    super.initState();
    _code = TextEditingController(text: widget.initialCode ?? '');
  }

  @override
  void dispose() {
    _code.dispose();
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
      inviteCode: _code.text,
    );
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final theme = Theme.of(context);
    return PublicScaffold(
      title: 'Join a household.',
      lead:
          'The admin of the household gives you a one-time code. '
          'Enter it with the account you want to use.',
      footer: Wrap(
        alignment: WrapAlignment.center,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          Text('Already have an account?', style: theme.textTheme.bodyMedium),
          TextButton(
            onPressed: () => context.go('/login'),
            child: const Text('Sign in'),
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
                  controller: _code,
                  textInputAction: TextInputAction.next,
                  autocorrect: false,
                  enableSuggestions: false,
                  textCapitalization: TextCapitalization.characters,
                  decoration: const InputDecoration(labelText: 'Invite code'),
                  validator: (v) => (v == null || v.trim().isEmpty)
                      ? 'The code from your admin.'
                      : null,
                ),
                const SizedBox(height: Space.x4),
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
                  validator: (v) {
                    final value = v?.trim() ?? '';
                    if (value.isEmpty) {
                      return 'An email address to sign in with.';
                    }
                    if (!value.contains('@')) {
                      return 'That does not look like an email address.';
                    }
                    return null;
                  },
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
                  validator: (v) => (v == null || v.length < 10)
                      ? 'Use at least 10 characters.'
                      : null,
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
              : const Text('Join'),
        ),
      ],
    );
  }
}
