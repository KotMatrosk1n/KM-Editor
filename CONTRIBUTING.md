# Contributing to KM Editor

## Welcome

KM Editor is a desktop app and toolchain for editing Pokemon Sword and Shield mod projects. Contributions can be code, tests, docs, wiki fixes, bug reports, release checks, UX feedback, or verified notes about in game behavior.

Good contributions make the editor safer, clearer, or more useful for people working with real mod projects.

## Before opening an issue

Please check whether a similar issue already exists. If you open a new one, include the details that would help someone reproduce the problem.

Useful details include the KM Editor version, Sword or Shield, the editor page or workflow involved, what you expected, what happened, and any diagnostics KM Editor showed.

Screenshots are welcome when they explain the problem. Do not upload ROMs, NSPs, game dumps, copyrighted assets, private saves, tokens, or personal information.

## Bug reports

For bugs, please say where the failure happened.

Common stages are load, edit, stage, review, save, cleanup, uninstall, and launch in game.

If generated LayeredFS output changed unexpectedly, say which kind of output changed and what you expected KM Editor to preserve. File names are useful. Copyrighted game files are not.

## In game behavior reports

Sometimes KM Editor saves a change successfully, but Sword or Shield does something unexpected. Those reports are useful.

Please describe what you edited in KM Editor, what changed in game, what did not change in game, and where the player sees the behavior.

If it matters, include whether this was a new save or an existing save, and whether you verified that the generated mod folder was loaded.

## Feature requests

Start with the user goal. What are you trying to change in game, where does the player see it, and why should KM Editor support it?

Known files, offsets, references, screenshots, or examples from other tools can help. Keep the request focused on behavior and workflow, not unrelated community disputes.

Good feature requests explain how KM Editor can make the task safer or easier than manual editing.

## Pull requests

Keep changes scoped. A small clear pull request is easier to review than a large change that mixes unrelated work.

For fixes, explain the root cause and the user impact. For features, explain the workflow being improved.

Backend edit or apply behavior should have relevant .NET tests. Visible desktop workflow changes should have desktop tests. Broad changes should run broader checks.

Do not commit local paths, private handoff notes, generated dumps, game files, release artifacts, private saves, tokens, or copyrighted assets.

If behavior changes, update docs or the wiki when needed.

## Development setup

KM Editor uses .NET, Node with pnpm, and Tauri for the desktop app.

Common validation commands:

```powershell
dotnet test .\KM.Editor.slnx --no-restore
pnpm --dir apps/desktop typecheck
pnpm --dir apps/desktop test:run
pnpm --dir apps/desktop build
git diff --check
```

Use focused tests while developing, then broaden validation when the change touches shared behavior, file writes, cleanup, or user visible workflows.

## Modding safety

Base dump paths are read only. Output folders are where generated files belong.

Cleanup and uninstall code must remove only data KM Editor wrote or can prove it owns. Avoid deleting whole LayeredFS files unless ownership is clear.

If a workflow edits shared files, explain how it preserves unrelated user edits and other KM Editor workflows.

Clear notes and tests make future maintenance easier.
