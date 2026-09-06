# Welcome content

Edit `content.json` to maintain the welcome hub without changing its React layout. It is bundled with the app and available offline. There are no startup network requests for this content. Updates reach users with the next application build.

Run `pnpm --filter @km-editor/desktop check:welcome` after editing. The same validation runs during the desktop typecheck and build. `welcomeContentSchema.ts` defines the format; invalid optional content falls back to empty content at runtime so project selection remains usable.

## Releases

Add a `releases` entry with an exact `version`, `summary`, and `sections`. The hub selects only the entry matching the installed application version reported by the native app, with Tauri configuration as the browser fallback. Missing versions display an empty state. Do not put unreleased changes in an older version's notes. The bundled 2.5.9 content comes from that version's published GitHub release.

Each section has a `title` and `items`. Each item has a `body` and `audience`. Supported audiences are `all`, `swsh`, `sv`, `za`, `sword`, `shield`, `scarlet`, and `violet`. Use `all` for application-wide changes; it remains visible under every game filter. Use multiple audiences for changes shared by selected families. Empty filtered sections are hidden. The complete-notes control expands all matching items inside the app.

## Announcements

Add entries to `announcements` with a unique `id`, `title`, `body`, and `audience`. Optional fields are `published` (YYYY-MM-DD), `featured` (defaults to false), and `links`. Announcements display in file order. The first applicable featured announcement also supplies the developer note below the panel. Keep its body brief. Remove an announcement from the array when it should no longer appear. An empty array shows the announcement empty state and hides the developer card.

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
