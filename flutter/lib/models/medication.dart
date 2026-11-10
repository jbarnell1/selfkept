import '../utils/json_reader.dart';
import 'date_only.dart';

enum MedicationKind {
  medication,
  supplement;

  String get wireValue => name;
  String toJson() => wireValue;

  static MedicationKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final kind in values) {
      if (kind.name == lower) return kind;
    }
    return medication;
  }

  String get displayName => switch (this) {
    medication => 'Medicine',
    supplement => 'Supplement',
  };
}

enum MedicationSchedule {
  daily('daily', 'Daily'),
  weekly('weekly', 'Weekly'),
  asNeeded('as_needed', 'As needed');

  final String wireValue;
  final String displayName;

  const MedicationSchedule(this.wireValue, this.displayName);

  String toJson() => wireValue;

  static MedicationSchedule fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final s in values) {
      if (s.wireValue == lower) return s;
    }
    return daily;
  }
}

/// One thing inside a product: "Magnesium, 200, mg".
class MedicationIngredient {
  final String name;
  final double? amount;
  final String? unit;

  const MedicationIngredient({required this.name, this.amount, this.unit});

  factory MedicationIngredient.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return MedicationIngredient(
      name: r.string('name'),
      amount: readDouble(r.raw('amount')),
      unit: r.stringOrNull('unit'),
    );
  }

  Map<String, dynamic> toJson() => {
    'name': name,
    'amount': amount,
    'unit': unit,
  };
}

/// A medicine or supplement the person takes or took. A record, not advice.
class Medication {
  final String id;
  final MedicationKind kind;
  final String? brand;
  final String product;
  final String? dose;
  final String? scheduleNote;

  /// Null until the person says either way.
  final bool? generic;
  final MedicationSchedule schedule;
  final List<MedicationIngredient> ingredients;

  /// Worked out by the server from the product and its ingredients.
  final List<String> substances;
  final DateTime? startedOn;
  final DateTime? stoppedOn;
  final bool active;

  const Medication({
    required this.id,
    required this.kind,
    this.brand,
    required this.product,
    this.dose,
    this.scheduleNote,
    this.generic,
    this.schedule = MedicationSchedule.daily,
    this.ingredients = const [],
    this.substances = const [],
    this.startedOn,
    this.stoppedOn,
    this.active = true,
  });

  /// "Brand Product", or the product alone.
  String get fullName =>
      brand == null || brand!.isEmpty ? product : '$brand $product';

  factory Medication.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final generic = r.raw('generic');
    return Medication(
      id: r.string('id'),
      kind: MedicationKind.fromString(r.stringOrNull('kind')),
      brand: r.stringOrNull('brand'),
      product: r.string('product'),
      dose: r.stringOrNull('dose'),
      scheduleNote: r.stringOrNull('scheduleNote'),
      generic: generic is bool ? generic : null,
      schedule: MedicationSchedule.fromString(r.stringOrNull('schedule')),
      ingredients: [
        for (final i in r.objects('ingredients'))
          MedicationIngredient.fromJson(i),
      ],
      substances: r.strings('substances'),
      startedOn: r.dateTime('startedOn'),
      stoppedOn: r.dateTime('stoppedOn'),
      active: r.boolean('active', fallback: true),
    );
  }

  static List<Medication> listFromJson(dynamic body) => [
    for (final item in asJsonList(body)) Medication.fromJson(item),
  ];

  /// The server's substance tags with the names a person reads; the same
  /// names as SubstanceTags.DisplayName on the server.
  static const Map<String, String> _substanceNames = {
    'testosterone': 'Testosterone',
    'statin': 'Statin',
    'thyroid': 'Thyroid hormone',
    'biotin': 'Biotin',
    'iron': 'Iron',
    'vitamin_d': 'Vitamin D',
    'creatine': 'Creatine',
    'glp1': 'GLP-1',
    'beta_blocker': 'Beta blocker',
    'estrogen': 'Estrogen',
    'metformin': 'Metformin',
    'zinc': 'Zinc',
    'magnesium': 'Magnesium',
    'omega_3': 'Omega-3',
    'vitamin_b12': 'Vitamin B12',
    'caffeine': 'Caffeine',
    'melatonin': 'Melatonin',
  };

  /// "vitamin_d" reads as "Vitamin D", "omega_3" as "Omega-3". A tag the
  /// client does not know reads as its words with a capital.
  static String substanceLabel(String tag) {
    final known = _substanceNames[tag];
    if (known != null) return known;
    if (tag.isEmpty) return tag;
    final spaced = tag.replaceAll('_', ' ');
    return spaced[0].toUpperCase() + spaced.substring(1);
  }
}

/// Body of POST /medications and PUT /medications/{id}.
class MedicationDraft {
  final MedicationKind kind;
  final String? brand;
  final String product;
  final String? dose;
  final String? scheduleNote;
  final bool? generic;
  final MedicationSchedule schedule;
  final List<MedicationIngredient> ingredients;
  final DateTime? startedOn;
  final DateTime? stoppedOn;

  const MedicationDraft({
    required this.kind,
    this.brand,
    required this.product,
    this.dose,
    this.scheduleNote,
    this.generic,
    this.schedule = MedicationSchedule.daily,
    this.ingredients = const [],
    this.startedOn,
    this.stoppedOn,
  });

  Map<String, dynamic> toJson() => {
    'kind': kind.wireValue,
    'brand': brand,
    'product': product,
    'dose': dose,
    'scheduleNote': scheduleNote,
    'generic': generic,
    'schedule': schedule.wireValue,
    'ingredients': [for (final i in ingredients) i.toJson()],
    'startedOn': startedOn == null ? null : dateOnly(startedOn!),
    'stoppedOn': stoppedOn == null ? null : dateOnly(stoppedOn!),
  };
}
