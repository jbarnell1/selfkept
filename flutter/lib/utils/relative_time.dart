import 'package:intl/intl.dart';

/// "Just now", "12 minutes ago", "Yesterday at 9:14 PM", or a date. Plain
/// words, no abbreviations.
String relativeTime(DateTime? at, {DateTime? now}) {
  if (at == null) return 'Never';
  final reference = (now ?? DateTime.now()).toLocal();
  final local = at.toLocal();
  final diff = reference.difference(local);
  if (diff.isNegative) return 'Just now';
  if (diff.inSeconds < 60) return 'Just now';
  if (diff.inMinutes < 60) {
    final m = diff.inMinutes;
    return '$m ${m == 1 ? 'minute' : 'minutes'} ago';
  }
  if (diff.inHours < 24 && local.day == reference.day) {
    final h = diff.inHours;
    return '$h ${h == 1 ? 'hour' : 'hours'} ago';
  }
  final yesterday = reference.subtract(const Duration(days: 1));
  if (local.year == yesterday.year &&
      local.month == yesterday.month &&
      local.day == yesterday.day) {
    return 'Yesterday at ${DateFormat.jm().format(local)}';
  }
  if (diff.inDays < 7) {
    return '${DateFormat.EEEE().format(local)} at ${DateFormat.jm().format(local)}';
  }
  return DateFormat.yMMMd().format(local);
}

/// [relativeTime] for the middle of a sentence ("Last sync just now", "Last
/// sync Monday at 9:14 PM", "Last sync Oct 30, 2026"): only the plain words
/// are lower-cased, never a weekday, a month or AM/PM.
String relativeTimeInSentence(DateTime? at, {DateTime? now}) {
  final text = relativeTime(at, now: now);
  for (final word in const ['Just now', 'Never', 'Yesterday']) {
    if (text.startsWith(word)) {
      return word.toLowerCase() + text.substring(word.length);
    }
  }
  return text;
}
