# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-09-16

### Added

- **Quick Build from ProjectBuildConfig Inspector** - new top section that runs a build
  directly from the ProjectBuildConfig inspector (platform, publisher, language, cheat,
  development build, .aab/.apk and auto-run options) without opening the Build Pipeline Window.
- **Recent Builds tab** in the Build Pipeline Window: records the last 30 builds
  (`Library/BuildPipelineHistory.json`, machine-local) with per-entry actions:
  open output folder, run again (exe/app directly, APK via `adb install -r` + launch),
  and remove entry.
- **Tools/build.sh.meta** - missing meta file added so cached packages no longer warn
  about an untracked file in an immutable folder.

### Fixed

- LegacyGameConfigMigrator now creates `Assets/_Game/Settings` via
  `AssetDatabase.CreateFolder` instead of `Directory.CreateDirectory`, fixing asset
  creation failure when the Editor process working directory is not the project root.
## [1.1.7] - 2026-09-16

### Added

- **`GameConfig.CanRate`** — property migrated from the legacy per-project `GameConfig`
  (rate/review prompt availability per store publisher), so legacy projects can delete their
  local `GameConfig`/`GameConfigEditor` and switch to the package without code changes.
- **`Publisher.FourTheBalanceAndroidFull = 102`** — legacy publisher value preserved from the
  per-project enums; also moved `LegacyGames` back to its historical value `26` (matches the
  value serialized inside existing legacy `GameConfig.asset` files).

### Fixed

- **`GameLanguage.Korean` restored** (value `13`) with `ko` language-code and name mappings so
  projects that still reference `GameLanguage.Korean` keep compiling.

## [1.1.6] - 2026-09-15

### Fixed

- **`PlayerSettings.useMacAppStoreValidation=true` crashed locally-installed builds with "exit 173"**
  — this Unity flag makes the app call `exit(173)` at launch whenever
  `Contents/_MASReceipt/receipt` is missing, which is always the case for a `.pkg` installed
  directly (outside the real Mac App Store) — exactly the scenario used to QA a build before/without
  going through Apple's review. Apple does not require this flag for App Store acceptance; it's an
  optional, known-buggy anti-piracy convenience. `ApplyPlayerSettingsStep` now defaults it to `false`
  for every macOS build; opt back in per-profile via `PublisherProfile.macAppStoreValidation` or
  `-macAppStoreValidation true` on the CLI if genuine receipt validation is wanted for a release build.

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
