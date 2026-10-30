import '../utils/json_reader.dart';
import 'enums.dart';

/// The person behind an account: the profile that health data attaches to.
class Person {
  final String id;
  final String displayName;
  final SexAtBirth sexAtBirth;
  final DateTime? birthDate;
  final Units units;
  final String timeZone;

  const Person({
    required this.id,
    required this.displayName,
    this.sexAtBirth = SexAtBirth.unspecified,
    this.birthDate,
    this.units = Units.metric,
    this.timeZone = 'UTC',
  });

  factory Person.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return Person(
      id: r.string('id'),
      displayName: r.string('displayName'),
      sexAtBirth: SexAtBirth.fromString(r.stringOrNull('sexAtBirth')),
      birthDate: r.dateTime('birthDate'),
      units: Units.fromString(r.stringOrNull('units')),
      timeZone: r.string('timeZone', fallback: 'UTC'),
    );
  }

  /// Body of PUT /me/profile: the DTO without its id.
  Map<String, dynamic> toUpdateJson() => {
    'displayName': displayName,
    'sexAtBirth': sexAtBirth.wireValue,
    'birthDate': birthDate == null ? null : _dateOnly(birthDate!),
    'units': units.wireValue,
    'timeZone': timeZone,
  };

  Person copyWith({
    String? displayName,
    SexAtBirth? sexAtBirth,
    DateTime? birthDate,
    bool clearBirthDate = false,
    Units? units,
    String? timeZone,
  }) => Person(
    id: id,
    displayName: displayName ?? this.displayName,
    sexAtBirth: sexAtBirth ?? this.sexAtBirth,
    birthDate: clearBirthDate ? null : (birthDate ?? this.birthDate),
    units: units ?? this.units,
    timeZone: timeZone ?? this.timeZone,
  );

  static String _dateOnly(DateTime date) {
    final y = date.year.toString().padLeft(4, '0');
    final m = date.month.toString().padLeft(2, '0');
    final d = date.day.toString().padLeft(2, '0');
    return '$y-$m-$d';
  }
}
