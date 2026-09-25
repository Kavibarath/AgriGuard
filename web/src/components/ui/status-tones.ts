import type { Tone } from './StatusBadge'

/** Crop stages read as a progression: growing → nearly ready → finished. */
export const stageTone: Record<string, Tone> = {
  Sown: 'neutral',
  Vegetative: 'active',
  Flowering: 'active',
  FruitSet: 'active',
  PreHarvest: 'warning',
  Harvested: 'done',
}

export const plotStatusTone: Record<string, Tone> = {
  Active: 'active',
  Fallow: 'neutral',
  Retired: 'danger',
}
