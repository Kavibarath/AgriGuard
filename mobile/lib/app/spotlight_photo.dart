import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

import 'theme.dart';

/// The spotlight's radius on the phone, in logical pixels. The web's is 260; a phone screen is
/// narrower, so the circle is too.
const phoneSpotlightRadius = 150.0;

/// How far the eased position moves towards the finger each frame: the web's 0.1.
const _easing = 0.1;

/// The opening sweep: the spotlight glides once across the photo, then rests.
const _sweepDuration = Duration(milliseconds: 5200);

/// A photograph in canopy monochrome with a soft spotlight that shows it in true colour. The
/// phone's version of the web's cursor spotlight (web/src/components/spotlight): a finger takes
/// the cursor's place.
///
/// On opening, the spotlight sweeps once across the photo and settles at [restAt]; touching and
/// dragging anywhere over the photo moves it, easing behind the finger. With the system's
/// "Remove animations" setting there is no sweep and no easing: the spotlight sits at [restAt],
/// and jumps straight to a touch.
class SpotlightPhoto extends StatefulWidget {
  const SpotlightPhoto({
    required this.asset,
    this.alignment = Alignment.center,
    this.restAt = const Offset(0.6, 0.45),
    this.radius = phoneSpotlightRadius,
    this.child,
    super.key,
  });

  final String asset;
  final Alignment alignment;

  /// Where the spotlight rests, as fractions of the photo's width and height.
  final Offset restAt;
  final double radius;

  /// Drawn over the photo (scrims, words, buttons); touches pass through to it as usual.
  final Widget? child;

  @override
  State<SpotlightPhoto> createState() => _SpotlightPhotoState();
}

class _SpotlightPhotoState extends State<SpotlightPhoto> with SingleTickerProviderStateMixin {
  late final Ticker _ticker = createTicker(_tick);
  Size? _size;
  Offset? _pos; // eased: where the spotlight is drawn
  Offset? _target; // where it is heading: the finger, or the sweep's path
  bool _sweeping = false;

  bool get _still => MediaQuery.disableAnimationsOf(context);

  Offset get _rest => Offset(_size!.width * widget.restAt.dx, _size!.height * widget.restAt.dy);

  @override
  void dispose() {
    _ticker.dispose();
    super.dispose();
  }

  /// First layout: place the spotlight, and start the opening sweep unless motion is off.
  void _place(Size size) {
    if (_size == size) return;
    final first = _size == null;
    _size = size;
    if (!first) {
      // A rotation or a keyboard: keep the spotlight inside the photo.
      _pos = _rest;
      _target = _rest;
      return;
    }
    if (_still) {
      _pos = _rest;
      _target = _rest;
    } else {
      _sweeping = true;
      _pos = _sweepAt(0);
      _target = _pos;
      _ticker.start();
    }
  }

  /// The sweep's path at [t] (0 to 1): in from the upper left, a gentle swing, ending at rest.
  Offset _sweepAt(double t) {
    final size = _size!;
    final eased = Curves.easeInOut.transform(t);
    final from = Offset(size.width * 0.12, size.height * 0.2);
    final x = from.dx + (_rest.dx - from.dx) * eased;
    final y = from.dy + (_rest.dy - from.dy) * eased + math.sin(eased * math.pi) * size.height * 0.12;
    return Offset(x, y);
  }

  void _tick(Duration elapsed) {
    if (_sweeping) {
      final t = elapsed.inMicroseconds / _sweepDuration.inMicroseconds;
      if (t >= 1) {
        _sweeping = false;
        _target = _rest;
      } else {
        _target = _sweepAt(t);
      }
    }
    final pos = _pos!;
    final target = _target!;
    final next = pos + (target - pos) * _easing;
    setState(() => _pos = next);
    if (!_sweeping && (target - next).distance < 0.5) {
      setState(() => _pos = target);
      _ticker.stop();
    }
  }

  void _touch(Offset at) {
    _sweeping = false;
    _target = at;
    if (_still) {
      setState(() => _pos = at);
    } else if (!_ticker.isActive) {
      _ticker.start();
    }
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        _place(constraints.biggest);
        final pos = _pos!;
        final size = constraints.biggest;
        final dpr = MediaQuery.devicePixelRatioOf(context);

        Widget photo() => Image.asset(
          widget.asset,
          fit: BoxFit.cover,
          alignment: widget.alignment,
          width: size.width,
          height: size.height,
          cacheWidth: (size.width * dpr).round(),
          excludeFromSemantics: true,
          errorBuilder: (_, _, _) => const SizedBox.shrink(),
        );

        // Listener, not a gesture detector: it sees every touch without claiming any, so buttons
        // and scrolling over the photo work as they always do.
        return Listener(
          behavior: HitTestBehavior.translucent,
          onPointerDown: (e) => _touch(e.localPosition),
          onPointerMove: (e) => _touch(e.localPosition),
          // Nothing of the photo, its tint or its spotlight ever paints outside the photo's box.
          child: ClipRect(
            child: Stack(
              fit: StackFit.expand,
              children: [
                ExcludeSemantics(
                  child: ColoredBox(
                    color: AgriColors.brand900,
                    // Canopy monochrome: grey, a little darker and harder, tinted canopy green.
                    child: ColorFiltered(colorFilter: const ColorFilter.matrix(_canopyMonochrome), child: photo()),
                  ),
                ),
                ExcludeSemantics(
                  child: ShaderMask(
                    blendMode: BlendMode.dstIn,
                    shaderCallback: (rect) => RadialGradient(
                      center: Alignment(pos.dx / rect.width * 2 - 1, pos.dy / rect.height * 2 - 1),
                      radius: widget.radius / rect.shortestSide,
                      // The web's stops: solid to 40%, then fading to nothing at the edge.
                      colors: const [
                        Color(0xFFFFFFFF),
                        Color(0xFFFFFFFF),
                        Color(0xBFFFFFFF),
                        Color(0x66FFFFFF),
                        Color(0x1FFFFFFF),
                        Color(0x00FFFFFF),
                      ],
                      stops: const [0, 0.4, 0.6, 0.75, 0.88, 1],
                    ).createShader(rect),
                    child: photo(),
                  ),
                ),
                ?widget.child,
              ],
            ),
          ),
        );
      },
    );
  }
}

// Greyscale at 85% brightness with a little more contrast (the web's `grayscale contrast-110
// brightness-75`), then multiplied by canopy green: brand-800 at 60%, as the web's overlay does.
// One colour matrix, not a blend-mode filter: a matrix leaves transparent pixels transparent,
// where a multiply filter would tint everything around the photo too.
const _r = 0.4 + 0.6 * 0x1A / 255; // brand-800 #1A4230, channel by channel
const _g = 0.4 + 0.6 * 0x42 / 255;
const _b = 0.4 + 0.6 * 0x30 / 255;
const _canopyMonochrome = <double>[
  0.2126 * 0.85 * _r, 0.7152 * 0.85 * _r, 0.0722 * 0.85 * _r, 0, -12 * _r, //
  0.2126 * 0.85 * _g, 0.7152 * 0.85 * _g, 0.0722 * 0.85 * _g, 0, -12 * _g, //
  0.2126 * 0.85 * _b, 0.7152 * 0.85 * _b, 0.0722 * 0.85 * _b, 0, -12 * _b, //
  0, 0, 0, 1, 0, //
];

/// Fades and lifts a block into place as part of a screen's entrance, timed by [parent] between
/// [start] and [start] + 0.55. Give it a finished animation (value 1) when animations are off.
class RiseIn extends StatelessWidget {
  const RiseIn({required this.parent, required this.start, required this.child, super.key});

  final Animation<double> parent;
  final double start;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final curve = CurvedAnimation(
      parent: parent,
      curve: Interval(start, math.min(1, start + 0.55), curve: Curves.easeOutCubic),
    );
    return FadeTransition(
      opacity: curve,
      child: SlideTransition(
        position: Tween(begin: const Offset(0, 0.12), end: Offset.zero).animate(curve),
        child: child,
      ),
    );
  }
}
