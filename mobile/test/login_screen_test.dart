import 'package:agriguard_mobile/app/app.dart';
import 'package:agriguard_mobile/core/api/api_exception.dart';
import 'package:agriguard_mobile/core/storage/token_storage.dart';
import 'package:agriguard_mobile/features/auth/auth_repository.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'fixtures.dart';

class MockAuthRepository extends Mock implements AuthRepository {}

void main() {
  late MockAuthRepository repository;

  setUp(() => repository = MockAuthRepository());

  Future<void> pumpApp(WidgetTester tester, {InMemoryTokenStorage? storage}) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          authRepositoryProvider.overrideWithValue(repository),
          tokenStorageProvider.overrideWithValue(storage ?? InMemoryTokenStorage()),
        ],
        child: const AgriGuardApp(),
      ),
    );
    await tester.pumpAndSettle();
  }

  /// Signed out, the app opens on the welcome screen; its Sign in button leads to the form.
  Future<void> openSignIn(WidgetTester tester) async {
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();
  }

  Future<void> signIn(WidgetTester tester, String email, String password) async {
    await tester.enterText(find.widgetWithText(TextFormField, 'Email'), email);
    await tester.enterText(find.widgetWithText(TextFormField, 'Password'), password);
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();
  }

  testWidgets('starts on the welcome screen when signed out, which leads to sign-in', (tester) async {
    await pumpApp(tester);

    expect(find.textContaining('Crop advice you can'), findsOneWidget);
    expect(find.text('Sign in to your farm advisory'), findsNothing);

    await openSignIn(tester);

    expect(find.text('Sign in to your farm advisory'), findsOneWidget);
  });

  testWidgets('goes back from sign-in to the welcome screen', (tester) async {
    await pumpApp(tester);
    await openSignIn(tester);

    await tester.tap(find.byTooltip('Back'));
    await tester.pumpAndSettle();

    expect(find.textContaining('Crop advice you can'), findsOneWidget);
  });

  testWidgets('the welcome spotlight sweeps across once, then rests', (tester) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          authRepositoryProvider.overrideWithValue(repository),
          tokenStorageProvider.overrideWithValue(InMemoryTokenStorage()),
        ],
        child: const AgriGuardApp(),
      ),
    );
    for (var i = 0; i < 12; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }

    // A second and a bit in: the screen is up, and the spotlight is still on its way.
    expect(find.textContaining('Crop advice you can'), findsOneWidget);
    expect(tester.binding.hasScheduledFrame, isTrue);

    // It settles by itself: nothing on the screen animates for ever.
    await tester.pumpAndSettle(const Duration(milliseconds: 100), EnginePhase.sendSemanticsUpdate, const Duration(seconds: 10));
    expect(tester.binding.hasScheduledFrame, isFalse);
  });

  testWidgets('with "Remove animations" on, nothing moves: no sweep, no entrance, no transition', (tester) async {
    tester.platformDispatcher.accessibilityFeaturesTestValue = const FakeAccessibilityFeatures(disableAnimations: true);
    addTearDown(tester.platformDispatcher.clearAccessibilityFeaturesTestValue);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          authRepositoryProvider.overrideWithValue(repository),
          tokenStorageProvider.overrideWithValue(InMemoryTokenStorage()),
        ],
        child: const AgriGuardApp(),
      ),
    );
    for (var i = 0; i < 12; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }

    // Where the same moment with motion still had the spotlight travelling, there is nothing to draw.
    expect(find.textContaining('Crop advice you can'), findsOneWidget);
    expect(tester.binding.hasScheduledFrame, isFalse);

    // The sign-in screen is in its final place from its first frame: nothing slides or fades in.
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));
    final heading = find.text('Sign in to your farm advisory');
    final early = tester.getTopLeft(heading);
    for (final fade in find.ancestor(of: heading, matching: find.byType(FadeTransition)).evaluate()) {
      expect((fade.widget as FadeTransition).opacity.value, 1);
    }
    await tester.pumpAndSettle();
    expect(tester.getTopLeft(heading), early);
  });

  testWidgets('goes straight home when a session is stored', (tester) async {
    await pumpApp(tester, storage: InMemoryTokenStorage(farmerSession()));

    expect(find.text('Sunil Perera'), findsOneWidget);
    verifyZeroInteractions(repository);
  });

  testWidgets('validates before calling the API', (tester) async {
    await pumpApp(tester);
    await openSignIn(tester);

    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Enter your email'), findsOneWidget);
    expect(find.text('Enter your password'), findsOneWidget);
    verifyZeroInteractions(repository);
  });

  testWidgets('shows the API message on wrong credentials and stays on login', (tester) async {
    when(() => repository.login(any(), any()))
        .thenThrow(ApiException(message: 'Invalid credentials.', statusCode: 401));
    await pumpApp(tester);
    await openSignIn(tester);

    await signIn(tester, farmer.email, 'wrong');

    expect(find.text('Invalid credentials.'), findsOneWidget);
    expect(find.text('Sign in to your farm advisory'), findsOneWidget);
  });

  testWidgets('navigates home after a successful login', (tester) async {
    when(() => repository.login(farmer.email, 'AgriGuard!Demo1'))
        .thenAnswer((_) async => farmerSession());
    await pumpApp(tester);
    await openSignIn(tester);

    await signIn(tester, farmer.email, 'AgriGuard!Demo1');

    expect(find.text('Sunil Perera'), findsOneWidget);
    expect(find.text('Farmer · farmer@agriguard.demo'), findsOneWidget);
    expect(find.text('Report a crop problem'), findsOneWidget);
  });

  testWidgets('sign out returns to the welcome screen', (tester) async {
    when(() => repository.logout(any())).thenAnswer((_) async {});
    await pumpApp(tester, storage: InMemoryTokenStorage(farmerSession()));

    await tester.tap(find.byTooltip('Sign out'));
    await tester.pumpAndSettle();

    expect(find.textContaining('Crop advice you can'), findsOneWidget);
    verify(() => repository.logout('refresh-1')).called(1);
  });
}
