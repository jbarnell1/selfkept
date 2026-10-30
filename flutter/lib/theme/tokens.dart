import 'package:flutter/material.dart';

import '../brand/brand.dart';

/// Corner radii. Cards are soft, controls a little tighter.
abstract final class Radii {
  static const double card = 20;
  static const double control = 12;
  static const BorderRadius cardRadius = BorderRadius.all(
    Radius.circular(card),
  );
  static const BorderRadius controlRadius = BorderRadius.all(
    Radius.circular(control),
  );
}

/// Spacing scale. Generous whitespace is part of the feel.
abstract final class Space {
  static const double x1 = 4;
  static const double x2 = 8;
  static const double x3 = 12;
  static const double x4 = 16;
  static const double x6 = 24;
  static const double x8 = 32;

  /// Horizontal page gutter.
  static const double gutter = x6;

  /// Content never grows wider than this on a laptop.
  static const double maxContentWidth = 720;
}

/// Motion. Quick and calm: state changes settle in 150 to 250 ms on a strong
/// ease-out, nothing bouncy, and exits are shorter than entrances.
abstract final class Motion {
  static const Duration short = Duration(milliseconds: 150);
  static const Duration standard = Duration(milliseconds: 200);
  static const Duration long = Duration(milliseconds: 250);
  static const Curve curve = Cubic(0.23, 1, 0.32, 1);

  /// For outgoing children. A switcher plays the exit in reverse, so an
  /// ease-in curve reads as a fast start.
  static const Curve reverseCurve = Curves.easeInCubic;

  /// The duration for movement, or zero when the system asks for less motion
  /// so the content simply appears in place.
  static Duration of(BuildContext context, Duration duration) =>
      (MediaQuery.maybeDisableAnimationsOf(context) ?? false)
      ? Duration.zero
      : duration;
}

/// Semantic colours that sit beside the Material scheme. Health states never
/// use red: amber is "watch", terracotta is the clinician tier.
@immutable
class AppColors extends ThemeExtension<AppColors> {
  final Color attention;
  final Color clinician;
  final Color band;
  final Color hairline;
  final Color surfaceRaised;
  final Color textSoft;

  const AppColors({
    required this.attention,
    required this.clinician,
    required this.band,
    required this.hairline,
    required this.surfaceRaised,
    required this.textSoft,
  });

  static final light = AppColors(
    attention: Brand.amber,
    clinician: Brand.terracotta,
    band: Brand.sage,
    hairline: Brand.ink.withValues(alpha: 0.12),
    surfaceRaised: Brand.cream2,
    textSoft: Brand.inkSoft,
  );

  static final dark = AppColors(
    attention: Brand.amber,
    clinician: Brand.terracotta,
    band: Brand.sage,
    hairline: Brand.linen.withValues(alpha: 0.14),
    surfaceRaised: Brand.charcoal2,
    textSoft: Brand.linen.withValues(alpha: 0.7),
  );

  static AppColors of(BuildContext context) =>
      Theme.of(context).extension<AppColors>() ?? light;

  @override
  AppColors copyWith({
    Color? attention,
    Color? clinician,
    Color? band,
    Color? hairline,
    Color? surfaceRaised,
    Color? textSoft,
  }) => AppColors(
    attention: attention ?? this.attention,
    clinician: clinician ?? this.clinician,
    band: band ?? this.band,
    hairline: hairline ?? this.hairline,
    surfaceRaised: surfaceRaised ?? this.surfaceRaised,
    textSoft: textSoft ?? this.textSoft,
  );

  @override
  AppColors lerp(AppColors? other, double t) {
    if (other == null) return this;
    return AppColors(
      attention: Color.lerp(attention, other.attention, t)!,
      clinician: Color.lerp(clinician, other.clinician, t)!,
      band: Color.lerp(band, other.band, t)!,
      hairline: Color.lerp(hairline, other.hairline, t)!,
      surfaceRaised: Color.lerp(surfaceRaised, other.surfaceRaised, t)!,
      textSoft: Color.lerp(textSoft, other.textSoft, t)!,
    );
  }
}
