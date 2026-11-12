import '../utils/json_reader.dart';
import 'journal.dart';
import 'lab.dart';
import 'medication.dart';

// Photos read by the home model (docs/photo-design.md). The server reads a
// photo once and forgets it; what comes back is a proposal the person checks
// before anything is saved.

/// What a photo shows.
enum PhotoKind {
  meal('meal'),
  nutritionLabel('nutrition_label'),
  medicineLabel('medicine_label'),
  deviceDisplay('device_display'),
  labReport('lab_report'),
  other('other');

  final String wireValue;

  const PhotoKind(this.wireValue);

  String toJson() => wireValue;

  /// Anything unknown reads as [other], as the server treats it.
  static PhotoKind fromString(String? value) {
    final lower = value?.trim().toLowerCase();
    for (final kind in values) {
      if (kind.wireValue == lower) return kind;
    }
    return other;
  }

  String get displayName => switch (this) {
    meal => 'A meal',
    nutritionLabel => 'A nutrition label',
    medicineLabel => 'A medicine or supplement label',
    deviceDisplay => 'A scale, cuff or meter',
    labReport => 'A lab report',
    other => 'Something else',
  };
}

/// What is on the plate, with the model's guess at its numbers for the
/// whole meal. The numbers can be well off.
class PhotoMeal {
  final String text;
  final List<String> tags;
  final JournalPortion? portion;
  final double? energyKcal;
  final double? proteinG;
  final double? carbsG;
  final double? fatG;

  const PhotoMeal({
    this.text = '',
    this.tags = const [],
    this.portion,
    this.energyKcal,
    this.proteinG,
    this.carbsG,
    this.fatG,
  });

  factory PhotoMeal.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return PhotoMeal(
      text: r.string('text'),
      tags: r.strings('tags'),
      portion: JournalPortion.fromString(r.stringOrNull('portion')),
      energyKcal: readDouble(r.raw('energyKcal')),
      proteinG: readDouble(r.raw('proteinG')),
      carbsG: readDouble(r.raw('carbsG')),
      fatG: readDouble(r.raw('fatG')),
    );
  }
}

/// A nutrition label as printed: the numbers are per serving.
class PhotoLabel {
  final String? product;
  final String? servingSize;
  final double? servingsPerContainer;
  final double? energyKcal;
  final double? proteinG;
  final double? carbsG;
  final double? fatG;

  const PhotoLabel({
    this.product,
    this.servingSize,
    this.servingsPerContainer,
    this.energyKcal,
    this.proteinG,
    this.carbsG,
    this.fatG,
  });

  factory PhotoLabel.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return PhotoLabel(
      product: r.stringOrNull('product'),
      servingSize: r.stringOrNull('servingSize'),
      servingsPerContainer: readDouble(r.raw('servingsPerContainer')),
      energyKcal: readDouble(r.raw('energyKcal')),
      proteinG: readDouble(r.raw('proteinG')),
      carbsG: readDouble(r.raw('carbsG')),
      fatG: readDouble(r.raw('fatG')),
    );
  }
}

/// A medicine or supplement label, ready to fill the medicines form.
class PhotoMedicine {
  final MedicationKind kind;
  final String? brand;
  final String product;
  final String? dose;
  final List<MedicationIngredient> ingredients;

  const PhotoMedicine({
    this.kind = MedicationKind.supplement,
    this.brand,
    required this.product,
    this.dose,
    this.ingredients = const [],
  });

  factory PhotoMedicine.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return PhotoMedicine(
      kind: MedicationKind.fromString(r.stringOrNull('kind')),
      brand: r.stringOrNull('brand'),
      product: r.string('product'),
      dose: r.stringOrNull('dose'),
      ingredients: [
        for (final i in r.objects('ingredients'))
          MedicationIngredient.fromJson(i),
      ],
    );
  }

  /// The medicines form's starting values. Nothing is saved until the
  /// person saves the form.
  MedicationDraft toDraft() => MedicationDraft(
    kind: kind,
    brand: brand,
    product: product,
    dose: dose,
    ingredients: ingredients,
  );
}

/// One number on a display, already in its metric's unit.
class PhotoReading {
  final String metricKey;
  final double value;
  final String unit;
  final String label;

  const PhotoReading({
    required this.metricKey,
    required this.value,
    this.unit = '',
    this.label = '',
  });

  /// Null when the metric or the value is missing.
  static PhotoReading? tryFromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final key = r.string('metricKey');
    final value = readDouble(r.raw('value'));
    if (key.isEmpty || value == null) return null;
    return PhotoReading(
      metricKey: key,
      value: value,
      unit: r.string('unit'),
      label: r.string('label'),
    );
  }
}

/// 200 from POST /assistant/photos. At most one of the parts is present,
/// matching [kind]; none for [PhotoKind.other].
class PhotoAnalysis {
  final PhotoKind kind;
  final String description;
  final PhotoMeal? meal;
  final PhotoLabel? label;
  final PhotoMedicine? medicine;
  final List<PhotoReading> readings;
  final ParseResult? lab;

  const PhotoAnalysis({
    required this.kind,
    this.description = '',
    this.meal,
    this.label,
    this.medicine,
    this.readings = const [],
    this.lab,
  });

  factory PhotoAnalysis.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return PhotoAnalysis(
      kind: PhotoKind.fromString(r.stringOrNull('kind')),
      description: r.string('description'),
      meal: switch (r.object('meal')) {
        final m? => PhotoMeal.fromJson(m),
        null => null,
      },
      label: switch (r.object('label')) {
        final l? => PhotoLabel.fromJson(l),
        null => null,
      },
      medicine: switch (r.object('medicine')) {
        final m? => PhotoMedicine.fromJson(m),
        null => null,
      },
      readings: [
        for (final o in r.objects('readings')) ?PhotoReading.tryFromJson(o),
      ],
      lab: switch (r.object('lab')) {
        final l? => ParseResult.fromJson(l),
        null => null,
      },
    );
  }

  /// The kind the card shows. A kind whose part is missing is shown as
  /// [PhotoKind.other] with the description, as the server would answer it.
  PhotoKind get shownKind => switch (kind) {
    PhotoKind.meal when meal == null => PhotoKind.other,
    PhotoKind.nutritionLabel when label == null => PhotoKind.other,
    PhotoKind.medicineLabel when medicine == null => PhotoKind.other,
    PhotoKind.deviceDisplay when readings.isEmpty => PhotoKind.other,
    PhotoKind.labReport when lab == null => PhotoKind.other,
    _ => kind,
  };
}

/// The `photo` part of a chat message: what an `other` photo shows, sent
/// with the person's question so the assistant can answer.
class PhotoNote {
  final PhotoKind kind;
  final String description;

  const PhotoNote({required this.kind, required this.description});

  /// The server's limit on the description.
  static const int maxDescription = 300;

  Map<String, dynamic> toJson() => {
    'kind': kind.wireValue,
    'description': description.length > maxDescription
        ? description.substring(0, maxDescription)
        : description,
  };
}
