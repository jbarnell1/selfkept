import '../utils/json_reader.dart';
import 'enums.dart';

/// Categories I have granted to one household member.
class ShareGrant {
  final String memberPersonId;
  final String displayName;
  final Set<ShareCategory> categories;
  final DateTime? updatedAt;

  const ShareGrant({
    required this.memberPersonId,
    this.displayName = '',
    this.categories = const {},
    this.updatedAt,
  });

  factory ShareGrant.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return ShareGrant(
      memberPersonId: r.has('memberPersonId')
          ? r.string('memberPersonId')
          : r.string('personId'),
      displayName: r.string('displayName'),
      categories: parseCategories(r.strings('categories')),
      updatedAt: r.dateTime('updatedAt'),
    );
  }

  /// Body of PUT /household/shares/{memberPersonId}.
  static Map<String, dynamic> toPutJson(Set<ShareCategory> categories) => {
    'categories': [
      for (final c in ShareCategory.values)
        if (categories.contains(c)) c.wireValue,
    ],
  };

  static Set<ShareCategory> parseCategories(List<String> values) => {
    for (final v in values) ?ShareCategory.fromString(v),
  };
}

/// Categories another member has granted to me.
class SharedWithMe {
  final String personId;
  final String displayName;
  final Set<ShareCategory> categories;

  const SharedWithMe({
    required this.personId,
    required this.displayName,
    this.categories = const {},
  });

  factory SharedWithMe.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SharedWithMe(
      personId: r.string('personId'),
      displayName: r.string('displayName'),
      categories: ShareGrant.parseCategories(r.strings('categories')),
    );
  }
}
