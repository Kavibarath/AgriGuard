import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'theme.dart';

/// The phone's building blocks, matched to the web console's (web/src/components/ui and
/// components/layout). Screens compose these rather than styling Material widgets one by one.

/// The AgriGuard mark: a leaf inside a shield (crop advice held to a safety rule). The same
/// drawing as the web's BrandMark.tsx, on a canopy tile.
class BrandMark extends StatelessWidget {
  const BrandMark({this.size = 32, super.key});

  final double size;

  @override
  Widget build(BuildContext context) =>
      ExcludeSemantics(child: CustomPaint(size: Size.square(size), painter: const _BrandMarkPainter()));
}

class _BrandMarkPainter extends CustomPainter {
  const _BrandMarkPainter();

  @override
  void paint(Canvas canvas, Size size) {
    // The web mark is drawn on a 32-unit grid.
    canvas.scale(size.width / 32);
    canvas.drawRRect(
      RRect.fromRectAndRadius(const Rect.fromLTWH(0, 0, 32, 32), const Radius.circular(8)),
      Paint()..color = AgriColors.brand500,
    );
    final shield = Path()
      ..moveTo(16, 5.5)
      ..lineTo(8, 8.4)
      ..lineTo(8, 14.7)
      ..cubicTo(8, 19.7, 11.4, 24.1, 16, 25.5)
      ..cubicTo(20.6, 24.1, 24, 19.7, 24, 14.7)
      ..lineTo(24, 8.4)
      ..close();
    canvas.drawPath(
      shield,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.5
        ..strokeJoin = StrokeJoin.round
        ..color = AgriColors.brand50,
    );
    final leaf = Path()
      ..moveTo(12, 19.5)
      ..cubicTo(12, 14.9, 15.2, 11.7, 19.8, 11.7)
      ..cubicTo(19.8, 16.3, 16.6, 19.5, 12, 19.5)
      ..close();
    canvas.drawPath(leaf, Paint()..color = AgriColors.brand200);
    canvas.drawLine(
      const Offset(12, 19.5),
      const Offset(16.4, 15.1),
      Paint()
        ..strokeWidth = 1.5
        ..strokeCap = StrokeCap.round
        ..color = AgriColors.brand800,
    );
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

/// How a status reads. As on the web (StatusBadge.tsx), each tone has its own icon shape, so the
/// meaning survives colour blindness, glare and a grayscale screen: a tick for done, a cross for
/// danger, a triangle for warning, a clock for in progress and a dashed ring for neutral.
enum Tone { neutral, active, warning, done, danger }

/// The fill, ring and ink of each tone.
({Color fill, Color ring, Color ink}) toneColors(Tone tone) => switch (tone) {
      Tone.neutral => (fill: AgriColors.surfaceInset, ring: AgriColors.borderStrong, ink: AgriColors.inkSoft),
      Tone.active => (fill: AgriColors.info50, ring: AgriColors.info200, ink: AgriColors.info800),
      Tone.warning => (fill: AgriColors.warning50, ring: AgriColors.warning200, ink: AgriColors.warning800),
      Tone.done => (fill: AgriColors.success50, ring: AgriColors.success200, ink: AgriColors.success800),
      Tone.danger => (fill: AgriColors.danger50, ring: AgriColors.danger200, ink: AgriColors.danger800),
    };

/// The shape that carries a tone's meaning.
class ToneIcon extends StatelessWidget {
  const ToneIcon(this.tone, {this.size = 18, this.color, super.key});

  final Tone tone;
  final double size;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final ink = color ?? toneColors(tone).ink;
    return switch (tone) {
      Tone.neutral => CustomPaint(size: Size.square(size), painter: _DashedRingPainter(ink)),
      Tone.active => Icon(Icons.schedule, size: size, color: ink),
      Tone.warning => Icon(Icons.warning_amber_rounded, size: size, color: ink),
      Tone.done => Icon(Icons.check_circle_outline, size: size, color: ink),
      Tone.danger => Icon(Icons.cancel_outlined, size: size, color: ink),
    };
  }
}

/// Not started, unknown or closed: a ring that is not finished.
class _DashedRingPainter extends CustomPainter {
  const _DashedRingPainter(this.color);

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = size.width / 12
      ..strokeCap = StrokeCap.round;
    final rect = Rect.fromCircle(center: size.center(Offset.zero), radius: size.width * 0.38);
    const dashes = 8;
    const sweep = 2 * math.pi / dashes;
    for (var i = 0; i < dashes; i++) {
      canvas.drawArc(rect, i * sweep, sweep * 0.55, false, paint);
    }
    canvas.drawCircle(size.center(Offset.zero), size.width / 14, paint..style = PaintingStyle.fill);
  }

  @override
  bool shouldRepaint(covariant _DashedRingPainter oldDelegate) => oldDelegate.color != color;
}

/// A status as a pill: the word carries the meaning, the shape repeats it, the colour reinforces it.
class StatusPill extends StatelessWidget {
  const StatusPill({required this.label, required this.tone, super.key});

  final String label;
  final Tone tone;

  @override
  Widget build(BuildContext context) {
    final c = toneColors(tone);
    return Container(
      padding: const EdgeInsets.fromLTRB(8, 4, 10, 4),
      decoration: BoxDecoration(
        color: c.fill,
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: c.ring),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          ToneIcon(tone, size: 16),
          const SizedBox(width: 5),
          Flexible(
            child: Text(
              label,
              style: TextStyle(fontSize: 14, height: 1.2, fontWeight: FontWeight.w600, color: c.ink),
            ),
          ),
        ],
      ),
    );
  }
}

/// A white card on the warm page, with a hairline and the canopy-tinted resting shadow.
/// [onTap] makes the whole card one 48dp-plus target with a ripple.
class AgriCard extends StatelessWidget {
  const AgriCard({
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.onTap,
    this.color = AgriColors.surfaceCard,
    this.borderColor = AgriColors.borderSubtle,
    this.raised = true,
    super.key,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;
  final Color color;
  final Color borderColor;
  final bool raised;

  @override
  Widget build(BuildContext context) {
    final radius = BorderRadius.circular(12);
    return DecoratedBox(
      decoration: BoxDecoration(borderRadius: radius, boxShadow: raised ? AgriShadows.raised : null),
      child: Material(
        color: color,
        shape: RoundedRectangleBorder(borderRadius: radius, side: BorderSide(color: borderColor)),
        clipBehavior: Clip.antiAlias,
        child: onTap == null ? Padding(padding: padding, child: child) : InkWell(onTap: onTap, child: Padding(padding: padding, child: child)),
      ),
    );
  }
}

/// A rounded glyph tile that leads a card: canopy for crops and cases, earth for soil, harvest
/// and orders.
class GlyphTile extends StatelessWidget {
  const GlyphTile(this.icon, {this.earth = false, this.size = 44, super.key});

  final IconData icon;
  final bool earth;
  final double size;

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: earth ? AgriColors.earth50 : AgriColors.brand50,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: earth ? AgriColors.earth200 : AgriColors.brand200),
        ),
        child: Icon(icon, size: size * 0.5, color: earth ? AgriColors.earth700 : AgriColors.brand700),
      );
}

/// A section's heading inside a screen: sans, with an optional glyph and an action at the end.
class SectionTitle extends StatelessWidget {
  const SectionTitle(this.text, {this.icon, this.trailing, this.earth = false, super.key});

  final String text;
  final IconData? icon;
  final Widget? trailing;
  final bool earth;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: Row(
          children: [
            if (icon != null) ...[
              Icon(icon, size: 20, color: earth ? AgriColors.earth600 : AgriColors.brand700),
              const SizedBox(width: 8),
            ],
            Expanded(child: Semantics(header: true, child: Text(text, style: Theme.of(context).textTheme.titleMedium))),
            ?trailing,
          ],
        ),
      );
}

/// A label above a value: the two-line fact used in summaries and the prescription.
class LabelledValue extends StatelessWidget {
  const LabelledValue({required this.label, required this.value, this.valueStyle, super.key});

  final String label;
  final String value;
  final TextStyle? valueStyle;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: theme.textTheme.bodySmall),
        const SizedBox(height: 2),
        Text(value, style: valueStyle ?? theme.textTheme.bodyLarge?.copyWith(fontWeight: FontWeight.w600)),
      ],
    );
  }
}

/// The bar that holds a screen's main action in the thumb's reach, at the bottom. Put it in the
/// body under an Expanded scroll view, not in bottomNavigationBar, so it rides above the keyboard.
class BottomActionBar extends StatelessWidget {
  const BottomActionBar({required this.children, this.note, super.key});

  final List<Widget> children;

  /// One line above the buttons: what pressing the main one will do.
  final String? note;

  @override
  Widget build(BuildContext context) => DecoratedBox(
        decoration: const BoxDecoration(
          color: AgriColors.surfaceCard,
          border: Border(top: BorderSide(color: AgriColors.borderSubtle)),
          boxShadow: AgriShadows.lifted,
        ),
        child: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (note != null) ...[
                  Text(note!, style: Theme.of(context).textTheme.bodySmall),
                  const SizedBox(height: 10),
                ],
                Row(
                  children: [
                    for (final (i, child) in children.indexed) ...[
                      if (i > 0) const SizedBox(width: 12),
                      child,
                    ],
                  ],
                ),
              ],
            ),
          ),
        ),
      );
}

/// Nothing to show yet: a line drawing in canopy-300 (never a photo), what is missing, and what
/// to do about it.
class EmptyState extends StatelessWidget {
  const EmptyState({required this.title, this.message, this.earth = false, super.key});

  final String title;
  final String? message;
  final bool earth;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 32),
      child: Column(
        children: [
          CustomPaint(size: const Size(120, 84), painter: _SproutPainter(earth: earth)),
          const SizedBox(height: 16),
          Text(title, textAlign: TextAlign.center, style: theme.textTheme.titleMedium),
          if (message != null) ...[
            const SizedBox(height: 6),
            Text(message!, textAlign: TextAlign.center, style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.inkMuted)),
          ],
        ],
      ),
    );
  }
}

/// A seedling on a furrowed bed, in single strokes.
class _SproutPainter extends CustomPainter {
  const _SproutPainter({required this.earth});

  final bool earth;

  @override
  void paint(Canvas canvas, Size size) {
    final line = Paint()
      ..color = AgriColors.brand300
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round;
    final soil = Paint()
      ..color = earth ? AgriColors.earth300 : AgriColors.brand300
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2
      ..strokeCap = StrokeCap.round;
    final w = size.width;
    final h = size.height;

    // The bed and two furrows.
    canvas.drawLine(Offset(w * 0.08, h * 0.82), Offset(w * 0.92, h * 0.82), soil);
    canvas.drawLine(Offset(w * 0.2, h * 0.94), Offset(w * 0.4, h * 0.94), soil);
    canvas.drawLine(Offset(w * 0.6, h * 0.94), Offset(w * 0.8, h * 0.94), soil);

    // The stem and two leaves.
    canvas.drawLine(Offset(w * 0.5, h * 0.82), Offset(w * 0.5, h * 0.36), line);
    final left = Path()
      ..moveTo(w * 0.5, h * 0.56)
      ..cubicTo(w * 0.42, h * 0.38, w * 0.3, h * 0.34, w * 0.22, h * 0.36)
      ..cubicTo(w * 0.26, h * 0.52, w * 0.38, h * 0.6, w * 0.5, h * 0.56);
    final right = Path()
      ..moveTo(w * 0.5, h * 0.42)
      ..cubicTo(w * 0.56, h * 0.2, w * 0.7, h * 0.1, w * 0.8, h * 0.12)
      ..cubicTo(w * 0.78, h * 0.3, w * 0.64, h * 0.42, w * 0.5, h * 0.42);
    canvas.drawPath(left, line);
    canvas.drawPath(right, line);
  }

  @override
  bool shouldRepaint(covariant _SproutPainter oldDelegate) => oldDelegate.earth != earth;
}

/// A small inline progress mark for buttons and status lines.
class InlineSpinner extends StatelessWidget {
  const InlineSpinner({this.size = 18, this.color, super.key});

  final double size;
  final Color? color;

  @override
  Widget build(BuildContext context) =>
      SizedBox.square(dimension: size, child: CircularProgressIndicator(strokeWidth: 2, color: color));
}
