# KM Native Gameplay Menu Runtime

This directory contains the independently authored guest runtime used by KM's native in-game
settings menus. It is a normal game-title `subsdk9` module, not a cheat file, cheat-manager
integration, external overlay, or host DLL.

The runtime supports only these exact game updates:

| Game | Update | Game-owned menu location |
| --- | --- | --- |
| Pokemon Sword and Shield | 1.3.2 | KM settings entry in the stock Pokemon Center main list |
| Pokemon Scarlet and Violet | 4.0.0 | Three rows in the existing Options screen |
| Pokemon Legends Z-A | 2.0.2 | Three rows in the existing Game Settings page |

Each menu exposes Experience Share On or Off, Experience Rate from 0 through 500 percent in
10 percent steps, and Supported EXP Level Cap Off or 1 through 100. The controls retain the fixed
editor's verified gameplay scopes and do not claim control over unlisted EXP sources.

The runtime is deliberately fail closed. During the loader-serialized `subsdk9` entry, it resolves
the required guest filesystem APIs, recognizes one exact supported executable profile, verifies
every patch preimage, and installs that title's complete immutable hook set in one transaction.
Executable code is never patched again during that process. If the KM-owned dual-slot settings
journal is not readable yet, the installed hooks retain retail-equivalent values while a sleeping
worker retries journal loading without a timeout. Later menu changes commit and verify the journal,
then publish one atomic data snapshot. A missing, corrupt, foreign, or mismatched dependency leaves
retail behavior active and never produces a partial live-code transition.

The startup patch transaction validates all owned bytes, writes through bounded aliases, flushes
and invalidates the required caches, verifies the committed bytes, and rolls back on verification
failure. The derived `main.npdm` grants only SD access and the exact process-handle and memory-alias
system calls needed by that transaction in both policy views. The journal uses alternating slots,
generations, title and family binding, CRC32C, flush, and readback verification so an interrupted
write cannot replace the last valid state. An ambiguous write is re-read from both authenticated
slots; the runtime publishes the durable winner or falls back to retail for the rest of the process.

`build.ps1` compiles a bounded AArch64 ELF. KM's managed build tooling converts that ELF to a
canonical compressed NSO and verifies every segment on readback. KM combines that independently
authored runtime with executable and menu assets derived from the user's clean Base ExeFS and Base
RomFS. Retail game files are not stored in this directory or embedded in the runtime.

The native path remains Beta until menu, gameplay, persistence, suspend and resume, restart,
upgrade, removal, emulator, and physical-hardware canaries pass for every supported title and
edition pair.
