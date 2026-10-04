import { cn } from '@/lib/utils'
import { agentIdentity } from './agents'
import type { AgentRole } from './types'

/** The agent's glyph on its tile. Decorative: always used next to the agent's name. */
export function AgentGlyph({ role, size = 'md', className }: { role: AgentRole; size?: 'sm' | 'md' | 'lg'; className?: string }) {
  const { glyph: Glyph, ink, tile } = agentIdentity[role]
  const box = size === 'sm' ? 'size-5 rounded' : size === 'lg' ? 'size-9 rounded-lg' : 'size-7 rounded-md'
  return (
    <span aria-hidden="true" className={cn('inline-flex shrink-0 items-center justify-center ring-1 ring-inset', box, tile, ink, className)}>
      <Glyph size={size === 'sm' ? 14 : size === 'lg' ? 20 : 16} />
    </span>
  )
}

/** Glyph and name together: how an agent is named anywhere in the console. */
export function AgentName({ role, size = 'md', className }: { role: AgentRole; size?: 'sm' | 'md'; className?: string }) {
  return (
    <span className={cn('inline-flex items-center gap-1.5 font-semibold', agentIdentity[role].ink, className)}>
      <AgentGlyph role={role} size={size === 'sm' ? 'sm' : 'md'} />
      {role}
    </span>
  )
}
