// Mirrors the API's AuthController DTOs and UserRole enum (serialised as names).

export const Role = {
  Farmer: 'Farmer',
  FieldAgronomist: 'FieldAgronomist',
  AgroDealer: 'AgroDealer',
  CoopAdministrator: 'CoopAdministrator',
} as const

export type Role = (typeof Role)[keyof typeof Role]

export const roleLabels: Record<Role, string> = {
  Farmer: 'Farmer',
  FieldAgronomist: 'Field Agronomist',
  AgroDealer: 'Agro-Dealer',
  CoopAdministrator: 'Co-op Administrator',
}

export interface UserSummary {
  id: string
  email: string
  fullName: string
  role: Role
  districtId: string | null
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAtUtc: string
  refreshToken: string
  refreshTokenExpiresAtUtc: string
  user: UserSummary
}
