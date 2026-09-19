import 'package:agriguard_mobile/features/auth/auth_models.dart';

const farmer = UserSummary(
  id: '01a0aaaf-a87a-736a-a322-4f4782a9b595',
  email: 'farmer@agriguard.demo',
  fullName: 'Sunil Perera',
  role: UserRole.farmer,
  districtId: '0199e7c2-3b1e-7f0a-9c1d-0d5f5a1c2b3e',
);

AuthSession farmerSession({String suffix = '1', DateTime? refreshExpiresAt}) {
  final now = DateTime.now().toUtc();
  return AuthSession(
    accessToken: 'access-$suffix',
    accessTokenExpiresAt: now.add(const Duration(minutes: 15)),
    refreshToken: 'refresh-$suffix',
    refreshTokenExpiresAt: refreshExpiresAt ?? now.add(const Duration(days: 14)),
    user: farmer,
  );
}

/// The JSON shape the API returns from /api/auth/login and /refresh.
Map<String, dynamic> loginJson({String suffix = '1'}) => farmerSession(suffix: suffix).toJson();

Map<String, dynamic> problemJson(int status, String detail) => {
      'status': status,
      'title': 'Authentication failed',
      'detail': detail,
    };
