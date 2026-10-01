import 'package:flutter/material.dart';

/// AgriGuard's palette, the same tokens as the web console (web/src/index.css, docs/design/TOKENS.md).
/// Canopy green is the brand; earth brown marks soil, harvest and orders; surfaces are warm white.
abstract final class AgriColors {
  // Canopy — primary.
  static const brand50 = Color(0xFFF2F8F3);
  static const brand100 = Color(0xFFDCF1DE);
  static const brand200 = Color(0xFFBCE3C2);
  static const brand300 = Color(0xFF8FCCA0);
  static const brand400 = Color(0xFF58A97C);
  static const brand500 = Color(0xFF2F855A); // the seed, shared with the web
  static const brand600 = Color(0xFF276749);
  static const brand700 = Color(0xFF22543D);
  static const brand800 = Color(0xFF1A4230);
  static const brand900 = Color(0xFF133025);

  // Earth — secondary.
  static const earth50 = Color(0xFFFAF6F0);
  static const earth100 = Color(0xFFF0E7DA);
  static const earth200 = Color(0xFFE0CDB4);
  static const earth300 = Color(0xFFC9AC86);
  static const earth400 = Color(0xFFA9835A);
  static const earth500 = Color(0xFF8B6340);
  static const earth600 = Color(0xFF6F4E33);
  static const earth700 = Color(0xFF563D28);
  static const earth800 = Color(0xFF3E2C1D);

  // Surfaces.
  static const surfacePage = Color(0xFFFDFCF9);
  static const surfaceCard = Color(0xFFFFFFFF);
  static const surfaceSunken = Color(0xFFF6F4EE);
  static const surfaceInset = Color(0xFFEFECE3);
  static const borderSubtle = Color(0xFFE7E3D8);
  static const borderStrong = Color(0xFFD5CFC0);

  // Ink: the phone is read in sunlight, so secondary text is darker than on the web.
  static const ink = Color(0xFF1C1917);
  static const inkSoft = Color(0xFF44403C);
  static const inkMuted = Color(0xFF57534E);
  static const outline = Color(0xFF78716C); // 4.6:1 on the page: field and chip borders

  // Semantic.
  static const success = Color(0xFF276749);
  static const success50 = Color(0xFFEEF7F0);
  static const success200 = Color(0xFFBFE0C8);
  static const success800 = Color(0xFF1A4230);
  static const warning = Color(0xFFB45309);
  static const warning50 = Color(0xFFFEF7EC);
  static const warning200 = Color(0xFFF3D6A4);
  static const warning800 = Color(0xFF7C3A0A);
  static const danger = Color(0xFFB42318);
  static const danger50 = Color(0xFFFDF1F0);
  static const danger200 = Color(0xFFF2C4BF);
  static const danger800 = Color(0xFF7F1D14);
  static const info = Color(0xFF2A78D6);
  static const info50 = Color(0xFFEEF5FC);
  static const info200 = Color(0xFFC3DBF4);
  static const info800 = Color(0xFF1A4A82);
}

/// Shadows tinted with the deep canopy green, never neutral black (`--shadow-*` on the web):
/// black shadows on warm surfaces look muddy.
abstract final class AgriShadows {
  /// At rest: cards that hold a decision or a key figure.
  static const raised = [
    BoxShadow(color: Color(0x0F133025), offset: Offset(0, 1), blurRadius: 2),
    BoxShadow(color: Color(0x0A133025), offset: Offset(0, 1), blurRadius: 3),
  ];

  /// The bottom action bar and anything that floats over scrolling content.
  static const lifted = [
    BoxShadow(color: Color(0x1A133025), offset: Offset(0, 4), blurRadius: 12),
    BoxShadow(color: Color(0x0D133025), offset: Offset(0, 2), blurRadius: 4),
  ];
}

/// Semantic colours the ColorScheme has no slot for, reached with `Theme.of(context).extension<AgriTheme>()!`.
@immutable
class AgriTheme extends ThemeExtension<AgriTheme> {
  const AgriTheme({
    required this.success,
    required this.successContainer,
    required this.onSuccessContainer,
    required this.warning,
    required this.warningContainer,
    required this.onWarningContainer,
    required this.info,
    required this.infoContainer,
    required this.onInfoContainer,
    required this.sunken,
  });

  final Color success;
  final Color successContainer;
  final Color onSuccessContainer;
  final Color warning;
  final Color warningContainer;
  final Color onWarningContainer;
  final Color info;
  final Color infoContainer;
  final Color onInfoContainer;
  final Color sunken;

  static const light = AgriTheme(
    success: AgriColors.success,
    successContainer: AgriColors.success50,
    onSuccessContainer: AgriColors.success800,
    warning: AgriColors.warning,
    warningContainer: AgriColors.warning50,
    onWarningContainer: AgriColors.warning800,
    info: AgriColors.info,
    infoContainer: AgriColors.info50,
    onInfoContainer: AgriColors.info800,
    sunken: AgriColors.surfaceSunken,
  );

  @override
  AgriTheme copyWith() => this;

  @override
  AgriTheme lerp(ThemeExtension<AgriTheme>? other, double t) => this;
}

/// The serif used for screen titles and headline figures, as on the web. Android ships Noto Serif
/// as "serif", so no font file has to travel over a field connection.
const _display = 'serif';

/// Builds the app's Material 3 theme. Field conditions drive it: 16px minimum body text, 48dp
/// touch targets, and stronger borders and text than the web console for reading in sunlight.
ThemeData buildAgriTheme() {
  const scheme = ColorScheme(
    brightness: Brightness.light,
    primary: AgriColors.brand500,
    onPrimary: Colors.white,
    primaryContainer: AgriColors.brand100,
    onPrimaryContainer: AgriColors.brand900,
    secondary: AgriColors.earth500,
    onSecondary: Colors.white,
    secondaryContainer: AgriColors.earth100,
    onSecondaryContainer: AgriColors.earth800,
    // Cautions (manual review, a refused report): the warning family, as on the web.
    tertiary: AgriColors.warning,
    onTertiary: Colors.white,
    tertiaryContainer: AgriColors.warning50,
    onTertiaryContainer: AgriColors.warning800,
    error: AgriColors.danger,
    onError: Colors.white,
    errorContainer: AgriColors.danger50,
    onErrorContainer: AgriColors.danger800,
    surface: AgriColors.surfacePage,
    onSurface: AgriColors.ink,
    onSurfaceVariant: AgriColors.inkMuted,
    surfaceContainerLowest: AgriColors.surfaceCard,
    surfaceContainerLow: Color(0xFFFBFAF6),
    surfaceContainer: AgriColors.surfaceSunken,
    surfaceContainerHigh: AgriColors.surfaceInset,
    surfaceContainerHighest: AgriColors.borderSubtle,
    outline: AgriColors.outline,
    outlineVariant: AgriColors.borderStrong,
    inverseSurface: AgriColors.brand900,
    onInverseSurface: AgriColors.brand50,
    inversePrimary: AgriColors.brand300,
    shadow: AgriColors.brand900,
    scrim: AgriColors.brand900,
    surfaceTint: Colors.transparent,
  );

  final base = ThemeData(useMaterial3: true, colorScheme: scheme);
  // Merged onto Material's styles, not swapped for them, so each role keeps its font family
  // (Roboto, the phone's own) and baseline; only size, weight, height and colour change here.
  final baseText = base.textTheme.apply(bodyColor: AgriColors.ink, displayColor: AgriColors.ink).merge(
        const TextTheme(
          headlineMedium: TextStyle(fontFamily: _display, fontSize: 28, height: 1.2, fontWeight: FontWeight.w600, color: AgriColors.ink),
          headlineSmall: TextStyle(fontFamily: _display, fontSize: 24, height: 1.2, fontWeight: FontWeight.w600, color: AgriColors.ink),
          titleLarge: TextStyle(fontFamily: _display, fontSize: 22, height: 1.25, fontWeight: FontWeight.w600, color: AgriColors.ink),
          titleMedium: TextStyle(fontSize: 18, height: 1.3, fontWeight: FontWeight.w600, letterSpacing: 0.1, color: AgriColors.ink),
          titleSmall: TextStyle(fontSize: 16, height: 1.35, fontWeight: FontWeight.w600, letterSpacing: 0.1, color: AgriColors.ink),
          bodyLarge: TextStyle(fontSize: 17, height: 1.5, letterSpacing: 0.15, color: AgriColors.ink),
          bodyMedium: TextStyle(fontSize: 16, height: 1.5, letterSpacing: 0.15, color: AgriColors.ink),
          bodySmall: TextStyle(fontSize: 14, height: 1.45, letterSpacing: 0.2, color: AgriColors.inkMuted),
          labelLarge: TextStyle(fontSize: 16, height: 1.25, fontWeight: FontWeight.w600),
          labelMedium: TextStyle(fontSize: 14, height: 1.3, fontWeight: FontWeight.w500),
          labelSmall: TextStyle(fontSize: 12, height: 1.3, fontWeight: FontWeight.w500),
        ),
      );

  final text = _tabular(baseText);
  final rounded10 = RoundedRectangleBorder(borderRadius: BorderRadius.circular(10));
  OutlineInputBorder field(Color color, [double width = 1.25]) =>
      OutlineInputBorder(borderRadius: BorderRadius.circular(10), borderSide: BorderSide(color: color, width: width));

  return base.copyWith(
    scaffoldBackgroundColor: AgriColors.surfacePage,
    textTheme: text,
    // Full 48dp targets: gloved, sun-dazzled, one-handed use.
    visualDensity: VisualDensity.standard,
    materialTapTargetSize: MaterialTapTargetSize.padded,
    extensions: const [AgriTheme.light],
    appBarTheme: AppBarTheme(
      backgroundColor: AgriColors.surfacePage,
      foregroundColor: AgriColors.ink,
      elevation: 0,
      scrolledUnderElevation: 1,
      shadowColor: AgriColors.brand900.withValues(alpha: 0.2),
      surfaceTintColor: Colors.transparent,
      centerTitle: false,
      titleTextStyle: text.titleLarge,
    ),
    cardTheme: CardThemeData(
      color: AgriColors.surfaceCard,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: const BorderSide(color: AgriColors.borderSubtle),
      ),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: AgriColors.surfaceCard,
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      border: field(AgriColors.outline),
      enabledBorder: field(AgriColors.outline),
      focusedBorder: field(AgriColors.brand700, 2),
      errorBorder: field(AgriColors.danger),
      focusedErrorBorder: field(AgriColors.danger, 2),
      labelStyle: const TextStyle(fontSize: 16, color: AgriColors.inkMuted),
      floatingLabelStyle: const TextStyle(color: AgriColors.brand700, fontWeight: FontWeight.w600),
      hintStyle: const TextStyle(fontSize: 16, color: AgriColors.inkMuted),
      helperStyle: const TextStyle(fontSize: 14, color: AgriColors.inkMuted),
      errorStyle: const TextStyle(fontSize: 14, color: AgriColors.danger800, fontWeight: FontWeight.w500),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        // Two shades darker than the seed: white on it is 8.7:1, readable in full sun.
        backgroundColor: AgriColors.brand700,
        foregroundColor: Colors.white,
        disabledBackgroundColor: AgriColors.surfaceInset,
        disabledForegroundColor: AgriColors.inkMuted,
        minimumSize: const Size(64, 52),
        padding: const EdgeInsets.symmetric(horizontal: 20),
        textStyle: text.labelLarge,
        shape: rounded10,
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: AgriColors.brand800,
        minimumSize: const Size(64, 52),
        padding: const EdgeInsets.symmetric(horizontal: 18),
        side: const BorderSide(color: AgriColors.outline, width: 1.25),
        textStyle: text.labelLarge,
        shape: rounded10,
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: AgriColors.brand700,
        minimumSize: const Size(48, 48),
        textStyle: text.labelLarge,
      ),
    ),
    floatingActionButtonTheme: const FloatingActionButtonThemeData(
      backgroundColor: AgriColors.brand700,
      foregroundColor: Colors.white,
      extendedTextStyle: TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: AgriColors.surfaceCard,
      indicatorColor: AgriColors.brand100,
      surfaceTintColor: Colors.transparent,
      height: 68,
      labelTextStyle: WidgetStateProperty.resolveWith(
        (states) => TextStyle(
          fontSize: 13,
          fontWeight: states.contains(WidgetState.selected) ? FontWeight.w700 : FontWeight.w500,
          color: states.contains(WidgetState.selected) ? AgriColors.brand800 : AgriColors.inkMuted,
        ),
      ),
      iconTheme: WidgetStateProperty.resolveWith(
        (states) => IconThemeData(color: states.contains(WidgetState.selected) ? AgriColors.brand800 : AgriColors.inkMuted),
      ),
    ),
    chipTheme: base.chipTheme.copyWith(
      labelStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w500, color: AgriColors.ink),
      side: const BorderSide(color: AgriColors.outline),
      selectedColor: AgriColors.brand100,
      checkmarkColor: AgriColors.brand800,
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
    ),
    segmentedButtonTheme: SegmentedButtonThemeData(
      style: SegmentedButton.styleFrom(
        minimumSize: const Size(48, 48),
        selectedBackgroundColor: AgriColors.brand100,
        selectedForegroundColor: AgriColors.brand900,
        side: const BorderSide(color: AgriColors.outline),
        textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
      ),
    ),
    listTileTheme: const ListTileThemeData(
      minVerticalPadding: 10,
      titleTextStyle: TextStyle(fontSize: 16, fontWeight: FontWeight.w600, color: AgriColors.ink),
      subtitleTextStyle: TextStyle(fontSize: 14, color: AgriColors.inkMuted),
      iconColor: AgriColors.brand700,
    ),
    dividerTheme: const DividerThemeData(color: AgriColors.borderSubtle, thickness: 1, space: 1),
    bottomSheetTheme: const BottomSheetThemeData(
      backgroundColor: AgriColors.surfaceCard,
      surfaceTintColor: Colors.transparent,
      showDragHandle: true,
      dragHandleColor: AgriColors.borderStrong,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: AgriColors.surfaceCard,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      titleTextStyle: text.titleLarge,
      contentTextStyle: text.bodyMedium,
    ),
    datePickerTheme: const DatePickerThemeData(
      backgroundColor: AgriColors.surfaceCard,
      surfaceTintColor: Colors.transparent,
      headerBackgroundColor: AgriColors.brand800,
      headerForegroundColor: Colors.white,
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: AgriColors.brand900,
      contentTextStyle: const TextStyle(fontSize: 15, color: Colors.white),
      actionTextColor: AgriColors.brand200,
      shape: rounded10,
    ),
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: AgriColors.brand600, linearTrackColor: AgriColors.brand100),
  );
}

/// Tabular figures on every text role, as on the web (`font-variant-numeric: tabular-nums`):
/// doses, totals, dates and pickup codes line up digit for digit.
TextTheme _tabular(TextTheme t) {
  TextStyle? tab(TextStyle? s) => s?.copyWith(fontFeatures: const [FontFeature.tabularFigures()]);
  return t.copyWith(
    displayLarge: tab(t.displayLarge),
    displayMedium: tab(t.displayMedium),
    displaySmall: tab(t.displaySmall),
    headlineLarge: tab(t.headlineLarge),
    headlineMedium: tab(t.headlineMedium),
    headlineSmall: tab(t.headlineSmall),
    titleLarge: tab(t.titleLarge),
    titleMedium: tab(t.titleMedium),
    titleSmall: tab(t.titleSmall),
    bodyLarge: tab(t.bodyLarge),
    bodyMedium: tab(t.bodyMedium),
    bodySmall: tab(t.bodySmall),
    labelLarge: tab(t.labelLarge),
    labelMedium: tab(t.labelMedium),
    labelSmall: tab(t.labelSmall),
  );
}
