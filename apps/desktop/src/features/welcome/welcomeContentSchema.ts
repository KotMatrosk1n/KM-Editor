/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
const text = z.string().trim().min(1).max(16000);
const localizedText = z.object({ en: text, de: text.optional(), es: text.optional(), fr: text.optional(), ru: text.optional(), uk: text.optional(), zh: text.optional() }).strict();
const audience = z.array(z.enum(['all', 'swsh', 'sv', 'za', 'sword', 'shield', 'scarlet', 'violet'])).min(1).max(8);
const safeUrl = z.string().url().max(2048).refine(value => {
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && !url.username && !url.password && url.hostname === 'github.com' &&
      (url.pathname === '/KotMatrosk1n/KM-Editor' || url.pathname.startsWith('/KotMatrosk1n/KM-Editor/'));
  } catch {
    return false;
  }
});
const link = z.object({ label: localizedText, url: safeUrl }).strict();
const section = z.object({ title: localizedText, items: z.array(z.object({ body: localizedText, audience }).strict()).min(1).max(100) }).strict();
export const welcomeContentSchema = z.object({
  schemaVersion: z.literal(1),
  releases: z.array(z.object({ version: z.string().regex(/^\d+\.\d+\.\d+(?:-[\w.-]+)?$/), summary: localizedText,
    sections: z.array(section).max(20), links: z.array(link).max(8).default([]) }).strict()).max(100),
  announcements: z.array(z.object({ id: z.string().regex(/^[a-z0-9-]+$/).max(100), title: localizedText, body: localizedText,
    audience, published: z.string().date().optional(), featured: z.boolean().default(false), links: z.array(link).max(8).default([]) }).strict()).max(100)
}).strict().superRefine((value, context) => {
  for (const [label, ids] of [['version', value.releases.map(release => release.version)], ['id', value.announcements.map(item => item.id)]] as const) {
    if (new Set(ids).size !== ids.length) context.addIssue({ code: 'custom', message: `Duplicate ${label}.` });
  }
});
export type WelcomeContent = z.infer<typeof welcomeContentSchema>;
export type WelcomeText = z.infer<typeof localizedText>;
