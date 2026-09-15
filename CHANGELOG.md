# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.5] - 2026-09-14

### Fixed

- **v1.1.3's Universal-architecture fix never actually applied on macOS** — it called
  `PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, ...)`, but that API only affects
  iOS/tvOS/visionOS and is a silent no-op on macOS (confirmed against Unity's own docs after the
  fix shipped and Mac App Store uploads kept failing with error 90981). The macOS "Architecture"
  dropdown is a standalone-platform setting only reachable via
  `EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", ...)`,
  which `ApplyPlayerSettingsStep` now uses instead.

## [1.1.4] - 2026-09-14

### Fixed

- **macOS/iOS build number could regress and get rejected by Apple (error 90061)** — a build
  landed with `CFBundleVersion=1` after the CI-bumped build number was #15, because
  `ApplyPlayerSettingsStep`'s macOS fallback path assigned `gameConfig.iOSBuildNumber` (a field
  shared with iOS, not macOS-specific) verbatim without checking it against the value already
  loaded from `ProjectSettings.asset`. `ApplyPlayerSettingsStep` and
  `BuildCLI.ApplyVersionAndBuildNumberOverrides` now floor every computed/CLI-supplied build
  number against the current in-memory Android/iOS/macOS values, so none of them can go backwards.
- **`CommandLineArgs` could keep stray wrapping quotes on a value** — CI wrappers that reconstruct
  argv from a single `--args "..."` string (e.g. the `unity build` CLI) can forward a value with
  its shell-escaped quotes still attached (`"15"` instead of `15`), which silently fails
  `int.TryParse` downstream. Values are now unwrapped of a single pair of surrounding quotes.

## [1.1.3] - 2026-09-14

### Fixed

- **macOS builds now default to Universal architecture** — `ApplyPlayerSettingsStep` calls
  `PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, ...)` before every macOS build.
  Previously an arm64-only Player Settings config would build fine locally but get rejected by
  `altool` on Mac App Store upload (error 90981: "supports Apple silicon but not Intel-based Mac
  computers") because the Info.plist's minimum OS didn't match. New `PublisherProfile.macArchitecture`
  (default `Universal`) lets a profile opt into `AppleSiliconOnly`/`IntelOnly` instead.

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
