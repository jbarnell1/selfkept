/// A `from` query value that is safe to navigate to after sign-in: an
/// in-app location that starts with a single slash. Anything else (another
/// site, a scheme, a protocol-relative `//host`) is dropped.
String? safeReturnPath(String? from) {
  if (from == null) return null;
  final value = from.trim();
  if (!value.startsWith('/')) return null;
  if (value.startsWith('//') || value.startsWith(r'/\')) return null;
  return value;
}
