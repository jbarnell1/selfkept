import '../utils/json_reader.dart';
import 'enums.dart';

/// What goes into a privacy bundle. Everything that could identify the person
/// is off unless they turn it on.
class BundleOptions {
  final Set<ShareCategory> categories;
  final int days;
  final bool keepDates;
  final bool includeNotes;
  final bool includeName;

  const BundleOptions({
    required this.categories,
    this.days = 90,
    this.keepDates = false,
    this.includeNotes = false,
    this.includeName = false,
  });

  /// The categories a bundle can carry, in the order they are offered.
  static const List<ShareCategory> offered = [
    ShareCategory.sleep,
    ShareCategory.activity,
    ShareCategory.vitals,
    ShareCategory.body,
    ShareCategory.checkins,
    ShareCategory.labs,
    ShareCategory.insights,
  ];

  static final BundleOptions defaults = BundleOptions(
    categories: offered.toSet(),
  );

  BundleOptions copyWith({
    Set<ShareCategory>? categories,
    int? days,
    bool? keepDates,
    bool? includeNotes,
    bool? includeName,
  }) => BundleOptions(
    categories: categories ?? this.categories,
    days: days ?? this.days,
    keepDates: keepDates ?? this.keepDates,
    includeNotes: includeNotes ?? this.includeNotes,
    includeName: includeName ?? this.includeName,
  );

  /// The request body. [export] is false for a preview; true when the person
  /// copies or saves it, which the server records in the audit log.
  Map<String, dynamic> toJson({required bool export}) => {
    'categories': [
      for (final c in offered)
        if (categories.contains(c)) c.toJson(),
    ],
    'days': days,
    'keepDates': keepDates,
    'includeNotes': includeNotes,
    'includeName': includeName,
    'export': export,
  };
}

class PrivacyBundle {
  final String text;
  final int characters;
  final int approximateTokens;
  final List<String> included;

  const PrivacyBundle({
    required this.text,
    required this.characters,
    required this.approximateTokens,
    required this.included,
  });

  factory PrivacyBundle.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final text = r.string('text');
    return PrivacyBundle(
      text: text,
      characters: r.integer('characters', fallback: text.length),
      approximateTokens: r.integer('approximateTokens'),
      included: r.strings('included'),
    );
  }
}
