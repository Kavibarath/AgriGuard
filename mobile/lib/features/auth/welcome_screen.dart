import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../app/agri_widgets.dart';
import '../../app/spotlight_photo.dart';
import '../../app/theme.dart';

/// Harvest lanes: the same photograph as the web's home page, here in portrait.
const welcomePhoto = 'assets/img/harvest-lanes-portrait.webp';

/// The first screen for someone not signed in: what AgriGuard is, over a harvest field that a
/// spotlight shows in colour (it sweeps across once, then follows a finger), and the way in.
class WelcomeScreen extends StatefulWidget {
  const WelcomeScreen({super.key});

  @override
  State<WelcomeScreen> createState() => _WelcomeScreenState();
}

class _WelcomeScreenState extends State<WelcomeScreen> with SingleTickerProviderStateMixin {
  late final AnimationController _entrance = AnimationController(vsync: this, duration: const Duration(milliseconds: 900));

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // With "Remove animations" on, everything is simply there.
    if (MediaQuery.disableAnimationsOf(context)) {
      _entrance.value = 1;
    } else if (_entrance.isDismissed) {
      _entrance.forward();
    }
  }

  @override
  void dispose() {
    _entrance.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    const shadow = [Shadow(color: Color(0x80000000), blurRadius: 10)];

    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        backgroundColor: AgriColors.brand900,
        body: SpotlightPhoto(
          asset: welcomePhoto,
          alignment: const Alignment(0, -0.1),
          restAt: const Offset(0.62, 0.3),
          child: Stack(
            fit: StackFit.expand,
            children: [
              // Deepest at the bottom, where the words are, so they stay readable over the spotlight.
              const IgnorePointer(
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topCenter,
                      end: Alignment.bottomCenter,
                      colors: [Color(0x99133025), Color(0x14133025), Color(0xB3133025), Color(0xF2133025)],
                      stops: [0, 0.3, 0.55, 1],
                    ),
                  ),
                ),
              ),
              SafeArea(
                child: LayoutBuilder(
                  builder: (context, constraints) => SingleChildScrollView(
                    padding: const EdgeInsets.fromLTRB(24, 12, 24, 20),
                    child: ConstrainedBox(
                      constraints: BoxConstraints(minHeight: constraints.maxHeight - 32),
                      // The mark at the top, everything else at the bottom: on a tall phone the
                      // words and the button sit low, under the thumb; on a short one, it scrolls.
                      child: IntrinsicHeight(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            RiseIn(
                              parent: _entrance,
                              start: 0,
                              child: Row(
                                children: [
                                  const BrandMark(size: 38),
                                  const SizedBox(width: 10),
                                  Text('AgriGuard', style: theme.textTheme.titleLarge?.copyWith(color: Colors.white, fontSize: 24)),
                                ],
                              ),
                            ),
                            const Spacer(),
                            const SizedBox(height: 48),
                            RiseIn(
                              parent: _entrance,
                              start: 0.1,
                              child: Align(
                                alignment: Alignment.centerLeft,
                                child: Container(
                                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                                  decoration: BoxDecoration(
                                    color: Colors.white.withValues(alpha: 0.12),
                                    borderRadius: BorderRadius.circular(999),
                                    border: Border.all(color: Colors.white.withValues(alpha: 0.3)),
                                  ),
                                  child: const Text(
                                    'Crop advisory for smallholder farms',
                                    style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: AgriColors.brand50),
                                  ),
                                ),
                              ),
                            ),
                            const SizedBox(height: 16),
                            RiseIn(
                              parent: _entrance,
                              start: 0.2,
                              child: Semantics(
                                header: true,
                                child: Text.rich(
                                  const TextSpan(
                                    children: [
                                      TextSpan(text: 'Crop advice you can '),
                                      TextSpan(
                                        text: 'trust',
                                        style: TextStyle(color: AgriColors.brand200),
                                      ),
                                      TextSpan(text: ', from the first leaf spot to harvest.'),
                                    ],
                                  ),
                                  style: theme.textTheme.headlineMedium?.copyWith(color: Colors.white, fontSize: 36, height: 1.12, shadows: shadow),
                                ),
                              ),
                            ),
                            const SizedBox(height: 14),
                            RiseIn(
                              parent: _entrance,
                              start: 0.3,
                              child: Text(
                                'Report a problem from the field. AI agents draft a treatment, safety rules check it, '
                                'and an agronomist approves it before anything is sprayed.',
                                style: theme.textTheme.bodyLarge?.copyWith(color: AgriColors.brand50, shadows: shadow),
                              ),
                            ),
                            const SizedBox(height: 28),
                            // The way in, low on the screen, under the thumb.
                            RiseIn(
                              parent: _entrance,
                              start: 0.4,
                              child: FilledButton.icon(
                                onPressed: () => context.push('/login'),
                                style: FilledButton.styleFrom(
                                  minimumSize: const Size.fromHeight(58),
                                  backgroundColor: Colors.white,
                                  foregroundColor: AgriColors.brand800,
                                  iconAlignment: IconAlignment.end,
                                ),
                                icon: const Icon(Icons.arrow_forward),
                                label: const Text('Sign in'),
                              ),
                            ),
                            const SizedBox(height: 14),
                            RiseIn(
                              parent: _entrance,
                              start: 0.45,
                              child: Row(
                                children: [
                                  const Icon(Icons.touch_app_outlined, size: 20, color: AgriColors.brand100),
                                  const SizedBox(width: 8),
                                  Expanded(
                                    child: Text(
                                      'Touch and drag across the field to see it in colour',
                                      style: theme.textTheme.bodySmall?.copyWith(color: AgriColors.brand100),
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
