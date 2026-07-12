# KM Editor Security Policy

## Supported versions

Security fixes target the latest public KM Editor release. Older releases are not guaranteed to receive backports.

Before reporting an issue from an older version, update to the latest release and check whether the behavior still occurs. If updating is unsafe because of the suspected vulnerability, say so in the private report.

## Report security issues privately

Use [GitHub Private Vulnerability Reporting](https://github.com/KotMatrosk1n/KM-Editor/security/advisories/new) for suspected vulnerabilities. Please do not open a public issue, discussion, or pull request containing exploit details.

If private reporting is unavailable, open a detail-free public issue stating only that you need a private security contact. Do not include the vulnerability, affected files, or reproduction details there.

## What belongs in a security report

Security issues are problems that cross a trust, privacy, or ownership boundary. Examples include:

* Installer, updater, signature, or release download tampering.
* Path traversal or writes outside the output location chosen by the user.
* Modification of base project files that KM Editor promises to treat as read only.
* Cleanup or uninstall deleting files KM Editor does not own.
* Unintended command execution or unsafe handling of untrusted project data.
* Exposure of credentials, signing material, private files, personal information, or sensitive local paths.
* A dependency vulnerability that is reachable through KM Editor.

File corruption is a security concern when it crosses one of those boundaries or can destroy unrelated user data. Incorrect generated values limited to KM Editor's own output are generally correctness bugs, even when they can crash the game.

## What is normally a regular bug

Crashes, incorrect Pokemon data, gameplay behavior, unsupported mod combinations, editor display problems, slow operations, missing features, and documentation errors usually belong in the normal issue templates.

When in doubt, report privately. The maintainer can move a report to the normal bug process without exposing sensitive details.

## What to include

A useful security report contains:

* The affected KM Editor version, or commit when testing source.
* The affected component and the security impact.
* Safe, minimal reproduction steps.
* The files, locations, or user data that could be affected.
* Any conditions required for exploitation.
* A suggested fix or mitigation, if you have one.

Use synthetic examples wherever possible. Do not attach game dumps, executable game files, copyrighted assets, private saves, credentials, signing keys, access tokens, or personal information. Redact local account names and unrelated path details from logs and screenshots.

## Coordinated handling

The maintainer will confirm the report, assess impact and affected versions, and coordinate a fix and disclosure when appropriate. Public details may wait until users have a safe release available.

Please allow reasonable time for investigation before public disclosure. Do not test against another person's files or system, maintain access after proving the issue, disrupt release services, or pressure users to share private data.

KM Editor does not promise a bug bounty or payment for security research unless the maintainer explicitly offers one in writing.
