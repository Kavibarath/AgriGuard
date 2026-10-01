import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/agri_widgets.dart';
import '../../app/theme.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import '../cases/outbox_banner.dart';

/// Paddy carried in from the field: the home header's photograph, one of the phone's only two
/// (docs/design/IMAGE-CREDITS.md). The person in it is a small figure in a landscape; no name is
/// ever set over them, so no one pictured is presented as an AgriGuard user.
const homePhoto = 'assets/img/harvest-carry-portrait.webp';

/// Landing screen after sign-in: who is signed in, anything waiting to send, and the way into
/// each part of the app for the role. A farmer's most important task, reporting a problem, sits
/// at the bottom, under the thumb.
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    if (user == null) return const SizedBox.shrink(); // router is already redirecting
    final isFarmer = user.role == UserRole.farmer;

    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        body: Column(
          children: [
            Expanded(
              child: ListView(
                padding: EdgeInsets.zero,
                children: [
                  // The user card overlaps the photo's lower edge. A Stack, not a transform, so the
                  // card's accessibility bounds sit where it is drawn.
                  Stack(
                    children: [
                      _Header(onSignOut: () => ref.read(authControllerProvider.notifier).logout()),
                      Padding(
                        padding: EdgeInsets.fromLTRB(16, _Header.heightFor(context) - 28, 16, 0),
                        child: _UserCard(user: user),
                      ),
                    ],
                  ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (isFarmer) ...[
                          const SizedBox(height: 12),
                          // Draws nothing when no report is waiting.
                          OutboxReminder(onOpen: () => context.push('/cases')),
                        ],
                        ..._tiles(user.role),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            if (isFarmer)
              BottomActionBar(
                children: [
                  Expanded(
                    child: FilledButton.icon(
                      onPressed: () => context.push('/cases/new'),
                      style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(58)),
                      icon: const Icon(Icons.add_a_photo_outlined, size: 24),
                      label: const Text('Report a crop problem'),
                    ),
                  ),
                ],
              ),
          ],
        ),
      ),
    );
  }

  List<Widget> _tiles(UserRole role) => switch (role) {
        UserRole.farmer => const [
            _SectionLabel('Your farm'),
            _FeatureTile(icon: Icons.receipt_long_outlined, title: 'My crop problems', subtitle: 'AI advice, review and prescriptions', route: '/cases'),
            _FeatureTile(icon: Icons.landscape_outlined, title: 'My farms & plots', subtitle: 'Plots, crop stages and spray safety', route: '/farms'),
            _SectionLabel('Inputs and harvest'),
            _FeatureTile(icon: Icons.shopping_bag_outlined, title: 'My orders', subtitle: 'Prescribed inputs, where to collect, pickup code', route: '/orders', earth: true),
            _FeatureTile(icon: Icons.local_shipping_outlined, title: 'Harvest collection', subtitle: 'Best harvest days, spray weather, book a slot', route: '/harvest', earth: true),
          ],
        UserRole.fieldAgronomist => const [
            _SectionLabel('Your district'),
            _FeatureTile(icon: Icons.fact_check_outlined, title: 'Cases in my district', subtitle: 'Field triage and follow-up', route: '/cases'),
            _FeatureTile(icon: Icons.landscape_outlined, title: 'Farms in my district', subtitle: 'Plots, crop stages and spray safety', route: '/farms'),
          ],
        _ => const [_WebConsoleNote()],
      };
}

/// The photograph strip with the mark and sign-out, over a canopy scrim.
class _Header extends StatelessWidget {
  const _Header({required this.onSignOut});

  final VoidCallback onSignOut;

  /// The strip's height: 168 under the status bar.
  static double heightFor(BuildContext context) => 168 + MediaQuery.paddingOf(context).top;

  @override
  Widget build(BuildContext context) {
    final top = MediaQuery.paddingOf(context).top;
    final width = MediaQuery.sizeOf(context).width;
    return SizedBox(
      height: heightFor(context),
      child: Stack(
        fit: StackFit.expand,
        children: [
          ColoredBox(
            color: AgriColors.brand800,
            child: Image.asset(
              homePhoto,
              fit: BoxFit.cover,
              // The tree line and the field, with the figure carrying paddy below the middle.
              alignment: const Alignment(0, -0.2),
              cacheWidth: (width * MediaQuery.devicePixelRatioOf(context)).round(),
              excludeFromSemantics: true,
              errorBuilder: (_, _, _) => const SizedBox.shrink(),
            ),
          ),
          const DecoratedBox(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: [Color(0x99133025), Color(0x26133025), Color(0x66133025)],
                stops: [0, 0.55, 1],
              ),
            ),
          ),
          Positioned(
            top: top + 8,
            left: 16,
            right: 4,
            child: Row(
              children: [
                const BrandMark(size: 34),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    'AgriGuard',
                    style: Theme.of(context).textTheme.titleLarge?.copyWith(color: Colors.white),
                  ),
                ),
                IconButton(
                  tooltip: 'Sign out',
                  color: Colors.white,
                  icon: const Icon(Icons.logout),
                  onPressed: onSignOut,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Who is signed in, and in which role.
class _UserCard extends StatelessWidget {
  const _UserCard({required this.user});

  final UserSummary user;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final initials = user.fullName.trim().split(RegExp(r'\s+')).take(2).map((w) => w.isEmpty ? '' : w[0].toUpperCase()).join();
    // Its own accessibility node: read as "who is signed in", apart from the header above it.
    return Semantics(container: true, child: AgriCard(
      child: Row(
        children: [
          Container(
            width: 48,
            height: 48,
            alignment: Alignment.center,
            decoration: const BoxDecoration(color: AgriColors.brand100, shape: BoxShape.circle),
            child: ExcludeSemantics(
              child: Text(initials, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700, color: AgriColors.brand800)),
            ),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(user.fullName, style: theme.textTheme.titleLarge),
                const SizedBox(height: 2),
                Text('${user.role.label} · ${user.email}', style: theme.textTheme.bodySmall),
              ],
            ),
          ),
        ],
      ),
    ));
  }
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(4, 16, 4, 8),
        child: Semantics(
          header: true,
          child: Text(text, style: Theme.of(context).textTheme.labelLarge?.copyWith(color: AgriColors.inkMuted)),
        ),
      );
}

class _FeatureTile extends StatelessWidget {
  const _FeatureTile({required this.icon, required this.title, required this.subtitle, required this.route, this.earth = false});

  final IconData icon;
  final String title;
  final String subtitle;
  final String route;

  /// Orders and harvest carry the earth accent, as the dealer and harvest pages do on the web.
  final bool earth;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: AgriCard(
        padding: const EdgeInsets.fromLTRB(14, 14, 8, 14),
        onTap: () => context.push(route),
        child: Row(
          children: [
            GlyphTile(icon, earth: earth),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: theme.textTheme.titleSmall),
                  const SizedBox(height: 2),
                  Text(subtitle, style: theme.textTheme.bodySmall),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, color: AgriColors.inkMuted),
          ],
        ),
      ),
    );
  }
}

/// Dealers and administrators work in the web console; the phone says so plainly.
class _WebConsoleNote extends StatelessWidget {
  const _WebConsoleNote();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: AgriCard(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const GlyphTile(Icons.desktop_windows_outlined, earth: true),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Use the web console', style: theme.textTheme.titleSmall),
                  const SizedBox(height: 2),
                  Text('Dealer and administrator tools are on the web app', style: theme.textTheme.bodySmall),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
