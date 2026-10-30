import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../providers/auth_provider.dart';
import '../../providers/sso_provider.dart';
import '../../theme/tokens.dart';
import '../../utils/return_path.dart';
import '../../widgets/public_scaffold.dart';
import '../../widgets/status_views.dart';

/// Sign in with email and password, or with the household's sign-in
/// provider when the server has one.
class LoginPage extends StatefulWidget {
  /// Where to go after signing in: the `from` query value. Only in-app
  /// locations starting with a slash are used.
  final String? from;

  /// The `sso` query a failed web provider trip lands back with.
  final String? ssoResult;

  const LoginPage({super.key, this.from, this.ssoResult});

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _obscure = true;
  String? _notice;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      if (!mounted) return;
      if (context.read<AuthProvider>().consumeSessionExpired()) {
        setState(
          () => _notice = 'Your session ended. Sign in again to continue.',
        );
      }
      final sso = context.read<SsoProvider>();
      await sso.loadInfo();
      sso.showReturn(widget.ssoResult);
    });
  }

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_form.currentState?.validate() ?? false)) return;
    setState(() => _notice = null);
    final auth = context.read<AuthProvider>();
    final router = GoRouter.maybeOf(context);
    final target = safeReturnPath(widget.from);
    await auth.login(email: _email.text, password: _password.text);
    if (auth.isAuthenticated && target != null) router?.go(target);
  }

  Future<void> _signInWithProvider() async {
    setState(() => _notice = null);
    final auth = context.read<AuthProvider>();
    final router = GoRouter.maybeOf(context);
    final target = safeReturnPath(widget.from);
    await context.read<SsoProvider>().signIn();
    if (auth.isAuthenticated && target != null) router?.go(target);
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final sso = context.watch<SsoProvider>();
    final theme = Theme.of(context);
    return PublicScaffold(
      title: 'Welcome back.',
      lead: _notice,
      footer: Wrap(
        alignment: WrapAlignment.center,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          Text('Have an invite code?', style: theme.textTheme.bodyMedium),
          TextButton(
            onPressed: () => context.go('/join'),
            child: const Text('Join a household'),
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
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                  textInputAction: TextInputAction.next,
                  autofillHints: const [
                    AutofillHints.username,
                    AutofillHints.email,
                  ],
                  autocorrect: false,
                  decoration: const InputDecoration(labelText: 'Email'),
                  validator: (v) => (v == null || v.trim().isEmpty)
                      ? 'Your email address.'
                      : null,
                ),
                const SizedBox(height: Space.x4),
                TextFormField(
                  controller: _password,
                  obscureText: _obscure,
                  textInputAction: TextInputAction.done,
                  autofillHints: const [AutofillHints.password],
                  onFieldSubmitted: (_) => _submit(),
                  decoration: InputDecoration(
                    labelText: 'Password',
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
                  validator: (v) =>
                      (v == null || v.isEmpty) ? 'Your password.' : null,
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
              : const Text('Sign in'),
        ),
        if (sso.info.enabled) ...[
          const SizedBox(height: Space.x3),
          OutlinedButton.icon(
            onPressed: auth.isLoading || sso.isBusy
                ? null
                : _signInWithProvider,
            icon: const Icon(Icons.key_outlined),
            label: Text('Sign in with ${sso.info.displayName}'),
          ),
        ],
        if (sso.error != null) AttentionLine(sso.error!),
      ],
    );
  }
}
