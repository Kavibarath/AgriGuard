import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_controller.dart';
import '../features/auth/login_screen.dart';
import '../features/auth/splash_screen.dart';
import '../features/cases/case_detail_screen.dart';
import '../features/cases/cases_screen.dart';
import '../features/cases/new_case_screen.dart';
import '../features/harvest/harvest_screen.dart';
import '../features/home/home_screen.dart';
import '../features/orders/orders_screen.dart';
import '../features/registry/farms_screen.dart';
import '../features/registry/new_plot_screen.dart';
import '../features/registry/plot_screen.dart';

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

      final signedIn = auth.value != null;
      if (!signedIn) return location == '/login' ? null : '/login';
      if (location == '/login' || location == '/splash') return '/home';
      return null;
    },
    routes: [
      GoRoute(path: '/splash', builder: (_, _) => const SplashScreen()),
      GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
      GoRoute(path: '/home', builder: (_, _) => const HomeScreen()),
      GoRoute(path: '/harvest', builder: (_, _) => const HarvestScreen()),
      GoRoute(path: '/orders', builder: (_, _) => const OrdersScreen()),
      GoRoute(
        path: '/farms',
        builder: (_, _) => const FarmsScreen(),
        routes: [
          GoRoute(
            path: ':farmId',
            builder: (_, state) => FarmScreen(farmId: state.pathParameters['farmId']!),
            routes: [
              GoRoute(path: 'plots/new', builder: (_, state) => NewPlotScreen(farmId: state.pathParameters['farmId']!)),
            ],
          ),
        ],
      ),
      GoRoute(path: '/plots/:plotId', builder: (_, state) => PlotScreen(plotId: state.pathParameters['plotId']!)),
      GoRoute(
        path: '/cases',
        builder: (_, _) => const CasesScreen(),
        routes: [
          // Before ':caseId', so "new" is not read as a case id.
          GoRoute(path: 'new', builder: (_, _) => const NewCaseScreen()),
          GoRoute(
            path: ':caseId',
            builder: (_, state) => CaseDetailScreen(caseId: state.pathParameters['caseId']!),
          ),
        ],
      ),
    ],
  );
});
