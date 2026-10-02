import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_controller.dart';
import '../features/auth/login_screen.dart';
import '../features/auth/splash_screen.dart';
import '../features/auth/welcome_screen.dart';
import '../features/cases/case_detail_screen.dart';
import '../features/cases/cases_screen.dart';
import '../features/cases/new_case_screen.dart';
import '../features/harvest/harvest_screen.dart';
import '../features/home/home_screen.dart';
import '../features/orders/orders_screen.dart';
import '../features/registry/farms_screen.dart';
import '../features/registry/new_plot_screen.dart';
import '../features/registry/plot_screen.dart';

/// Every screen's page. With the system's "Remove animations" setting on, a screen simply appears:
/// no transition on the new screen and none on the old. Otherwise, the platform's usual transition.
Page<void> _page(BuildContext context, GoRouterState state, Widget child) => MediaQuery.disableAnimationsOf(context)
    ? NoTransitionPage<void>(key: state.pageKey, child: child)
    : MaterialPage<void>(key: state.pageKey, child: child);

/// Bridges Riverpod → go_router: any auth change re-evaluates `redirect`.
class _AuthChangeNotifier extends ChangeNotifier {
  void bump() => notifyListeners();
}

final routerProvider = Provider<GoRouter>((ref) {
  final authChanged = _AuthChangeNotifier();
  ref.listen(authControllerProvider, (_, _) => authChanged.bump());
  ref.onDispose(authChanged.dispose);

  return GoRouter(
    initialLocation: '/home',
    refreshListenable: authChanged,
    debugLogDiagnostics: kDebugMode,
    redirect: (context, state) {
      final auth = ref.read(authControllerProvider);
      final location = state.matchedLocation;

      // Still reading the stored session: hold on the splash rather than flashing the login screen.
      if (auth.isLoading) return location == '/splash' ? null : '/splash';

      // Signed out: the welcome screen, and the sign-in screen it leads to. Signed in: never either.
      final signedIn = auth.value != null;
      const publicScreens = {'/welcome', '/login'};
      if (!signedIn) return publicScreens.contains(location) ? null : '/welcome';
      if (publicScreens.contains(location) || location == '/splash') return '/home';
      return null;
    },
    routes: [
      GoRoute(path: '/splash', pageBuilder: (context, state) => _page(context, state, const SplashScreen())),
      GoRoute(path: '/welcome', pageBuilder: (context, state) => _page(context, state, const WelcomeScreen())),
      GoRoute(path: '/login', pageBuilder: (context, state) => _page(context, state, const LoginScreen())),
      GoRoute(path: '/home', pageBuilder: (context, state) => _page(context, state, const HomeScreen())),
      GoRoute(path: '/harvest', pageBuilder: (context, state) => _page(context, state, const HarvestScreen())),
      GoRoute(path: '/orders', pageBuilder: (context, state) => _page(context, state, const OrdersScreen())),
      GoRoute(
        path: '/farms',
        pageBuilder: (context, state) => _page(context, state, const FarmsScreen()),
        routes: [
          GoRoute(
            path: ':farmId',
            pageBuilder: (context, state) => _page(context, state, FarmScreen(farmId: state.pathParameters['farmId']!)),
            routes: [
              GoRoute(path: 'plots/new', pageBuilder: (context, state) => _page(context, state, NewPlotScreen(farmId: state.pathParameters['farmId']!))),
            ],
          ),
        ],
      ),
      GoRoute(path: '/plots/:plotId', pageBuilder: (context, state) => _page(context, state, PlotScreen(plotId: state.pathParameters['plotId']!))),
      GoRoute(
        path: '/cases',
        pageBuilder: (context, state) => _page(context, state, const CasesScreen()),
        routes: [
          // Before ':caseId', so "new" is not read as a case id.
          GoRoute(path: 'new', pageBuilder: (context, state) => _page(context, state, const NewCaseScreen())),
          GoRoute(
            path: ':caseId',
            pageBuilder: (context, state) => _page(context, state, CaseDetailScreen(caseId: state.pathParameters['caseId']!)),
          ),
        ],
      ),
    ],
  );
});
