import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:selfkept/brand/brand.dart';
import 'package:selfkept/theme/app_theme.dart';
import 'package:selfkept/theme/tokens.dart';

void main() {
  group('typography is bundled, never fetched', () {
    test('no runtime font package is declared or imported', () {
      // The web app must load nothing cross-origin, so fonts are assets.
      final pubspec = File('pubspec.yaml').readAsStringSync();
      expect(
        pubspec.contains('google_fonts'),
        isFalse,
        reason: 'google_fonts fetches from fonts.gstatic.com',
      );

      final dartFiles = Directory('lib')
          .listSync(recursive: true)
          .whereType<File>()
          .where((f) => f.path.endsWith('.dart'));
      for (final file in dartFiles) {
        expect(
          file.readAsStringSync().contains('package:google_fonts'),
          isFalse,
          reason: '${file.path} imports google_fonts',
        );
      }
    });

    test('text styles name the brand families', () {
      for (final theme in [AppTheme.light, AppTheme.dark]) {
        expect(theme.textTheme.displayLarge?.fontFamily, Brand.displayFamily);
        expect(theme.textTheme.headlineSmall?.fontFamily, Brand.displayFamily);
        expect(theme.textTheme.bodyMedium?.fontFamily, Brand.uiFamily);
        expect(theme.textTheme.labelLarge?.fontFamily, Brand.uiFamily);
      }
    });

    test('the pubspec fonts block names the same families', () {
      final pubspec = File('pubspec.yaml').readAsStringSync();
      for (final family in [
        Brand.displayFamily,
        Brand.uiFamily,
        Brand.monoFamily,
      ]) {
        expect(
          pubspec.contains('family: $family'),
          isTrue,
          reason: '$family missing from pubspec fonts',
        );
      }
    });
  });

  group('palette', () {
    test('light is forest on cream', () {
      final t = AppTheme.light;
      expect(t.colorScheme.primary, Brand.forest);
      expect(t.scaffoldBackgroundColor, Brand.cream);
      expect(t.colorScheme.onSurface, Brand.ink);
    });

    test('dark is moss on charcoal, not an inversion', () {
      final t = AppTheme.dark;
      expect(t.colorScheme.primary, Brand.moss);
      expect(t.scaffoldBackgroundColor, Brand.charcoal);
      expect(t.colorScheme.onSurface, Brand.linen);
      expect(t.colorScheme.surfaceContainer, Brand.charcoal2);
    });

    test('nothing is red: attention is amber, clinician is terracotta', () {
      for (final t in [AppTheme.light, AppTheme.dark]) {
        final colors = t.extension<AppColors>()!;
        expect(colors.attention, Brand.amber);
        expect(colors.clinician, Brand.terracotta);
        expect(t.colorScheme.error, isNot(Colors.red));
        expect(t.colorScheme.error, isNot(const Color(0xFFB3261E)));
      }
    });

    test('cards have a 20 px radius, a hairline and no shadow', () {
      for (final t in [AppTheme.light, AppTheme.dark]) {
        final shape = t.cardTheme.shape as RoundedRectangleBorder;
        expect(shape.borderRadius, Radii.cardRadius);
        expect(shape.side.width, 1);
        expect(t.cardTheme.elevation, 0);
      }
    });
  });

  testWidgets('numeric style is mono with tabular figures', (tester) async {
    late TextStyle style;
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: Builder(
          builder: (context) {
            style = AppTheme.numeric(context);
            return const SizedBox();
          },
        ),
      ),
    );
    expect(style.fontFamily, Brand.monoFamily);
    expect(style.fontFeatures, contains(const FontFeature.tabularFigures()));
  });
}
