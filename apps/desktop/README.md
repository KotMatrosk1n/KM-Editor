# Desktop App

The KM Editor desktop frontend shell lives here.

## Stack

- React
- TypeScript
- Vite
- Zustand
- TanStack Virtual
- Zod
- lucide-react
- Tauri 2

## Commands

Run these from the repository root:

```powershell
pnpm install
pnpm dev
pnpm typecheck
pnpm build
pnpm check
pnpm sidecar:publish
pnpm tauri:dev
pnpm tauri:build
```

Use `pnpm check` to run workspace hygiene checks and compile both the desktop and backend projects.

## KM interface contract

The shared styles in `src/styles.css` are the default visual authority for every editor and tool.
New UI must look and behave like part of KM Editor without requiring a feature-specific patch to
hide browser or operating-system defaults.

When adding or changing desktop UI:

- Use semantic `button`, `input`, `select`, and `textarea` elements and let the shared low-specificity
  baseline provide KM colors, borders, radii, typography, pointer feedback, and interaction states.
- Use the existing `--color-*`, sizing, spacing, shadow, and focus tokens. Do not place inline style
  on a native control and do not hardcode a control color in a feature stylesheet.
- Use `background-color` for field surface refinements. Do not use the `background` shorthand on a
  field because it can erase the shared select arrow, search affordance, or other background layer.
- Preserve normal, hover, active, keyboard-focus, busy, disabled, and read-only behavior. A custom
  proxy control with an opacity-zero native input must visibly mirror every relevant native state.
- Treat browser-owned parts as part of the control. Select arrows and options, search clear
  buttons, number spinners, date and datalist indicators, range tracks and thumbs, color swatches,
  file buttons, textarea resize handles, placeholders, and autofill must remain KM themed.
- Keep forced-colors behavior intact. Windows system colors and restored native affordances are
  intentional only while forced-colors mode is active for accessibility.
- If a new input type or proxy-control pattern is genuinely required, extend the shared theme and
  `scripts/check-control-theme.mjs` in the same change. Do not add a local exception that bypasses
  the contract.

Run `pnpm typecheck` from the repository root before handing off any desktop UI change. The command
audits every desktop TSX and CSS source and rejects inline native-control styling, unsupported or
dynamic input types, platform appearance outside forced-colors mode, hardcoded field colors,
field background shorthands, erased select arrows, and missing shared or proxy-control states.

Tauri dev and build commands publish `src/KM.Tools` as a self-contained sidecar before launching or packaging the desktop app. To refresh only the sidecar, run `pnpm sidecar:publish`; the generated executable is staged under `apps/desktop/src-tauri/binaries/` and is intentionally not committed.

Tauri builds on Windows require Visual Studio Build Tools with the Microsoft C++ toolchain and Windows SDK components available to the Rust MSVC target.

When building from a protected or synced workspace, use a local writable Cargo target cache to avoid generated Rust build artifacts inheriting restrictive workspace ACLs:

```powershell
$env:CARGO_TARGET_DIR = "$env:LOCALAPPDATA\Temp\km-editor-tauri-target"
pnpm tauri:build
```

The Tauri build compiles the unbundled desktop executable. Windows install, update, and uninstall packages are produced only by the custom setup driver under `installer/windows/`, which combines that executable with the published `km-tools-bridge.exe` sidecar.

The desktop app should consume typed contracts from `src/KM.Api` through the chosen local bridge rather than binding directly to backend storage or binary model types.
