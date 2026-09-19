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

  Future<void> signIn(WidgetTester tester, String email, String password) async {
    await tester.enterText(find.widgetWithText(TextFormField, 'Email'), email);
    await tester.enterText(find.widgetWithText(TextFormField, 'Password'), password);
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();
  }

  testWidgets('starts on the login screen when signed out', (tester) async {
    await pumpApp(tester);

    expect(find.text('Sign in to your farm advisory'), findsOneWidget);
  });

  testWidgets('goes straight home when a session is stored', (tester) async {
    await pumpApp(tester, storage: InMemoryTokenStorage(farmerSession()));

    expect(find.text('Sunil Perera'), findsOneWidget);
    verifyZeroInteractions(repository);
  });

  testWidgets('validates before calling the API', (tester) async {
    await pumpApp(tester);

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

    await signIn(tester, farmer.email, 'wrong');

    expect(find.text('Invalid credentials.'), findsOneWidget);
    expect(find.text('Sign in to your farm advisory'), findsOneWidget);
  });

  testWidgets('navigates home after a successful login', (tester) async {
    when(() => repository.login(farmer.email, 'AgriGuard!Demo1'))
        .thenAnswer((_) async => farmerSession());
    await pumpApp(tester);

    await signIn(tester, farmer.email, 'AgriGuard!Demo1');

    expect(find.text('Sunil Perera'), findsOneWidget);
    expect(find.text('Farmer · farmer@agriguard.demo'), findsOneWidget);
    expect(find.text('Report a crop problem'), findsOneWidget);
  });

  testWidgets('sign out returns to the login screen', (tester) async {
    when(() => repository.logout(any())).thenAnswer((_) async {});
    await pumpApp(tester, storage: InMemoryTokenStorage(farmerSession()));

    await tester.tap(find.byTooltip('Sign out'));
    await tester.pumpAndSettle();

    expect(find.text('Sign in to your farm advisory'), findsOneWidget);
    verify(() => repository.logout('refresh-1')).called(1);
  });
}
