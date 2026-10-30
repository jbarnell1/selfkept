import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../models/connect.dart';
import '../providers/auth_provider.dart';
import '../services/connect_service.dart';
import '../theme/tokens.dart';
import '../utils/api_error.dart';
import '../utils/open_url.dart';
import '../widgets/page_body.dart';
import '../widgets/status_views.dart';
import '../widgets/surface_card.dart';

/// Where an assistant sends a person to say yes or no. Reached at
/// `/connect?request=<id>`, signed in or not.
class ConsentPage extends StatefulWidget {
  /// The `request` query parameter. Null or empty when it is missing.
  final String? requestId;

  /// Sends the browser to the address the server returns. Tests pass their
  /// own to capture it.
  final UrlOpener opener;

  const ConsentPage({super.key, this.requestId, this.opener = openUrl});

  static const String expired =
      'This request has expired or was already answered. Start again from your assistant.';

  @override
  State<ConsentPage> createState() => _ConsentPageState();
}

class _ConsentPageState extends State<ConsentPage> {
  ConsentRequest? _request;
  bool _loading = true;
  bool _gone = false;
  bool _deciding = false;
  String _problem = '';

  String get _id => (widget.requestId ?? '').trim();

  @override
  void initState() {
    super.initState();
    if (_id.isEmpty) {
      _loading = false;
      _gone = true;
      return;
    }
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    if (!mounted) return;
    final service = context.read<ConnectService>();
    try {
      final request = await service.consent(_id);
      if (!mounted) return;
      setState(() {
        _request = request;
        _loading = false;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _gone = true;
        _loading = false;
      });
    }
  }

  Future<void> _decide(bool approve) async {
    final service = context.read<ConnectService>();
    final router = GoRouter.maybeOf(context);
    setState(() {
      _deciding = true;
      _problem = '';
    });
    try {
      final url = await service.decide(_id, approve);
      final uri = Uri.tryParse(url);
      if (uri == null || !uri.hasScheme || _unsafeScheme(uri.scheme)) {
        throw const FormatException('Unusable address');
      }
      final opened = await widget.opener(uri);
      if (!mounted) return;
      if (!opened) {
        setState(() {
          _deciding = false;
          _problem =
              'Your answer was saved, but the assistant could not be opened.';
        });
        return;
      }
      // On web this tab is already on its way to the assistant. On a phone
      // the assistant opened elsewhere, so come back to the app.
      if (!kIsWeb) router?.go('/connections');
    } on ApiException catch (e) {
      if (!mounted) return;
      if (e.isNotFound) {
        setState(() {
          _gone = true;
          _deciding = false;
        });
        return;
      }
      setState(() {
        _deciding = false;
        _problem = e.message;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _deciding = false;
        _problem = describeError(e);
      });
    }
  }

  static bool _unsafeScheme(String scheme) {
    final s = scheme.toLowerCase();
    return s == 'javascript' || s == 'data' || s == 'vbscript';
  }

  @override
  Widget build(BuildContext context) {
    final Widget body;
    final request = _request;
    if (_loading) {
      body = const LoadingView();
    } else if (_gone || request == null) {
      body = const _Expired();
    } else {
      body = _Ask(
        request: request,
        deciding: _deciding,
        problem: _problem,
        onDecide: _decide,
      );
    }
    return Scaffold(
      body: SafeArea(child: PageBody(children: [body])),
    );
  }
}

class _Expired extends StatelessWidget {
  const _Expired();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.x8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(ConsentPage.expired, style: theme.textTheme.headlineSmall),
          const SizedBox(height: Space.x6),
          Align(
            alignment: Alignment.centerLeft,
            child: OutlinedButton(
              onPressed: () => context.go('/'),
              child: const Text('Go to Today'),
            ),
          ),
        ],
      ),
    );
  }
}

class _Ask extends StatelessWidget {
  final ConsentRequest request;
  final bool deciding;
  final String problem;
  final void Function(bool approve) onDecide;

  const _Ask({
    required this.request,
    required this.deciding,
    required this.problem,
    required this.onDecide,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final name = context.watch<AuthProvider>().user?.displayName ?? '';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: Space.x6),
        Text(
          '${request.clientName} wants to connect',
          style: theme.textTheme.displaySmall,
        ),
        const SizedBox(height: Space.x2),
        Text(
          'It will return you to ${request.redirectHost}.',
          style: theme.textTheme.bodyMedium?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: Space.x6),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              for (final scope in request.scopes)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: Space.x1),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(
                        Icons.check,
                        size: 20,
                        color: AppColors.of(context).band,
                      ),
                      const SizedBox(width: Space.x3),
                      Expanded(
                        child: Text(
                          scope.description,
                          style: theme.textTheme.bodyMedium,
                        ),
                      ),
                    ],
                  ),
                ),
              if (name.isNotEmpty) ...[
                const SizedBox(height: Space.x3),
                Text('As $name', style: theme.textTheme.bodySmall),
              ],
            ],
          ),
        ),
        const SizedBox(height: Space.x6),
        Row(
          children: [
            FilledButton(
              onPressed: deciding ? null : () => onDecide(true),
              child: const Text('Allow'),
            ),
            const SizedBox(width: Space.x3),
            OutlinedButton(
              onPressed: deciding ? null : () => onDecide(false),
              child: const Text('Deny'),
            ),
            if (deciding) ...[
              const SizedBox(width: Space.x4),
              const SizedBox(
                width: 16,
                height: 16,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            ],
          ],
        ),
        AttentionLine(problem),
      ],
    );
  }
}
