## UI Toolkit: leading-icon text
Never put an emoji/symbol inline at the start of a `Button.text` (or a lone `Label`) — on Windows the fallback glyph draws wider than it measures and the following text overlaps the icon. Use `BuildPipelineUIStyle.ApplyIconText(button, text)` (and `CreateIconLabel` for title labels) so the icon gets its own reserved box. This is the only supported way to show an icon before a label.

## Single source of truth (2.0)
- `ProjectBuildConfig` is the only source of build settings. Build contexts are created **only** by `BuildContextFactory.Create` (editor, Steam, CLI); never `new BuildContext { ... }` with hand-copied profile fields.
- Defines: `DefineStackResolver` (pure) / `DefineEnvironment.StackFor` (editor) are the only resolution path. The build step, the window, the confirmation dialog, the manifest and the catalog all use it. `BuildContext.ScriptingDefines` holds only the CLI/hook extras — the step applies the profile's presets + `scriptingDefines` itself.
- Language: `BuildLanguageResolver` (one-off override > profile lock > project default). The CLI sends `-language` only as an override.
- New `ProjectBuildConfig`/`PublisherProfile` fields that change old data need a step in `ProjectBuildConfigMigrator` (bump `CurrentSchema`). `schemaVersion` must keep initializer 0 so assets that predate it are detected.
- A package documents its defines in its own `package.json` → `"wagenheimer": { "defines": [...] }` (schema in README ▸ Defines). Never hard-code another package's symbols here.
- New window content is a section (`BuildPipelineSections` + `BuildPipelineWindow.CreateSection`), not a new `EditorWindow`. `WindowSectionsSmokeTests` builds every section; keep it green.
- Validate without opening Unity: compile with the harness (netstandard2.1 + Unity 6 managed DLLs), then run the EditMode suite in a throwaway project that references this package with `file:`.
