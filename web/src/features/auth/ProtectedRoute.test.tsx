import { screen } from '@testing-library/react'
import { renderApp } from '@/test/render'
import { agronomist, authResponse, farmer } from '@/test/msw'
import { useAuthStore } from './auth-store'
import { can, policies } from './policies'

describe('ProtectedRoute', () => {
  it('sends anonymous users to the login page', async () => {
    const { router } = renderApp('/dashboard')

    expect(await screen.findByLabelText('Email')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
  })

  it('renders the protected page for a signed-in user', async () => {
    useAuthStore.getState().setSession(authResponse(farmer))

    renderApp('/dashboard')

    expect(await screen.findByText('Sunil Perera')).toBeInTheDocument()
  })

  it('shows navigation only for areas the role is admitted to', async () => {
    useAuthStore.getState().setSession(authResponse(farmer))

    renderApp('/dashboard')

    expect(await screen.findByRole('link', { name: /farms & plots/i })).toBeInTheDocument()
    // Farmers cannot approve, manage stock or edit rules — the cards must not even be offered.
    expect(screen.queryByRole('link', { name: /agent runs/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /inventory/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /regulatory rules/i })).not.toBeInTheDocument()
  })

  it('offers the approval console to an agronomist', async () => {
    useAuthStore.getState().setSession(authResponse(agronomist))

    renderApp('/dashboard')

    expect(await screen.findByRole('link', { name: /agent runs/i })).toBeInTheDocument()
  })
})

describe('policies (mirror of AuthorizationSetup.cs)', () => {
  it.each([
    ['CanApprovePrescriptions', ['FieldAgronomist']],
    ['ManagesInventory', ['AgroDealer']],
    ['AdministersRules', ['CoopAdministrator']],
    ['OwnsFarm', ['Farmer', 'FieldAgronomist', 'CoopAdministrator']],
  ] as const)('%s admits exactly %j', (policy, roles) => {
    expect([...policies[policy]]).toEqual(roles)
    expect(can('AgroDealer', 'CanApprovePrescriptions')).toBe(false)
    expect(can(undefined, policy)).toBe(false)
  })
})
