<!-- SPDX-License-Identifier: GPL-3.0-only -->

# KM Setup compatibility launcher

`KM.Setup.Launcher` is an invisible, native x64 Windows entry point for the KM
Editor Burn bundle. It exists only to bridge Windows updater command
lines emitted by already-released Tauri clients. It is intentionally not part
of the root solution and is not built by ordinary application or solution
builds or by the pull-request workflow. It is built only as the final stage of
an explicit `Build-KmWindowsSetup.ps1` packaging run, including eligible
`Desktop Release` runs; it has no independent package or release trigger of
its own. Both configurations use the static Visual C++ runtime (`/MTd` for
Debug and `/MT` for Release), so a clean machine does not need the VC++
redistributable merely to start setup.

## Packaging contract

The project has no default inner payload. A packaging invocation must set
`KmInnerBundlePath` to an already-built x64 Burn EXE. The project computes that
exact file's SHA-256 digest, generates a private digest header, and embeds the
same file as `RCDATA`.

Release packaging must also supply `KmVersion` as three or four numeric
components. Each component must fit Windows' unsigned 16-bit VERSIONINFO field.
The tracked `apps/desktop/src-tauri/icons/icon.ico` is the default
`KmLauncherIconPath`; packaging may override that property with another
validated `.ico`. The generated resources identify the outer executable as
`KM Editor` / `KM Editor Setup` in Explorer and the UAC program-name field. The
launcher remains `asInvoker`; the Burn engine owns any required elevation.

At runtime the launcher:

1. parses its complete command line with `CommandLineToArgvW`;
2. reconstructs only reviewed Burn switches and KM bridge variables;
3. verifies the embedded resource's PE architecture, build-pinned SHA-256
   digest, and extracted-file digest; an unsigned payload is accepted, while
   any payload containing an Authenticode signature must pass offline trust
   validation;
4. writes it with `CREATE_NEW` into a 128-bit-random directory beneath the
   current user's temporary directory;
5. launches it with an explicit application path and Windows-correct argument
   quoting, waits, and forwards its exact exit code; and
6. best-effort deletes only that exact file and directory.

No recursive cleanup, search-based deletion, inherited handles, shell command,
or user-provided executable path is used.

## Accepted input

Legacy Tauri switches are case-insensitive:

- `/P` becomes Burn passive display;
- `/S` becomes Burn quiet display;
- `/R` sets `KMAutoLaunch=1`;
- `/UPDATE` sets `KMUpdateMode=1`; and
- `/ARGS` terminates installer parsing permanently.

Every token after `/ARGS` is opaque application input. It is never compared to
an installer switch and never copied onto Burn's command line in clear form.
Tauri updater `installerArgs` must remain empty because Tauri appends them after
`/ARGS`, where they are intentionally indistinguishable from application argv.

For direct fresh-install and maintenance use, a leading `/` or `-` is accepted
for this strict allowlist only:

```text
install  modify  repair  uninstall
passive  quiet
norestart  promptrestart  forcerestart
log <path>
```

Conflicting actions, display modes, or restart modes fail with
`ERROR_INVALID_PARAMETER`. `/UPDATE`, `/R`, and `/ARGS` are valid only for an
install action. Arbitrary `Name=Value` input, `-burn.*`, layout, unsafe
uninstall, and unknown future switches are rejected rather than forwarded.

`/R` means relaunch KM Editor after a successful update; it never grants
permission to reboot Windows. When `/UPDATE` is present and no explicit restart
mode was supplied, the launcher adds `-norestart` before starting Burn. Direct
callers may select `norestart`, `promptrestart`, or `forcerestart`; setup
forwards the standard `3010` restart-required and `1641` restart-initiated exit
codes without translating them into generic failure.

## Pre-bootstrap failure UX

The launcher shows one concise `KM Editor Setup` error dialog, including the
unchanged hexadecimal exit code, only when it cannot:

- parse or serialize the updater invocation;
- verify or extract the embedded Burn payload; or
- create or monitor the Burn process.

The launcher still returns the same deterministic failure code after the user
closes the dialog. Once Burn starts and returns normally, Burn owns cancellation
and error presentation: every child exit code, including a nonzero code, is
forwarded without a second launcher dialog.

The launcher creates only these WiX overridable-variable assignments. They are
intentionally non-switch `Name=Value` tokens, as required by WiX v7's command
parser:

```text
KMInvocationBridged=1
KMUpdateMode=0|1
KMAutoLaunch=0|1
KMLaunchArgumentsBase64=<encoded envelope>
```

`KMLaunchArgumentsBase64` must remain a non-persisted hidden Burn variable.
The other three variables contain intent flags only. The project caps opaque
application input at 256 arguments and 16 KiB serialized, and caps the final
child command line at 30,000 UTF-16 code units.

## Relaunch argument envelope

`KMLaunchArgumentsBase64` uses standard padded RFC 4648 Base64 over this binary
format:

```text
offset  size  meaning
0       4     ASCII "KMAR" (4B 4D 41 52)
4       4     uint32 little-endian format version (1)
8       4     uint32 little-endian argument count
12      ...   repeated argument records:
               uint32 little-endian UTF-8 byte length
               exactly that many strict UTF-8 bytes
```

There are no NUL terminators and no trailing bytes. A zero-byte record
represents an empty argument. The decoder must reject the wrong magic/version,
invalid UTF-8, embedded U+0000, count or length overflow, truncation, and
trailing data. Base64 makes slash-prefixed arguments, quotes, spaces, and
strings resembling Burn variables a single inert variable value until the BA
decodes them.

## Quoting and trust notes

The launcher does not concatenate the original command line. It parses first,
then emits a new command line with the standard Windows backslash-before-quote
rules and supplies `lpApplicationName` explicitly to `CreateProcessW`.
Application argv is length-delimited before Base64 encoding, so no quoting
round-trip is needed for relaunch.

The SHA-256 pin protects the resource-to-temp extraction boundary. A genuinely
unsigned payload is accepted, but a present Authenticode signature must be
structurally valid and trusted. Release packaging still Minisign-signs the final
distributed launcher bytes for Tauri updater verification.
