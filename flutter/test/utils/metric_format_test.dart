import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/utils/metric_format.dart';

void main() {
  test('sleep minutes read as hours and minutes', () {
    expect(formatSleepMinutes(460), '7 h 40');
    expect(formatSleepMinutes(480), '8 h');
    expect(formatSleepMinutes(365), '6 h 05');
    expect(formatMetricValue('sleep_stage', 460.4, unit: 'min'), '7 h 40');
    expect(formatMetricWithUnit('sleep_stage', 460, unit: 'min'), '7 h 40');
  });

  test('counts and rates are whole, weight and percent keep a decimal', () {
    expect(formatMetricValue('steps', 8421, unit: 'count'), '8,421');
    expect(formatMetricWithUnit('steps', 8421, unit: 'count'), '8,421');
    expect(formatMetricWithUnit('resting_hr_bpm', 58.4, unit: 'bpm'), '58 bpm');
    expect(formatMetricWithUnit('weight_kg', 72.46, unit: 'kg'), '72.5 kg');
    expect(formatMetricWithUnit('spo2_pct', 96.25, unit: '%'), '96.3%');
    expect(
      formatMetricWithUnit('wrist_temp_delta_c', 0.125, unit: 'C'),
      '0.13 C',
    );
    expect(formatMetricValue('something', 3.14159), '3.1');
  });
}
