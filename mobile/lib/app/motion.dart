import 'dart:math' as math;

import 'package:flutter/material.dart';

/// Scrolling that eases to a stop and springs softly at the ends, on every screen. The default on
/// Android stops hard with a stretch; this reads as one smooth surface, as on the web.
class AgriScrollBehavior extends MaterialScrollBehavior {
  const AgriScrollBehavior();

  @override
  ScrollPhysics getScrollPhysics(BuildContext context) => const BouncingScrollPhysics(parent: AlwaysScrollableScrollPhysics());
}

/// Rises and fades a card in when it is first built, which in a lazy list means as it scrolls into
/// view. [index] staggers neighbours (capped, so a long list never waits). With "Remove
/// animations" on, it is simply there.
class Reveal extends StatefulWidget {
  const Reveal({required this.child, this.index = 0, super.key});

  final Widget child;
  final int index;

  @override
  State<Reveal> createState() => _RevealState();
}

class _RevealState extends State<Reveal> with SingleTickerProviderStateMixin {
  static const _rise = Duration(milliseconds: 520);
  static const _stagger = Duration(milliseconds: 70);

  // One controller covers the stagger and the rise, so no timer is ever left pending.
  late final Duration _delay = _stagger * math.min(widget.index, 5);
  late final AnimationController _controller = AnimationController(vsync: this, duration: _delay + _rise);
  late final Animation<double> _progress = CurvedAnimation(
    parent: _controller,
    curve: Interval(_delay.inMicroseconds / (_delay + _rise).inMicroseconds, 1, curve: Curves.easeOutCubic),
  );

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.disableAnimationsOf(context)) {
      _controller.value = 1;
    } else if (_controller.isDismissed) {
      _controller.forward();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => FadeTransition(
    opacity: _progress,
    child: SlideTransition(
      position: Tween(begin: const Offset(0, 0.08), end: Offset.zero).animate(_progress),
      child: widget.child,
    ),
  );
}
