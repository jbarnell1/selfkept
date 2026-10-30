import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter_web_auth_2/flutter_web_auth_2.dart';
import 'package:url_launcher/url_launcher.dart';

import '../brand/brand.dart';
import '../config/app_config.dart';
import '../models/sso.dart';
import '../services/sso_service.dart';
import '../utils/api_error.dart';
import '../utils/safe_notifier.dart';
import 'auth_provider.dart';

/// Opens a provider trip. On a phone it returns the app link the trip ended
/// on, or null when the person closed the sheet. On the web the tab itself
/// goes to the provider and comes back to a page, so it returns null.
typedef SsoBrowser = Future<Uri?> Function(Uri url);

Future<Uri?> openSsoBrowser(Uri url) async {
  if (kIsWeb) {
    await launchUrl(url, webOnlyWindowName: '_self');
    return null;
  }
  try {
    final result = await FlutterWebAuth2.authenticate(
      url: url.toString(),
      callbackUrlScheme: Brand.deepLinkScheme,
    );
    return Uri.parse(result);
  } on PlatformException catch (e) {
    if (e.code == 'CANCELED') return null;
    rethrow;
  }
}

/// Provider sign-in (Pocket ID and the like): the sign-in button, and the
/// Connect row under You, Account. The server decides everything that
/// matters; this drives the browser and says how it went.
class SsoProvider extends ChangeNotifier with SafeNotifier {
  final SsoService _service;
  final AuthProvider _auth;
  final SsoBrowser _browser;
  final String _client;

  SsoInfo _info = SsoInfo.off;
  SsoLinkStatus? _link;
  bool _busy = false;
  String? _error;
  String? _notice;

  SsoProvider({
    required this._service,
    required this._auth,
    SsoBrowser? browser,
    String? client,
  }) : _browser = browser ?? openSsoBrowser,
       _client = client ?? AppConfig.clientKind;

  SsoInfo get info => _info;
  SsoLinkStatus? get link => _link;
  bool get isBusy => _busy;
  String? get error => _error;
  String? get notice => _notice;

  /// Asks the server whether the button should show. A server that does not
  /// answer, or predates provider sign-in, has no button.
  Future<void> loadInfo() async {
    try {
      _info = await _service.info();
    } catch (_) {
      _info = SsoInfo.off;
    }
    notify();
  }

  /// The sign-in button. True when a session was started here (a phone); on
  /// the web the page leaves for the provider instead.
  Future<bool> signIn() async {
    _error = null;
    _busy = true;
    notify();
    try {
      final back = await _browser(_service.startUrl(_client));
      if (back == null) return false; // web: navigating; phone: closed
      final failure = back.queryParameters['error'];
      final code = back.queryParameters['code'];
      if (failure != null || code == null) {
        _error = SsoMessages.forError(failure ?? 'provider', _info);
        return false;
      }
      return await _auth.completeSignIn(() => _service.exchange(code));
    } catch (e) {
      _error = describeError(e);
      return false;
    } finally {
      _busy = false;
      notify();
    }
  }

  /// The `?sso=` a web trip lands back with, on the sign-in page or under You.
  void showReturn(String? code) {
    if (code == null || code.isEmpty) return;
    if (code == 'linked') {
      _notice = SsoMessages.linked;
      _error = null;
      loadLink();
    } else {
      _error = SsoMessages.forError(code, _info);
      _notice = null;
    }
    notify();
  }

  Future<void> loadLink() async {
    try {
      final status = await _service.linkStatus();
      _link = status;
      _info = status.info;
    } catch (_) {
      _link = null;
    }
    notify();
  }

  /// Ties the signed-in account to a login at the provider.
  Future<void> connect() async {
    _error = null;
    _notice = null;
    _busy = true;
    notify();
    try {
      final back = await _browser(await _service.linkStartUrl(_client));
      if (back == null) return;
      if (back.queryParameters['linked'] == '1') {
        _notice = SsoMessages.linked;
        await loadLink();
      } else {
        _error = SsoMessages.forError(
          back.queryParameters['error'] ?? 'provider',
          _info,
        );
      }
    } catch (e) {
      _error = describeError(e);
    } finally {
      _busy = false;
      notify();
    }
  }

  Future<void> disconnect() async {
    _error = null;
    _notice = null;
    _busy = true;
    notify();
    try {
      await _service.unlink();
      await loadLink();
    } on ApiException catch (e) {
      _error = e.isNotFound ? null : e.message;
      await loadLink();
    } catch (e) {
      _error = describeError(e);
    } finally {
      _busy = false;
      notify();
    }
  }

  void clearMessages() {
    if (_error == null && _notice == null) return;
    _error = null;
    _notice = null;
    notify();
  }
}
