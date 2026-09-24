# Package manifests

A mod archive or folder can include an optional `km-mod.json` file describing its components. Ordinary mods do not need one. The manifest supplies installation choices; it cannot run commands, download files or modify game data itself.

Place the manifest beside the component folders. Paths are relative to that manifest. Each package identifies a folder, and its `romfs` and `exefs` children belong to that one package. For a direct virtual RomFS payload without those folders, set `layout` to `trinity`.

```json
{
  "version": 1,
  "groups": [
    { "id": "difficulty", "name": "Difficulty", "required": true }
  ],
  "packages": [
    { "id": "core", "path": "Core", "name": "Main mod", "required": true, "game": "za" },
    { "id": "shops", "path": "Optional Shops", "name": "Extra shop items", "requires": ["core"] },
    { "id": "standard", "path": "Standard", "group": "difficulty", "recommended": true },
    { "id": "hard", "path": "Hard", "group": "difficulty", "requires": ["core"] }
  ]
}
```

| Package field | Meaning |
| --- | --- |
| `id` | Required unique identifier using 1 to 80 ASCII letters, digits, underscores or hyphens. |
| `path` | Required folder path relative to the manifest. `.` selects its containing folder. Absolute paths and parent traversal are rejected. |
| `name` | Optional display name, up to 160 characters. Otherwise the package path is shown. |
| `description` | Optional plain text explanation, up to 4,000 characters. |
| `game` | Optional `sword`, `shield`, `scarlet`, `violet` or `za`. Contradictions with detected game evidence are rejected. |
| `layout` | Optional `standalone`, `trinity`, `bypass` or `independent`. An explicit user layout override takes precedence. |
| `required` | Prevents removing this package or its dependencies. Defaults to `false`. |
| `recommended` | Offers the author's default selection. Defaults to `false`. It does not make a package required. |
| `requires` | Optional list of package IDs from the same manifest. Selecting a package selects its dependencies. |
| `group` | Optional ID of an alternative group declared in this manifest. At most one member can be selected. |

Groups have a required `id`, optional `name` and optional `required` flag. A required group needs exactly one selected member. An optional group also offers **None**. Packages can require a specific alternative by listing its package ID in `requires`.

If multiple alternatives in one group are recommended, no default is inferred for that group. Required packages take precedence over recommendations. A later explicit selection can replace an alternative and deselect packages whose dependencies are no longer present. The dialog reports these adjustments before confirmation.

Declared package roots must be unique and contain payload files. When roots are nested, the most specific declaration owns its files. Undeclared components can still be discovered from their installation structure. Declarations inside nested archives apply only to their own scope. Cycles, missing dependencies, unknown properties, duplicate property names and incompatible required alternatives are rejected. Manifests are limited to 256 KiB, 64 package declarations and 64 groups; the entire source still has a limit of 64 discovered packages.

Package selection does not override conflict review. Selecting two components with different values for the same game field still requires the normal Basic or Advanced merge rules. Recommended selections describe author intent, not a guarantee of gameplay compatibility.
