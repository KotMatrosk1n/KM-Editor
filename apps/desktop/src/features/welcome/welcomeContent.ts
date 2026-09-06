/* SPDX-License-Identifier: GPL-3.0-only */
import bundledContent from './content.json';
import type { ProjectGame } from '../../bridge/contracts';

import { welcomeContentSchema, type WelcomeContent, type WelcomeText } from './welcomeContentSchema';
export { welcomeContentSchema, type WelcomeContent, type WelcomeText } from './welcomeContentSchema';
export function contentText(value: WelcomeText, language: string): string {
  return value[language as keyof WelcomeText] ?? value.en;
}
export function gameFamily(game: ProjectGame): 'swsh' | 'sv' | 'za' {
  return game === 'sword' || game === 'shield' ? 'swsh' : game === 'za' ? 'za' : 'sv';
}
export function appliesToGame(audiences: readonly string[], game: ProjectGame): boolean {
  return audiences.includes('all') || audiences.includes(game) || audiences.includes(gameFamily(game));
}
// Invalid optional editorial content must never prevent opening a project.
const parsed = welcomeContentSchema.safeParse(bundledContent);
export const welcomeContent: WelcomeContent = parsed.success ? parsed.data : { schemaVersion: 1, releases: [], announcements: [] };
export const lastWelcomeGameKey = 'km-editor.welcome.game.v1';
export function readWelcomeGame(): ProjectGame {
  try {
    const saved = localStorage.getItem(lastWelcomeGameKey);
    if (saved === 'sword' || saved === 'shield' || saved === 'scarlet' || saved === 'violet' || saved === 'za') return saved;
  } catch { /* Selection remains usable without browser storage. */ }
  return 'sword';
}
export function rememberWelcomeGame(game: ProjectGame): void {
  try { localStorage.setItem(lastWelcomeGameKey, game); } catch { /* Session selection still works. */ }
}
