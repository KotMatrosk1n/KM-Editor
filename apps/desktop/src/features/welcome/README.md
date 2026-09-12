# Welcome content

Edit `content.json` to maintain the welcome hub without changing its React layout. It is bundled with the app and available offline. There are no startup network requests for this content. Updates reach users with the next application build.

Run `pnpm --filter @km-editor/desktop check:welcome` after editing. The same validation runs during the desktop typecheck and build. `welcomeContentSchema.ts` defines the format; invalid optional content falls back to empty content at runtime so project selection remains usable.

Before every release, finalize this content against the complete release changelog before packaging. Verify all five game selections, shared changes, version matching, links, and the developer card. Published GitHub notes cannot change the content already embedded in an installed build.

## Releases

Add a `releases` entry with an exact `version`, `summary`, and `sections`. The hub selects only the entry matching the installed application version reported by the native app, with Tauri configuration as the browser fallback. Missing versions display an empty state. Do not put unreleased changes in an older version's notes. Keep each entry's English summary, sections, items and comparison link identical to its matching document in `docs/release-notes/`. Preserve historical entries when adding a new release.

Each section has a `title` and `items`. Each item has a `body` and `audience`. Supported audiences are `all`, `swsh`, `sv`, `za`, `sword`, `shield`, `scarlet`, and `violet`. Use `all` for application wide changes; it remains visible under every game filter. Use multiple audiences for changes shared by selected families. Empty filtered sections are hidden. The complete-notes control expands all matching items inside the app.

## From the developer

The hub currently has only What's new and Getting started tabs. Developer notes still use the `announcements` content array, but there is no Announcements tab. Add entries with a unique `id`, `title`, `body`, and `audience`. Optional fields are `published` (YYYY-MM-DD), `featured` (defaults to false), and `links`. The first applicable featured entry supplies the developer note below the panel. Keep its body brief. Remove an entry when it should no longer appear. An empty array hides the developer card; entries without `featured` are not displayed.

```json
{
  "id": "project-update",
  "title": { "en": "Your announcement title" },
  "body": { "en": "Your announcement text." },
  "audience": ["all"],
  "published": "2026-09-06",
  "featured": true,
  "links": []
}
```

This is a format example, not bundled announcement content.

## Text and links

All editorial text uses an object with required `en` and optional `de`, `es`, `fr`, `ru`, `uk`, and `zh` translations. Missing translations show the author's English text. Interface controls remain localized separately through the normal language resources. Text renders literally; HTML and Markdown are not interpreted. Announcement paragraph breaks may use `\n`.

Optional `links` contain a localized `label` and a `url`. Only HTTPS links within the existing `github.com/KotMatrosk1n/KM-Editor` repository, wiki, issues, releases, and comparisons are accepted. Credentials, external hosts, and executable URL schemes are rejected. Links use the existing desktop external-browser action and never load content inside the hub.
