import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** Merges Tailwind classes so a caller's class can override a component's default. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
