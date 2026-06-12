# KM Editor Security Policy

## Supported versions

Security fixes target the latest public KM Editor release.

Please update to the newest version before reporting old behavior unless you can also reproduce the issue on the latest release.

Older releases are not guaranteed to receive backports.

## What counts as security

Please treat the following as security issues:

1. Release installer, updater, or download tampering.
2. Unsafe update behavior.
3. Path traversal or writing outside the selected output folder.
4. Deleting or modifying base dump files.
5. Cleanup or uninstall removing files KM Editor did not create or cannot prove it owns.
6. Leaking local paths, private files, save data, tokens, or sensitive diagnostics.
7. Malformed project data causing file corruption.
8. Anything that could trick users into sharing copyrighted or private files.

## What is usually not security

Normal crashes, wrong Pokemon data, incorrect item behavior, bad trainer edits, encounter mistakes, UI glitches, missing docs, and gameplay balance bugs are usually regular bugs.

They still matter. Please report them with the normal issue templates.

## Reporting

Use GitHub private vulnerability reporting when available.

If private vulnerability reporting is not available, open a minimal public issue that says a security report is needed, without posting exploit details. You can also contact a maintainer through their public GitHub profile.

Please include the KM Editor version, operating system, affected area, safe reproduction steps, and what files or user data could be affected.

Do not attach ROMs, NSPs, game dumps, private saves, tokens, personal data, or copyrighted assets.

## Handling

Security reports are reviewed based on risk and user impact. Public details may wait until a fix is released.

Please do not exploit the issue against other users or pressure people to share private files while testing.
