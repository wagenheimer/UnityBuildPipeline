# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-09-08

### Added

- **Machine-readable build manifest** — `-manifest <path>` writes a JSON summary
  (`success`, `platform`, `profileId`, `defines`, `version`, `buildNumber`, `artifactPath`,
  `xcodeProjectPath`, `sizeBytes`, `errors`, `warnings`, `durationSeconds`). `BuildMatrix`
  writes an array of entries. Intended for external CI (AppDeployHub.Forge) instead of log scraping.
- **Stable profile ids** — `PublisherProfile.id` + `-buildProfile <id>` / `-matrixProfiles "a,b,c"`
  so CI can select a profile without depending on the `Publisher` enum. `EffectiveId` falls back to
  the publisher name when `id` is empty.
- **Compile-time build variants** — `PublisherProfile.scriptingDefines` + `-defines "A;B;C"` applied
  by the new `ApplyScriptingDefinesStep` and **restored** afterwards (also on build failure), so CI
  never leaves `ProjectSettings` dirty. Enables real Free/Full/Demo via `#if`.
- **WebGL platform** — `PlatformType.WebGL` with target/target-group mapping and output-path
  resolution; `PlatformTypeExtensions.IsGenericArtifact()` marks folder/site artifacts (no store upload).
- **`Tools/build.sh`** — macOS/Linux headless runner that resolves the Editor from
  `ProjectVersion.txt` (Unity Hub layout), mirroring `Tools/build.ps1`.
- **`-keystorePath` / `-keystoreAlias`** CLI args for the Android keystore (forces `LocalDisk` source);
  passwords still come from `ANDROID_KEYSTORE_PASS` / `ANDROID_KEYALIAS_PASS`. Lets CI inject a central keystore.
- **ProjectSettings stays pristine on CI** — `ApplyPlayerSettingsStep` now snapshots and restores
  `Android.bundleVersionCode` / `iOS.buildNumber` / `macOS.buildNumber` in its post step (also on build failure).

### Fixed

- `package.json` had a duplicate `version` key.
- Update checker no longer phones home in batchmode / when `CI` (or `BUILD_PIPELINE_NO_UPDATE_CHECK`) is set.

## [1.0.0] - 2026-09-07

### Added
- Initial release of `com.wagenheimer.buildpipeline`.
- Unified runtime `GameConfig` ScriptableObject supporting all Green Sauce Games properties.
- Unified stable `Publisher` and `GameLanguage` enums with language code/name extension methods.
- Modular pipeline engine with extensible `IPreBuildStep` and `IPostBuildStep` lifecycle.
- Built-in steps: GameConfig synchronization, PlayerSettings configuration, Android Keystore setup, Scene filtering, Publisher splash copier, Crash handler cleanup, and Zip archiving.
- Modern UI Toolkit EditorWindow with Quick Build tabs, Matrix batch generator, and live CLI command exporter.
- Headless `BuildCLI` runner supporting Unity CLI `-batchmode -quit` and PowerShell automation.
- 1-click Legacy GameConfig Migration wizard.
