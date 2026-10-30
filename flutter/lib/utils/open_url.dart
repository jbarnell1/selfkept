import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:url_launcher/url_launcher.dart';

/// Opens a URL outside the app. Pages and providers take one as a parameter
/// (or read it from Provider) so tests can capture the address.
typedef UrlOpener = Future<bool> Function(Uri uri);

/// For a round trip that comes back to this app (Oura's sign-in): on web the
/// same tab goes there and returns; elsewhere the system browser opens.
Future<bool> openUrl(Uri uri) => launchUrl(
  uri,
  mode: LaunchMode.externalApplication,
  webOnlyWindowName: '_self',
);

/// For a link to read or download (docs, Oura's developer page, the phone
/// app): a new tab on web, so the page someone is filling in stays open.
/// This is the [UrlOpener] the app provides.
Future<bool> openExternalUrl(Uri uri) => launchUrl(
  uri,
  mode: LaunchMode.externalApplication,
  webOnlyWindowName: '_blank',
);

/// Opens [url] with the provided [UrlOpener] and says so in a snack bar when
/// it cannot.
Future<void> openLink(BuildContext context, String url) async {
  final messenger = ScaffoldMessenger.of(context);
  final open = context.read<UrlOpener>();
  var ok = false;
  try {
    ok = await open(Uri.parse(url));
  } catch (_) {
    ok = false;
  }
  if (!ok) {
    messenger.showSnackBar(
      const SnackBar(content: Text('Could not open that link.')),
    );
  }
}
