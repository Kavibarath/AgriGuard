/// Mirrors the API's AuthController DTOs. Roles are serialised as names.
enum UserRole {
  farmer('Farmer', 'Farmer'),
  fieldAgronomist('FieldAgronomist', 'Field Agronomist'),
  agroDealer('AgroDealer', 'Agro-Dealer'),
  coopAdministrator('CoopAdministrator', 'Co-op Administrator');

  const UserRole(this.wireName, this.label);

  /// The exact string the API sends and the `role` claim carries.
  final String wireName;
  final String label;

  static UserRole fromWire(String value) => values.firstWhere(
        (r) => r.wireName == value,
        orElse: () => throw FormatException('Unknown role "$value"'),
      );
}

class UserSummary {
  const UserSummary({
    required this.id,
    required this.email,
    required this.fullName,
    required this.role,
    this.districtId,
  });

  factory UserSummary.fromJson(Map<String, dynamic> json) => UserSummary(
        id: json['id'] as String,
        email: json['email'] as String,
        fullName: json['fullName'] as String,
        role: UserRole.fromWire(json['role'] as String),
        districtId: json['districtId'] as String?,
      );

  final String id;
  final String email;
  final String fullName;
  final UserRole role;
  final String? districtId;

  Map<String, dynamic> toJson() => {
        'id': id,
        'email': email,
        'fullName': fullName,
        'role': role.wireName,
        'districtId': districtId,
      };
}

/// A signed-in session: the tokens plus who they belong to. Persisted whole, so the app can
/// show the user's name on launch without a network round trip.
class AuthSession {
  const AuthSession({
    required this.accessToken,
    required this.accessTokenExpiresAt,
    required this.refreshToken,
    required this.refreshTokenExpiresAt,
    required this.user,
  });

  factory AuthSession.fromJson(Map<String, dynamic> json) => AuthSession(
        accessToken: json['accessToken'] as String,
        accessTokenExpiresAt: DateTime.parse(json['accessTokenExpiresAtUtc'] as String),
        refreshToken: json['refreshToken'] as String,
        refreshTokenExpiresAt: DateTime.parse(json['refreshTokenExpiresAtUtc'] as String),
        user: UserSummary.fromJson(json['user'] as Map<String, dynamic>),
      );

  final String accessToken;
  final DateTime accessTokenExpiresAt;
  final String refreshToken;
  final DateTime refreshTokenExpiresAt;
  final UserSummary user;

  Map<String, dynamic> toJson() => {
        'accessToken': accessToken,
        'accessTokenExpiresAtUtc': accessTokenExpiresAt.toUtc().toIso8601String(),
        'refreshToken': refreshToken,
        'refreshTokenExpiresAtUtc': refreshTokenExpiresAt.toUtc().toIso8601String(),
        'user': user.toJson(),
      };
}
