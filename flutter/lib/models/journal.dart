import '../utils/json_reader.dart';

/// What a journal entry is about.
enum JournalKind {
  food,
  drink,
  symptom,
  training,
  sleep,
  note;

  String get wireValue => name;
  String toJson() => wireValue;

  static JournalKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final kind in values) {
      if (kind.name == lower) return kind;
    }
    return note;
  }

  String get displayName => switch (this) {
    food => 'Food',
    drink => 'Drink',
    symptom => 'Symptom',
    training => 'Training',
    sleep => 'Sleep',
    note => 'Note',
  };

  /// Only food and drink carry a portion on the server; the quick entry
  /// offers the choice for food.
  bool get takesPortion => this == food || this == drink;
}

/// A rough size for food or drink.
enum JournalPortion {
  small,
  usual,
  large;

  String get wireValue => name;
  String toJson() => wireValue;

  /// Null for anything unknown: an entry simply has no portion.
  static JournalPortion? fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final p in values) {
      if (p.name == lower) return p;
    }
    return null;
  }

  String get displayName => switch (this) {
    small => 'Small',
    usual => 'Usual',
    large => 'Large',
  };
}

/// A tag the server offers as a chip.
class JournalTag {
  final String tag;
  final String label;

  const JournalTag({required this.tag, required this.label});

  factory JournalTag.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final tag = r.string('tag');
    return JournalTag(
      tag: tag,
      label: r.string('label', fallback: tag),
    );
  }

  /// "late_caffeine" reads as "Late caffeine" when the server sent no label.
  static String labelFor(String tag) {
    if (tag.isEmpty) return tag;
    final spaced = tag.replaceAll('_', ' ');
    return spaced[0].toUpperCase() + spaced.substring(1);
  }
}

/// The tags of one kind.
class JournalTagGroup {
  final JournalKind kind;
  final List<JournalTag> tags;

  const JournalTagGroup({required this.kind, this.tags = const []});

  factory JournalTagGroup.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return JournalTagGroup(
      kind: JournalKind.fromString(r.stringOrNull('kind')),
      tags: [for (final t in r.objects('tags')) JournalTag.fromJson(t)],
    );
  }

  static List<JournalTagGroup> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) JournalTagGroup.fromJson(item),
  ];
}

/// Where the numbers on a food or drink entry came from.
enum NutritionBasis {
  /// The model's guess from a picture of a meal. It can be well off.
  estimate,

  /// Printed on a nutrition label, times the servings eaten.
  label;

  String get wireValue => name;
  String toJson() => wireValue;

  /// Null for anything unknown: the entry then has no numbers.
  static NutritionBasis? fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final b in values) {
      if (b.name == lower) return b;
    }
    return null;
  }

  String get displayName => switch (this) {
    estimate => 'guessed from a photo',
    label => 'from the label',
  };
}

/// The numbers on a food or drink entry, with where they came from. Only a
/// confirmed photo carries them.
class JournalNutrition {
  final NutritionBasis basis;
  final double? energyKcal;
  final double? proteinG;
  final double? carbsG;
  final double? fatG;

  const JournalNutrition({
    required this.basis,
    this.energyKcal,
    this.proteinG,
    this.carbsG,
    this.fatG,
  });

  /// The server refuses a basis without any number.
  bool get hasAny =>
      energyKcal != null || proteinG != null || carbsG != null || fatG != null;

  /// Null when there is no known basis or no number.
  static JournalNutrition? tryFromJson(Map<String, dynamic>? json) {
    if (json == null) return null;
    final r = JsonReader(json);
    final basis = NutritionBasis.fromString(r.stringOrNull('basis'));
    if (basis == null) return null;
    final n = JournalNutrition(
      basis: basis,
      energyKcal: readDouble(r.raw('energyKcal')),
      proteinG: readDouble(r.raw('proteinG')),
      carbsG: readDouble(r.raw('carbsG')),
      fatG: readDouble(r.raw('fatG')),
    );
    return n.hasAny ? n : null;
  }

  Map<String, dynamic> toJson() => {
    'basis': basis.wireValue,
    'energyKcal': ?energyKcal,
    'proteinG': ?proteinG,
    'carbsG': ?carbsG,
    'fatG': ?fatG,
  };

  /// "About 620 kcal, 35 g protein (guessed from a photo)" or "230 kcal,
  /// 3 g protein (from the label)".
  String describe() {
    final parts = <String>[
      if (energyKcal != null) '${energyKcal!.round()} kcal',
      if (proteinG != null) '${_grams(proteinG!)} g protein',
      if (carbsG != null) '${_grams(carbsG!)} g carbohydrate',
      if (fatG != null) '${_grams(fatG!)} g fat',
    ];
    final numbers = parts.join(', ');
    final lead = basis == NutritionBasis.estimate ? 'About $numbers' : numbers;
    return '$lead (${basis.displayName})';
  }

  static String _grams(double value) {
    final text = value.toStringAsFixed(1);
    return text.endsWith('.0') ? text.substring(0, text.length - 2) : text;
  }
}

/// One line of the journal.
class JournalEntry {
  final String id;
  final DateTime? at;
  final DateTime? day;
  final JournalKind kind;
  final String? text;
  final List<String> tags;
  final JournalPortion? portion;
  final String source;

  /// The numbers from a confirmed photo; null for every other entry.
  final JournalNutrition? nutrition;

  const JournalEntry({
    required this.id,
    this.at,
    this.day,
    required this.kind,
    this.text,
    this.tags = const [],
    this.portion,
    this.source = '',
    this.nutrition,
  });

  factory JournalEntry.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return JournalEntry(
      id: r.string('id'),
      at: r.dateTime('at'),
      day: r.dateTime('day'),
      kind: JournalKind.fromString(r.stringOrNull('kind')),
      text: r.stringOrNull('text'),
      tags: r.strings('tags'),
      portion: JournalPortion.fromString(r.stringOrNull('portion')),
      source: r.string('source'),
      nutrition: JournalNutrition.tryFromJson(r.object('nutrition')),
    );
  }

  static List<JournalEntry> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) JournalEntry.fromJson(item),
  ];
}

/// Body of POST /journal.
class JournalDraft {
  final JournalKind kind;
  final String? text;
  final List<String> tags;
  final JournalPortion? portion;

  /// Numbers from a confirmed photo. With them the entry's source is photo.
  final JournalNutrition? nutrition;

  const JournalDraft({
    required this.kind,
    this.text,
    this.tags = const [],
    this.portion,
    this.nutrition,
  });

  /// The server refuses an entry with neither words nor a tag.
  bool get isEmpty => (text == null || text!.trim().isEmpty) && tags.isEmpty;

  Map<String, dynamic> toJson() => {
    'kind': kind.wireValue,
    'text': text == null || text!.trim().isEmpty ? null : text!.trim(),
    'tags': tags,
    'portion': portion?.wireValue,
    if (nutrition != null && nutrition!.hasAny)
      'nutrition': nutrition!.toJson(),
  };
}
