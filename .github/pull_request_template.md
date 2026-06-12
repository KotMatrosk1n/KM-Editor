# Summary

Describe what changed in a few short bullets or sentences.

# Root Cause / Impact

For fixes, explain why the bug happened and who it affected.

For features, explain which KM Editor workflow improves and why it belongs in the editor.

# Validation

List the checks you ran.

Useful examples:

```powershell
pnpm --dir apps/desktop typecheck
pnpm --dir apps/desktop test:run
pnpm --dir apps/desktop build
dotnet test .\KM.Editor.slnx --no-restore
git diff --check
```

If the behavior depends on Sword or Shield runtime behavior, include what was tested in game.

# Safety and Data Ownership

Does this write, delete, cleanup, or uninstall files?

Does it touch base dump paths?

Does it preserve files and data KM Editor does not own?

If cleanup removes anything, how is ownership proven?

This PR should not include ROMs, NSPs, game dumps, private saves, tokens, personal data, release artifacts, or copyrighted assets.

# Docs and Wiki

Say whether README, wiki, release notes, or in app text need updates.

# Notes

Add anything reviewers should know, such as a known limitation, hidden workflow, follow up task, or UI screenshot.
