import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'router.dart';

class AgriGuardApp extends ConsumerWidget {
  const AgriGuardApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return MaterialApp.router(
      title: 'AgriGuard',
      routerConfig: ref.watch(routerProvider),
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF2F855A)),
        inputDecorationTheme: const InputDecorationTheme(border: OutlineInputBorder()),
        // Field use: large tap targets, readable in sunlight.
        visualDensity: VisualDensity.comfortable,
      ),
    );
  }
}
