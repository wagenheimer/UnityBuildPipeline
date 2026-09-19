# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.11.0] - 2026-09-19

## [1.10.0] - 2026-09-19

### Added
- Auto-installs `com.wagenheimer.packagehub` via git if missing, using a zero-dependency Editor bootstrap assembly (`PackageHubBootstrap`). Installing this package now pulls in PackageHub automatically, with no manual manifest edits or scoped registry required.

### Changed
- Reverted the `com.wagenheimer.packagehub` OpenUPM registry dependency added in 1.9.2: it required every consumer to configure a scoped registry manually, which defeats the "install one package, get everything" goal. The git-based auto-bootstrap replaces it.

## [1.9.2] - 2026-09-19

### Changed
- Re-added `com.wagenheimer.packagehub` as a proper semver dependency (`1.0.4`) now that it is published on the [OpenUPM registry](https://openupm.com/packages/com.wagenheimer.packagehub/). Consumers need the `com.wagenheimer` scope added to their `scopedRegistries`.

## [1.9.1] - 2026-09-18

### Fixed
- Removed `com.wagenheimer.packagehub` from `dependencies` in package.json: UPM does not support a git URL as a dependency version, which made this package fail to resolve/update in any consuming project. PackageHub must still be added directly to the consumer's manifest.json.

## [1.9.0] - 2026-09-18

## [1.8.1] - 2026-09-18

### Changed
- Standardized menu item priorities under `Tools > Wagenheimer > Build Pipeline` (base priority 110) for cohesive editor grouping and ordering.
- Updated `com.wagenheimer.packagehub` dependency to `v1.0.4`.

## [1.8.0] - 2026-09-18

## [1.7.1] - 2026-09-18

### Changed
- **Centralized Update Management**: Replaced standalone update checker with dependency on `com.wagenheimer.packagehub` (`UnityPackageHub`). Updates, changelogs, and package management are now handled centrally through the unified Wagenheimer Package Hub.

## [1.7.0] - 2026-09-18

## [1.6.1] - 2026-09-18

### Changed
- **Menu Hierarchy**: Consolidated all Build Pipeline editor menu items under `Tools > Wagenheimer > Build Pipeline` for clean grouping with other Wagenheimer packages.
- **Update Window Redesign**: Complete visual overhaul of `UpdateAvailableWindow` with modern header banner, pill badges, version diff card, rich-text markdown release notes parser (`✦ Added`, `✔ Fixed`, `⚡ Changed`, styled bullets), and fixed scrollview text truncation.
- **Multi-Version Release Notes**: Enhanced `ExtractVersionNotes` in `UpdateChecker` to extract notes across intermediate versions (e.g. 1.3.0 -> 1.6.1) and gracefully fallback to latest changelog section instead of showing empty notes.

## [1.5.0] - 2026-09-18

### Added
- **BuildDebugOverlay (`BuildDebugOverlay.cs`)**: In-game runtime verification overlay for Development Builds and Unity Editor.
  - Automatically activates in Editor and Development Builds via `[RuntimeInitializeOnLoadMethod]` (zero manual setup).
  - Floating on-screen `"BUILD DBG"` button (positioned without overlapping IAP DBG / RATE DBG) and hotkey `F7`.
  - Real-time display of active Publisher, FullGame status (Free vs Full), Demo, CheatMode, Language, and build numbers.
  - Automatic Platform & Package Verification: checks `Application.identifier` against the expected package for the active publisher and alerts of any mismatch.
  - Quick actions: Copy full diagnostic report, log to console, and toggle CheatMode at runtime.
  - Added Editor menu item: `Tools > Build Pipeline > Add Build Debug Overlay to Scene`.

### Fixed
- **CLI Platform Default in `build.ps1`**: Fixed a critical bug where `build.ps1` defaulted `-platform` to `"Windows64"`, overriding the publisher profile's platform (`Android`/`iOS`) and causing mobile builds to compile for Windows unless explicitly overridden.
- **Publisher & Profile Resolution in `BuildCLI`**: Added `FindPublisherProfile` with fuzzy matching, display name resolution, and common aliases (e.g. `GoogleFree`, `Google Play (Free)` -> `GoogleAndroidFree`), preventing silent fallbacks to `Publisher.Default`.
- Added `-buildProfile` CLI parameter support to `build.ps1`.

## [1.4.1] - 2026-09-17

### Changed
- chore(i18n): translate all UI strings, comments and docs to English


## [1.4.0] - 2026-09-17

### Added
- reorganize Build Pipeline Window header, version bar and tabs

## [1.3.0] - 2026-09-17

### Added
- add live save-status banner and visual GameConfig vs ProjectBuildConfig comparison

## [1.2.12] - 2026-09-17

### Changed
- docs: clarify GameConfig vs ProjectBuildConfig split in UI and tooltips

## [1.2.11] - 2026-09-17

### Changed
- ci: add automatic package version bump (bump + changelog + tag + release on push)

## [1.2.10] - 2026-09-17

### Fixed

- **Missing `.meta` for `ExportAndroidSymbolsStep.cs` broke every project on 1.2.9** - the new
  script had no versioned `.cs.meta`, so Unity did not import/compile it inside the git package and
  any package that referenced the type failed with `CS0103/CS0246: ExportAndroidSymbolsStep does not
  exist`. Added the `.meta` (repo-consistent 59-byte format: `fileFormatVersion` + `guid`). Every
  `.cs` in the package must ship with its `.meta`.

## [1.2.9] - 2026-09-17

### Added

- **Android native debug symbols enabled by default, and exported for Google Play** - release
  Android builds now generate native symbols and hand them to CI. The Play Console warnings
  "this release contains native code and you didn't upload debug symbols" are gone.
  - `ApplyPlayerSettingsStep` now uses the Unity 6 API
    `UnityEditor.Android.UserBuildSettings.DebugSymbols` (`level = SymbolTable`,
    `format = Zip | LegacyExtensions | IncludeInBundle`), falling back to the obsolete
    `EditorUserBuildSettings.androidCreateSymbols` on older editors. The obsolete property
    became a no-op in Unity 6000.0.23, which is why builds were producing no `*.symbols.zip` at
    all. Symbols are embedded in the `.aab` itself (Play receives them with the bundle, no extra
    upload) and also written as a `.symbols.zip` beside the artifact.
  - New `ExportAndroidSymbolsStep` post-build step copies the generated `*.symbols.zip` and, when
    present, the R8/ProGuard `mapping.txt` next to the `.aab` with deterministic names.
  - `BuildManifest` gained `symbolsZipPath`, `mappingPath` and `symbolsEmbedded` so external CI
    (AppDeployHub.Forge) can upload them without guessing paths.

## [1.2.8] - 2026-09-16

### Added

- **AsGameLanguage / AsFlagSprite** moved from per-project GameUtils into the shared
  `LanguageExtensions` (Runtime), including Korean support, so legacy projects can delete
  their local duplicates.
## [1.2.7] - 2026-09-16

### Fixed

- **Slim bundletool jars still passed the aapt2 check** - the previous check matched any entry
  containing "aapt2", which also matches class files present in *every* bundletool build
  (`Aapt2Command.class`), so the Visual Studio / .NET SDK slim jar kept being selected and
  `build-apks` failed with "Unable to locate aapt2 inside jar". The check now matches only the
  actual binary entry name (`aapt2` / `aapt2.exe`), which exists solely in the official
  `bundletool-all` jar (`windows/aapt2.exe`, `macos/aapt2`, `linux/aapt2`).
- **Deep jar search aborted on a single unreadable subfolder** - the recursive scan now falls
  back to a depth-limited tolerant scan instead of giving up (which previously let a slim
  Visual Studio copy win the search).

## [1.2.6] - 2026-09-16

### Fixed

- **Valid bundletool-all jars were rejected as slim** - the aapt2 validator only matched
  entries whose path *starts with* `aapt2`, but in bundletool 1.17+ the binary lives under
  an OS folder (`windows/aapt2.exe`). The check now matches `aapt2` anywhere in the entry
  path, so the official jar is detected correctly and "NOT FOUND" no longer triggers a
  needless download loop.

## [1.2.5] - 2026-09-16

### Fixed

- **bundletool download could hang forever** - the .NET `WebClient` has no timeout, so a
  stalled GitHub connection kept the Editor "downloading" indefinitely. The download now
  prefers the OS `curl.exe` (`--connect-timeout 15 --max-time 600`), and the WebClient
  fallback aborts automatically after 30s without progress (watchdog), reporting the failure
  instead of hanging.

### Changed

- **Downloaded bundletool now stored in a stable machine-wide folder**
  (`%LOCALAPPDATA%\Wagenheimer\BuildPipeline`), shared across projects and immune to Unity
  regenerating the project's `Library` folder; the search looks there as well.

## [1.2.4] - 2026-09-16

### Fixed

- **BuildHistory did not compile** - missing `using System.Linq;` (`Select`/`OrderByDescending`
  on `string[]`).

### Added

- **Visible toolchain in the UI** - the Recent Builds tab shows which `bundletool.jar` is in
  use (or "NOT FOUND"), and the Run confirmation dialog now lists the resolved
  bundletool/java (AAB) and adb (APK) paths before running, so the user can verify what will
  execute.

## [1.2.3] - 2026-09-16

### Fixed

- **AAB install failed with "Unable to locate aapt2 inside jar"** - the slim `bundletool.jar`
  shipped inside .NET Android SDK packs / Visual Studio lacks the embedded aapt2 binary that
  `build-apks` requires. Every discovered jar is now inspected (zip check for embedded aapt2
  resources) and slim builds are skipped with a warning.

### Added

- **Automatic download of bundletool-all.jar** - when no jar with embedded aapt2 is found on
  the system, the AAB runner offers to download the latest official `bundletool-all.jar`
  from Google's GitHub releases into the project's `Library/` folder (cancelable download).
  The manual file picker remains as last resort and now rejects slim jars too.

## [1.2.2] - 2026-09-16

### Added

- **Automatic bundletool.jar discovery** - when no explicit path is configured, the AAB
  runner now scans known toolchain locations for `bundletool*.jar` before asking:
  Unity's Android player tools (`Data/PlaybackEngines/AndroidPlayer`), .NET Android SDK
  workloads (`ProgramFiles/dotnet/packs/Microsoft.Android.Sdk.Windows/<ver>/tools`),
  and Visual Studio editions (`<edition>/MSBuild/Xamarin/Android`). The newest copy wins;
  the file picker is now a last resort only.

## [1.2.1] - 2026-09-16

### Added

- **"Run Build" confirmation dialog** - before running/installing, a dialog shows exactly
  what will happen (run exe/app, adb install APK, or bundletool AAB install) plus the
  entry's publisher/language/cheat/dev flags, with RUN/CANCEL buttons.
- **Cancellable installs** - APK install, bundletool AAB generate/install and launch steps now
  use `DisplayCancelableProgressBar`; the adb/bundletool process is killed and the operation
  aborts cleanly when Cancel is pressed.

## [1.2.1] - 2026-09-16

### Added

- **AsGameLanguage / AsFlagSprite** moved from per-project GameUtils into the shared
  `LanguageExtensions` (Runtime), including Korean support, so legacy projects can delete
  their local duplicates.
## [1.2.0] - 2026-09-16

### Added

- **Quick Build from ProjectBuildConfig Inspector** - new top section that runs a build
  directly from the ProjectBuildConfig inspector (platform, publisher, language, cheat,
  development build, .aab/.apk and auto-run options) without opening the Build Pipeline Window.
- **Recent Builds tab** in the Build Pipeline Window: records the last 30 builds
  (`Library/BuildPipelineHistory.json`, machine-local) with per-entry actions:
  open output folder, run again (exe/app directly, APK via `adb install -r` + launch),
  and remove entry.
- **Install .aab builds on a connected device** - the "Run" button on `.aab` history entries
  now generates device-specific APKs with Google's official `bundletool`
  (`build-apks --connected-device`) and installs them (`install-apks`), then launches the
  game via adb. Java comes from Unity's bundled OpenJDK
  (`Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK`); `bundletool.jar` is auto-discovered
  from `Library/bundletool.jar` / project root / `BUNDLETOOL_JAR` env var, with a file-picker
  fallback that persists the path in EditorPrefs.
- **Tools/build.sh.meta** - missing meta file added so cached packages no longer warn
  about an untracked file in an immutable folder.

### Fixed

- **"Recent Builds" tab showed "Keystore & Vault" content** - a stale tab-index shift
  (`i >= 2 ? i + 1 : i`) left over from before the Recent Builds tab existed made every tab
  from index 2 onward render the wrong panel. Tab index now maps 1:1 to content.
- **`BuildHistory` did not compile** - ambiguous `Debug` reference
  (`System.Diagnostics` vs `UnityEngine`) and an implicit `ulong`-to-`long` conversion.
- LegacyGameConfigMigrator now creates `Assets/_Game/Settings` via
  `AssetDatabase.CreateFolder` instead of `Directory.CreateDirectory`, fixing asset
  creation failure when the Editor process working directory is not the project root.

## [1.1.7] - 2026-09-16

### Added

- **`GameConfig.CanRate`** � property migrated from the legacy per-project `GameConfig`
  (rate/review prompt availability per store publisher), so legacy projects can delete their
  local `GameConfig`/`GameConfigEditor` and switch to the package without code changes.
- **`Publisher.FourTheBalanceAndroidFull = 102`** � legacy publisher value preserved from the
  per-project enums; also moved `LegacyGames` back to its historical value `26` (matches the
  value serialized inside existing legacy `GameConfig.asset` files).

### Fixed

- **`GameLanguage.Korean` restored** (value `13`) with `ko` language-code and name mappings so
  projects that still reference `GameLanguage.Korean` keep compiling.

## [1.1.6] - 2026-09-15

### Fixed

- **`PlayerSettings.useMacAppStoreValidation=true` crashed locally-installed builds with "exit 173"**
  � this Unity flag makes the app call `exit(173)` at launch whenever
  `Contents/_MASReceipt/receipt` is missing, which is always the case for a `.pkg` installed
  directly (outside the real Mac App Store) � exactly the scenario used to QA a build before/without
  going through Apple's review. Apple does not require this flag for App Store acceptance; it's an
  optional, known-buggy anti-piracy convenience. `ApplyPlayerSettingsStep` now defaults it to `false`
  for every macOS build; opt back in per-profile via `PublisherProfile.macAppStoreValidation` or
  `-macAppStoreValidation true` on the CLI if genuine receipt validation is wanted for a release build.

## [1.1.5] - 2026-09-14

### Fixed

- **v1.1.3's Universal-architecture fix never actually applied on macOS** � it called
  `PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, ...)`, but that API only affects
  iOS/tvOS/visionOS and is a silent no-op on macOS (confirmed against Unity's own docs after the
  fix shipped and Mac App Store uploads kept failing with error 90981). The macOS "Architecture"
  dropdown is a standalone-platform setting only reachable via
  `EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", ...)`,
  which `ApplyPlayerSettingsStep` now uses instead.

## [1.1.4] - 2026-09-14

### Fixed

- **macOS/iOS build number could regress and get rejected by Apple (error 90061)** � a build
  landed with `CFBundleVersion=1` after the CI-bumped build number was #15, because
  `ApplyPlayerSettingsStep`'s macOS fallback path assigned `gameConfig.iOSBuildNumber` (a field
  shared with iOS, not macOS-specific) verbatim without checking it against the value already
  loaded from `ProjectSettings.asset`. `ApplyPlayerSettingsStep` and
  `BuildCLI.ApplyVersionAndBuildNumberOverrides` now floor every computed/CLI-supplied build
  number against the current in-memory Android/iOS/macOS values, so none of them can go backwards.
- **`CommandLineArgs` could keep stray wrapping quotes on a value** � CI wrappers that reconstruct
  argv from a single `--args "..."` string (e.g. the `unity build` CLI) can forward a value with
  its shell-escaped quotes still attached (`"15"` instead of `15`), which silently fails
  `int.TryParse` downstream. Values are now unwrapped of a single pair of surrounding quotes.

## [1.1.3] - 2026-09-14

### Fixed

- **macOS builds now default to Universal architecture** � `ApplyPlayerSettingsStep` calls
  `PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, ...)` before every macOS build.
  Previously an arm64-only Player Settings config would build fine locally but get rejected by
  `altool` on Mac App Store upload (error 90981: "supports Apple silicon but not Intel-based Mac
  computers") because the Info.plist's minimum OS didn't match. New `PublisherProfile.macArchitecture`
  (default `Universal`) lets a profile opt into `AppleSiliconOnly`/`IntelOnly` instead.

## [1.1.0] - 2026-09-08

### Added

- **Machine-readable build manifest** � `-manifest <path>` writes a JSON summary
  (`success`, `platform`, `profileId`, `defines`, `version`, `buildNumber`, `artifactPath`,
  `xcodeProjectPath`, `sizeBytes`, `errors`, `warnings`, `durationSeconds`). `BuildMatrix`
  writes an array of entries. Intended for external CI (AppDeployHub.Forge) instead of log scraping.
- **Stable profile ids** � `PublisherProfile.id` + `-buildProfile <id>` / `-matrixProfiles "a,b,c"`
  so CI can select a profile without depending on the `Publisher` enum. `EffectiveId` falls back to
  the publisher name when `id` is empty.
- **Compile-time build variants** � `PublisherProfile.scriptingDefines` + `-defines "A;B;C"` applied
  by the new `ApplyScriptingDefinesStep` and **restored** afterwards (also on build failure), so CI
  never leaves `ProjectSettings` dirty. Enables real Free/Full/Demo via `#if`.
- **WebGL platform** � `PlatformType.WebGL` with target/target-group mapping and output-path
  resolution; `PlatformTypeExtensions.IsGenericArtifact()` marks folder/site artifacts (no store upload).
- **`Tools/build.sh`** � macOS/Linux headless runner that resolves the Editor from
  `ProjectVersion.txt` (Unity Hub layout), mirroring `Tools/build.ps1`.
- **`-keystorePath` / `-keystoreAlias`** CLI args for the Android keystore (forces `LocalDisk` source);
  passwords still come from `ANDROID_KEYSTORE_PASS` / `ANDROID_KEYALIAS_PASS`. Lets CI inject a central keystore.
- **ProjectSettings stays pristine on CI** � `ApplyPlayerSettingsStep` now snapshots and restores
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
