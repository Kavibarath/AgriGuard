import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/agri_widgets.dart';
import '../../app/motion.dart';
import '../../app/theme.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import '../cases/outbox_banner.dart';

/// The home screen's full-screen photograph for each role (docs/design/IMAGE-CREDITS.md): harvest
/// lanes for the farmer, fields from above for the agronomist, a tractor hauling inputs for the
/// dealer and the administrator. No person is recognisable in any of them.
(String, Alignment) homePhotoFor(UserRole role) => switch (role) {
  UserRole.farmer => ('assets/img/harvest-lanes-portrait.webp', const Alignment(0, -0.2)),
  UserRole.fieldAgronomist => ('assets/img/fields-aerial-portrait.webp', const Alignment(0, -0.3)),
  _ => ('assets/img/tractor-maize-portrait.webp', const Alignment(-0.3, 0)),
};

/// Landing screen after sign-in: who is signed in, anything waiting to send, and the way into
/// each part of the app for the role, all over the role's photograph. A farmer's most important
/// task, reporting a problem, sits at the bottom, under the thumb.
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    if (user == null) return const SizedBox.shrink(); // router is already redirecting
    final isFarmer = user.role == UserRole.farmer;
    final top = MediaQuery.paddingOf(context).top;

    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        backgroundColor: AgriColors.brand900,
        body: Stack(
          children: [
            // The photo stays put while the cards scroll over it.
            Positioned.fill(child: _Backdrop(role: user.role)),
            Column(
              children: [
                Expanded(
                  child: ListView(
                    padding: EdgeInsets.fromLTRB(16, top + 4, 16, 24),
                    children: [
                      _TopBar(onSignOut: () => ref.read(authControllerProvider.notifier).logout()),
                      const SizedBox(height: 28),
                      _Greeting(user: user),
                      const SizedBox(height: 24),
                      if (isFarmer) ...[
                        // Draws nothing when no report is waiting.
                        OutboxReminder(onOpen: () => context.push('/cases')),
                        const SizedBox(height: 4),
                      ],
                      ..._tiles(user.role),
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
          ],
        ),
      ),
    );
  }

  List<Widget> _tiles(UserRole role) => switch (role) {
    UserRole.farmer => const [
      _SectionLabel('Your farm'),
      _FeatureTile(
        index: 0,
        icon: Icons.receipt_long_outlined,
        title: 'My crop problems',
        subtitle: 'AI advice, review and prescriptions',
        route: '/cases',
      ),
      _FeatureTile(
        index: 1,
        icon: Icons.landscape_outlined,
        title: 'My farms & plots',
        subtitle: 'Plots, crop stages and spray safety',
        route: '/farms',
      ),
      _SectionLabel('Inputs and harvest'),
      _FeatureTile(
        index: 2,
        icon: Icons.shopping_bag_outlined,
        title: 'My orders',
        subtitle: 'Prescribed inputs, where to collect, pickup code',
        route: '/orders',
        earth: true,
      ),
      _FeatureTile(
        index: 3,
        icon: Icons.local_shipping_outlined,
        title: 'Harvest collection',
        subtitle: 'Best harvest days, spray weather, book a slot',
        route: '/harvest',
        earth: true,
      ),
    ],
    UserRole.fieldAgronomist => const [
      _SectionLabel('Your district'),
      _FeatureTile(index: 0, icon: Icons.fact_check_outlined, title: 'Cases in my district', subtitle: 'Field triage and follow-up', route: '/cases'),
      _FeatureTile(
        index: 1,
        icon: Icons.landscape_outlined,
        title: 'Farms in my district',
        subtitle: 'Plots, crop stages and spray safety',
        route: '/farms',
      ),
    ],
    _ => const [_WebConsoleNote()],
  };
}

/// The role's photograph across the whole screen, under a canopy scrim that is deepest where white
/// words sit on it: white text stays readable over the brightest wheat.
class _Backdrop extends StatelessWidget {
  const _Backdrop({required this.role});

  final UserRole role;

  @override
  Widget build(BuildContext context) {
    final (asset, alignment) = homePhotoFor(role);
    final size = MediaQuery.sizeOf(context);
    return Stack(
      fit: StackFit.expand,
      children: [
        Image.asset(
          asset,
          fit: BoxFit.cover,
          alignment: alignment,
          cacheWidth: (size.width * MediaQuery.devicePixelRatioOf(context)).round(),
          excludeFromSemantics: true,
          errorBuilder: (_, _, _) => const SizedBox.shrink(),
        ),
        const DecoratedBox(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topCenter,
              end: Alignment.bottomCenter,
              colors: [Color(0xC7133025), Color(0x99133025), Color(0x8C133025), Color(0xB3133025)],
              stops: [0, 0.32, 0.6, 1],
            ),
          ),
        ),
      ],
    );
  }
}

/// The mark, the name, and signing out.
class _TopBar extends StatelessWidget {
  const _TopBar({required this.onSignOut});

  final VoidCallback onSignOut;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      const BrandMark(size: 34),
      const SizedBox(width: 10),
      Expanded(
        child: Text('AgriGuard', style: Theme.of(context).textTheme.titleLarge?.copyWith(color: Colors.white)),
      ),
      IconButton(tooltip: 'Sign out', color: Colors.white, icon: const Icon(Icons.logout), onPressed: onSignOut),
    ],
  );
}

/// "Good morning", who is signed in, and in which role, in white on the photograph.
class _Greeting extends StatelessWidget {
  const _Greeting({required this.user});

  final UserSummary user;

  static String _partOfDay(DateTime now) => now.hour < 12
      ? 'Good morning'
      : now.hour < 17
      ? 'Good afternoon'
      : 'Good evening';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    const shadow = [Shadow(color: Color(0x66000000), blurRadius: 8)];
    return Semantics(
      container: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            _partOfDay(DateTime.now()),
            style: theme.textTheme.titleSmall?.copyWith(color: AgriColors.brand200, shadows: shadow),
          ),
          const SizedBox(height: 2),
          Text(
            user.fullName,
            style: theme.textTheme.headlineMedium?.copyWith(color: Colors.white, fontSize: 32, shadows: shadow),
          ),
          const SizedBox(height: 4),
          Text(
            '${user.role.label} · ${user.email}',
            style: theme.textTheme.bodyMedium?.copyWith(color: AgriColors.brand50, shadows: shadow),
          ),
        ],
      ),
    );
  }
}

/// A heading between groups of tiles, in white on the photograph.
class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(4, 16, 4, 8),
    child: Semantics(
      header: true,
      child: Text(
        text,
        style: Theme.of(context).textTheme.labelLarge?.copyWith(
          color: Colors.white,
          shadows: const [Shadow(color: Color(0x66000000), blurRadius: 6)],
        ),
      ),
    ),
  );
}

class _FeatureTile extends StatelessWidget {
  const _FeatureTile({required this.index, required this.icon, required this.title, required this.subtitle, required this.route, this.earth = false});

  /// Its place in the list, for the staggered rise.
  final int index;
  final IconData icon;
  final String title;
  final String subtitle;
  final String route;

  /// Orders and harvest carry the earth accent, as the dealer and harvest pages do on the web.
  final bool earth;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    // A pane of glass over the photograph, with white words on it.
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Reveal(
        index: index,
        child: GlassCard(
          padding: const EdgeInsets.fromLTRB(14, 14, 8, 14),
          onTap: () => context.push(route),
          child: Row(
            children: [
              Container(
                width: 44,
                height: 44,
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(12),
                  gradient: LinearGradient(
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                    colors: earth ? const [AgriColors.earth400, AgriColors.earth600] : const [AgriColors.brand400, AgriColors.brand700],
                  ),
                  border: Border.all(color: const Color(0x40FFFFFF)),
                ),
                child: Icon(icon, size: 22, color: Colors.white),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: theme.textTheme.titleSmall?.copyWith(color: Colors.white)),
                    const SizedBox(height: 2),
                    Text(subtitle, style: theme.textTheme.bodySmall?.copyWith(color: AgriColors.brand50)),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right, color: Colors.white),
            ],
          ),
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
