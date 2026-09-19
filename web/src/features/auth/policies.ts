import { Role } from './types'

/**
 * Client-side mirror of AuthorizationSetup.cs — the same four policies, the same roles.
 *
 * This decides what the UI *shows* (navigation, disabled buttons with an explanation).
 * It is not a security boundary: the API re-checks every request against its own copy, and a
 * user who edits this file gets a 403, not access. Keep the two in sync when a policy changes.
 */
export const policies = {
  CanApprovePrescriptions: [Role.FieldAgronomist],
  ManagesInventory: [Role.AgroDealer],
  AdministersRules: [Role.CoopAdministrator],
  OwnsFarm: [Role.Farmer, Role.FieldAgronomist, Role.CoopAdministrator],
} as const satisfies Record<string, readonly Role[]>

export type Policy = keyof typeof policies

export function can(role: Role | null | undefined, policy: Policy): boolean {
  return role != null && (policies[policy] as readonly Role[]).includes(role)
}
