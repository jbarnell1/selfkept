import 'package:flutter/cupertino.dart' show CupertinoPageTransitionsBuilder;
import 'package:flutter/material.dart';

import '../brand/brand.dart';
import 'tokens.dart';

/// Light: forest on cream. Dark: charcoal with moss fills, a true dark rather
/// than an inversion; text-sized accents there (links, text buttons, the
/// selected tab, focus) use sage, because moss on charcoal is too dark to read. Fraunces for display and headings, Inter for body and UI,
/// JetBrains Mono with tabular figures for numbers.
///
/// The typefaces are bundled assets (tool/fetch-fonts.sh), never fetched at
/// runtime: the web app loads nothing cross-origin. Until the files are
/// present the platform font stands in.
abstract final class AppTheme {
  static ThemeData get light => _build(
    brightness: Brightness.light,
    scheme: _lightScheme,
    colors: AppColors.light,
  );

  static ThemeData get dark => _build(
    brightness: Brightness.dark,
    scheme: _darkScheme,
    colors: AppColors.dark,
  );

  static final ColorScheme _lightScheme = ColorScheme(
    brightness: Brightness.light,
    primary: Brand.forest,
    onPrimary: Brand.cream,
    primaryContainer: Brand.sage.withValues(alpha: 0.35),
    onPrimaryContainer: Brand.forest,
    secondary: Brand.moss,
    onSecondary: Brand.cream,
    secondaryContainer: Brand.cream2,
    onSecondaryContainer: Brand.ink,
    tertiary: Brand.amber,
    onTertiary: Brand.ink,
    tertiaryContainer: Brand.amber.withValues(alpha: 0.18),
    onTertiaryContainer: Brand.ink,
    error: _darkenedAmber,
    onError: Brand.cream,
    errorContainer: Brand.amber.withValues(alpha: 0.18),
    onErrorContainer: Brand.ink,
    surface: Brand.cream,
    onSurface: Brand.ink,
    surfaceDim: Brand.cream2,
    surfaceBright: Brand.cream,
    surfaceContainerLowest: Brand.cream,
    surfaceContainerLow: Brand.cream,
    surfaceContainer: Brand.cream2,
    surfaceContainerHigh: Brand.cream2,
    surfaceContainerHighest: Brand.cream2,
    onSurfaceVariant: Brand.inkSoft,
    outline: Brand.inkSoft.withValues(alpha: 0.5),
    outlineVariant: Brand.ink.withValues(alpha: 0.12),
    shadow: Colors.transparent,
    scrim: Brand.ink.withValues(alpha: 0.4),
    inverseSurface: Brand.ink,
    onInverseSurface: Brand.cream,
    inversePrimary: Brand.sage,
    surfaceTint: Colors.transparent,
  );

  static final ColorScheme _darkScheme = ColorScheme(
    brightness: Brightness.dark,
    primary: Brand.sage,
    onPrimary: Brand.charcoal,
    primaryContainer: Brand.moss.withValues(alpha: 0.4),
    onPrimaryContainer: Brand.linen,
    secondary: Brand.sage,
    onSecondary: Brand.charcoal,
    secondaryContainer: Brand.charcoal2,
    onSecondaryContainer: Brand.linen,
    tertiary: Brand.amber,
    onTertiary: Brand.charcoal,
    tertiaryContainer: Brand.amber.withValues(alpha: 0.22),
    onTertiaryContainer: Brand.linen,
    error: Brand.amber,
    onError: Brand.charcoal,
    errorContainer: Brand.amber.withValues(alpha: 0.22),
    onErrorContainer: Brand.linen,
    surface: Brand.charcoal,
    onSurface: Brand.linen,
    surfaceDim: Brand.charcoal,
    surfaceBright: Brand.charcoal2,
    surfaceContainerLowest: Brand.charcoal,
    surfaceContainerLow: Brand.charcoal,
    surfaceContainer: Brand.charcoal2,
    surfaceContainerHigh: Brand.charcoal2,
    surfaceContainerHighest: Brand.charcoal2,
    onSurfaceVariant: Brand.linen.withValues(alpha: 0.7),
    outline: Brand.linen.withValues(alpha: 0.4),
    outlineVariant: Brand.linen.withValues(alpha: 0.14),
    shadow: Colors.transparent,
    scrim: Colors.black.withValues(alpha: 0.5),
    inverseSurface: Brand.linen,
    onInverseSurface: Brand.charcoal,
    inversePrimary: Brand.forest,
    surfaceTint: Colors.transparent,
  );

  /// Amber pulled towards ink so it reads as text on cream and on the field
  /// fill (4.5:1 on both).
  static final Color _darkenedAmber = Color.lerp(Brand.amber, Brand.ink, 0.45)!;

  /// Numbers and data: mono, tabular figures.
  static TextStyle numeric(BuildContext context, {TextStyle? base}) {
    final style = base ?? Theme.of(context).textTheme.bodyLarge!;
    return style.copyWith(
      fontFamily: Brand.monoFamily,
      fontFeatures: const [FontFeature.tabularFigures()],
      fontVariations: _weightAxis(style.fontWeight ?? FontWeight.w400),
    );
  }

  /// Variable fonts take their weight from the wght axis; fontWeight alone
  /// only selects among static files. Set both so either kind renders right.
  static List<FontVariation> _weightAxis(FontWeight weight) => [
    FontVariation.weight(weight.value.toDouble()),
  ];

  static ThemeData _build({
    required Brightness brightness,
    required ColorScheme scheme,
    required AppColors colors,
  }) {
    final base = ThemeData(
      brightness: brightness,
      colorScheme: scheme,
      useMaterial3: true,
      // Every default text style uses the bundled UI face, so nothing falls back to Roboto
      // (which the web engine would fetch from a font CDN).
      fontFamily: Brand.uiFamily,
    );
    final textTheme = _textTheme(
      base.textTheme,
      scheme.onSurface,
      scheme.onSurfaceVariant,
    );

    final hairline = BorderSide(color: colors.hairline, width: 1);
    // Filled buttons keep the brand's deep green in both themes.
    final dark = brightness == Brightness.dark;
    final fill = dark ? Brand.moss : scheme.primary;
    final onFill = dark ? Brand.linen : scheme.onPrimary;

    return base.copyWith(
      scaffoldBackgroundColor: scheme.surface,
      canvasColor: scheme.surface,
      textTheme: textTheme,
      primaryTextTheme: textTheme,
      // A plain ripple: calmer than the sparkle, and it needs no shader asset.
      splashFactory: InkRipple.splashFactory,
      visualDensity: VisualDensity.standard,
      // iOS and macOS keep the native slide so a swipe from the left edge goes
      // back; Android's predictive back works with the fade.
      pageTransitionsTheme: const PageTransitionsTheme(
        builders: {
          TargetPlatform.android: FadeForwardsPageTransitionsBuilder(),
          TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
          TargetPlatform.macOS: CupertinoPageTransitionsBuilder(),
          TargetPlatform.windows: FadeForwardsPageTransitionsBuilder(),
          TargetPlatform.linux: FadeForwardsPageTransitionsBuilder(),
          TargetPlatform.fuchsia: FadeForwardsPageTransitionsBuilder(),
        },
      ),
      appBarTheme: AppBarTheme(
        backgroundColor: scheme.surface,
        foregroundColor: scheme.onSurface,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        titleTextStyle: textTheme.headlineSmall,
      ),
      cardTheme: CardThemeData(
        color: scheme.surface,
        elevation: 0,
        shadowColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(
          borderRadius: Radii.cardRadius,
          side: hairline,
        ),
        clipBehavior: Clip.antiAlias,
      ),
      dividerTheme: DividerThemeData(
        color: colors.hairline,
        thickness: 1,
        space: 1,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: colors.surfaceRaised,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: Space.x4,
          vertical: Space.x4,
        ),
        border: OutlineInputBorder(
          borderRadius: Radii.controlRadius,
          borderSide: hairline,
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: Radii.controlRadius,
          borderSide: hairline,
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: Radii.controlRadius,
          borderSide: BorderSide(color: scheme.primary, width: 1.5),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: Radii.controlRadius,
          borderSide: BorderSide(color: scheme.error, width: 1),
        ),
        focusedErrorBorder: OutlineInputBorder(
          borderRadius: Radii.controlRadius,
          borderSide: BorderSide(color: scheme.error, width: 1.5),
        ),
        labelStyle: textTheme.bodyMedium?.copyWith(
          color: scheme.onSurfaceVariant,
        ),
        hintStyle: textTheme.bodyMedium?.copyWith(
          color: scheme.onSurfaceVariant,
        ),
        helperStyle: textTheme.bodySmall?.copyWith(
          color: scheme.onSurfaceVariant,
        ),
        errorStyle: textTheme.bodySmall?.copyWith(color: scheme.error),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: fill,
          foregroundColor: onFill,
          elevation: 0,
          minimumSize: const Size(64, 52),
          padding: const EdgeInsets.symmetric(horizontal: Space.x6),
          shape: const StadiumBorder(),
          textStyle: textTheme.labelLarge,
          animationDuration: Motion.short,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: scheme.primary,
          minimumSize: const Size(64, 52),
          padding: const EdgeInsets.symmetric(horizontal: Space.x6),
          side: BorderSide(color: scheme.primary.withValues(alpha: 0.5)),
          shape: const StadiumBorder(),
          textStyle: textTheme.labelLarge,
          animationDuration: Motion.short,
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: scheme.primary,
          minimumSize: const Size(48, 44),
          shape: const StadiumBorder(),
          textStyle: textTheme.labelLarge,
          animationDuration: Motion.short,
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          foregroundColor: scheme.onSurfaceVariant,
          shape: const RoundedRectangleBorder(
            borderRadius: Radii.controlRadius,
          ),
        ),
      ),
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) return scheme.onPrimary;
          return scheme.onSurfaceVariant;
        }),
        trackColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) return scheme.primary;
          return colors.surfaceRaised;
        }),
        trackOutlineColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) return Colors.transparent;
          return colors.hairline;
        }),
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: scheme.surface,
        elevation: 0,
        shadowColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        indicatorColor: scheme.primaryContainer,
        indicatorShape: const StadiumBorder(),
        height: 72,
        labelTextStyle: WidgetStateProperty.resolveWith((states) {
          final selected = states.contains(WidgetState.selected);
          return textTheme.labelMedium!.copyWith(
            color: selected ? scheme.onSurface : scheme.onSurfaceVariant,
            fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
          );
        }),
        iconTheme: WidgetStateProperty.resolveWith((states) {
          final selected = states.contains(WidgetState.selected);
          return IconThemeData(
            color: selected ? scheme.primary : scheme.onSurfaceVariant,
            size: 24,
          );
        }),
      ),
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: scheme.surface,
        elevation: 0,
        indicatorColor: scheme.primaryContainer,
        indicatorShape: const StadiumBorder(),
        selectedIconTheme: IconThemeData(color: scheme.primary),
        unselectedIconTheme: IconThemeData(color: scheme.onSurfaceVariant),
        selectedLabelTextStyle: textTheme.labelMedium!.copyWith(
          color: scheme.onSurface,
          fontWeight: FontWeight.w600,
        ),
        unselectedLabelTextStyle: textTheme.labelMedium!.copyWith(
          color: scheme.onSurfaceVariant,
        ),
        labelType: NavigationRailLabelType.all,
        groupAlignment: -0.9,
      ),
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        backgroundColor: scheme.inverseSurface,
        contentTextStyle: textTheme.bodyMedium?.copyWith(
          color: scheme.onInverseSurface,
        ),
        shape: const RoundedRectangleBorder(borderRadius: Radii.controlRadius),
        elevation: 0,
        insetPadding: const EdgeInsets.all(Space.x4),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: scheme.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: Radii.cardRadius,
          side: hairline,
        ),
        titleTextStyle: textTheme.headlineSmall,
        contentTextStyle: textTheme.bodyMedium,
      ),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: scheme.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        showDragHandle: true,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(Radii.card)),
        ),
      ),
      listTileTheme: ListTileThemeData(
        contentPadding: const EdgeInsets.symmetric(horizontal: Space.x4),
        titleTextStyle: textTheme.bodyLarge,
        subtitleTextStyle: textTheme.bodySmall?.copyWith(
          color: scheme.onSurfaceVariant,
        ),
        iconColor: scheme.onSurfaceVariant,
        shape: const RoundedRectangleBorder(borderRadius: Radii.controlRadius),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: colors.surfaceRaised,
        selectedColor: scheme.primaryContainer,
        side: BorderSide(color: colors.hairline),
        shape: const StadiumBorder(),
        labelStyle: textTheme.labelMedium,
        padding: const EdgeInsets.symmetric(
          horizontal: Space.x3,
          vertical: Space.x2,
        ),
      ),
      segmentedButtonTheme: SegmentedButtonThemeData(
        style: SegmentedButton.styleFrom(
          selectedBackgroundColor: scheme.primaryContainer,
          selectedForegroundColor: scheme.onSurface,
          foregroundColor: scheme.onSurfaceVariant,
          side: BorderSide(color: colors.hairline),
          shape: const StadiumBorder(),
          textStyle: textTheme.labelLarge,
        ),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: scheme.primary,
        linearTrackColor: colors.surfaceRaised,
        circularTrackColor: Colors.transparent,
      ),
      extensions: [colors],
    );
  }

  static TextTheme _textTheme(TextTheme base, Color onSurface, Color soft) {
    TextStyle serif(
      TextStyle? style,
      double size, {
      double height = 1.15,
      FontWeight weight = FontWeight.w500,
      double spacing = -0.2,
    }) => (style ?? const TextStyle()).copyWith(
      fontFamily: Brand.displayFamily,
      fontSize: size,
      height: height,
      fontWeight: weight,
      fontVariations: _weightAxis(weight),
      letterSpacing: spacing,
      color: onSurface,
    );

    TextStyle sans(
      TextStyle? style,
      double size, {
      double height = 1.45,
      FontWeight weight = FontWeight.w400,
      Color? color,
      double spacing = 0,
    }) => (style ?? const TextStyle()).copyWith(
      fontFamily: Brand.uiFamily,
      fontSize: size,
      height: height,
      fontWeight: weight,
      fontVariations: _weightAxis(weight),
      letterSpacing: spacing,
      color: color ?? onSurface,
    );

    return TextTheme(
      displayLarge: serif(
        base.displayLarge,
        48,
        height: 1.05,
        weight: FontWeight.w400,
        spacing: -0.8,
      ),
      displayMedium: serif(
        base.displayMedium,
        40,
        height: 1.08,
        weight: FontWeight.w400,
        spacing: -0.6,
      ),
      displaySmall: serif(
        base.displaySmall,
        32,
        height: 1.1,
        weight: FontWeight.w400,
        spacing: -0.4,
      ),
      headlineLarge: serif(base.headlineLarge, 28),
      headlineMedium: serif(base.headlineMedium, 24),
      headlineSmall: serif(base.headlineSmall, 21),
      titleLarge: sans(
        base.titleLarge,
        18,
        height: 1.3,
        weight: FontWeight.w600,
        spacing: -0.1,
      ),
      titleMedium: sans(
        base.titleMedium,
        16,
        height: 1.35,
        weight: FontWeight.w600,
      ),
      titleSmall: sans(
        base.titleSmall,
        14,
        height: 1.35,
        weight: FontWeight.w600,
      ),
      bodyLarge: sans(base.bodyLarge, 16, height: 1.5),
      bodyMedium: sans(base.bodyMedium, 14, height: 1.5),
      bodySmall: sans(base.bodySmall, 13, height: 1.45, color: soft),
      labelLarge: sans(
        base.labelLarge,
        15,
        height: 1.2,
        weight: FontWeight.w600,
      ),
      labelMedium: sans(
        base.labelMedium,
        13,
        height: 1.2,
        weight: FontWeight.w500,
      ),
      labelSmall: sans(
        base.labelSmall,
        12,
        height: 1.25,
        weight: FontWeight.w500,
        color: soft,
      ),
    );
  }
}
