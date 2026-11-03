# Unity Build Pipeline (`com.wagenheimer.buildpipeline`)

A modular, multi-project build automation pipeline for Unity. Designed for multi-store portfolio game development, featuring publisher presets, language matrix generation, post-processing hooks, a modern UI Toolkit interface, and 100% headless automation via Unity CLI and PowerShell.

[![openupm](https://img.shields.io/npm/v/com.wagenheimer.buildpipeline?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.wagenheimer.buildpipeline/)
[![License: MIT](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

---

## ✨ Features

- **One window, one place per setting** (`Tools ▸ Wagenheimer ▸ Build Pipeline ▸ Open Build Pipeline`): a sidebar grouped by
  intent — **Build** (one-click or batch, with a plan table) · **History** · **Profiles** · **Defines** · **Languages** ·
  **Project** · **Signing** · **Steam** · **CLI & Forge** — and an always-visible **health bar** (validator, external
  changes to the global defines, catalog freshness). The asset inspector only links to it.
- **`ProjectBuildConfig` is the single source of truth.** The editor, the CLI and AppDeployHub/Forge read the same profile
  stack; the Forge never overrides defines or language.
- **Per-profile language**: *Auto* (project default, normally AutoDetect) or *Locked* to one language (e.g. a German-only
  build for a publisher — `_DE` folder suffix, `GameConfig.IsLanguageLocked` so the game hides its selector). A one-off
  language can still be picked for a single build without touching the profile.
- **Defines center**: every symbol with its documentation, the profiles that get it, a layered stack per profile
  (PlayerSettings globals → presets → profile → pipeline auto-defines → CLI), **🔍 find usages** in `Assets/`, embedded
  packages and `Library/PackageCache` (`#if/#elif`, `[Conditional]`, asmdef `defineConstraints`/`versionDefines`, `csc.rsp`)
  with click-to-open, **dead/orphan** detection, **Free × Full code impact** and a detector for code that writes global
  defines behind your back.
- **Package-declared define registry**: each package documents its symbols and rules (`requiredWhen`/`forbiddenWhen`) in its
  own `package.json`; the game documents its own in the window. Broken rules show in the window, the confirmation dialog,
  the catalog and the Forge.
- **Store & publisher profiles** for Steam (explicit flag), Big Fish, GameHouse, Alawar, itch.io, Google Play, Amazon,
  iOS, Mac App Store, Samsung and direct site distribution; Free ↔ Full duplication with the ad/tracking defines.
- **Game config extension & build variants** (Collector's / Standard Edition…) compiled by the batch, the single build and
  the CLI.
- **Android signing from the AppDeployHub vault** (same source as the Forge CI) or a local keystore.
- **Headless CLI & CI** (`-executeMethod`, `build.ps1`/`build.sh`), JSON manifest per build (with language source and the
  full define stack) and `ProjectBuildConfig.catalog.json` (schema 4) for AppDeployHub.

## 📦 Installation

### Via Unity Package Manager (Git URL)
1. Open your Unity project.
2. Go to **Window > Package Manager**.
3. Click the **+** button in the top-left corner and select **Add package from git URL...**
4. Enter:
   ```
   https://github.com/wagenheimer/UnityBuildPipeline.git
   ```

### Via `manifest.json` (Local or Git)
Add to your project's `Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.wagenheimer.buildpipeline": "https://github.com/wagenheimer/UnityBuildPipeline.git#vX.Y.Z"
  }
}
```
Or for local development:
```json
{
  "dependencies": {
    "com.wagenheimer.buildpipeline": "file:../../Open Source/UnityBuildPipeline"
  }
}
```

---

## 🚀 Quick Start

1. **Tools ▸ Wagenheimer ▸ Build Pipeline ▸ Open Build Pipeline** (or `Window ▸ Wagenheimer ▸ Build Pipeline`).
2. First run creates `Assets/_Game/Settings/ProjectBuildConfig.asset` and links your `GameConfig.asset`.
3. **Profiles**: check each store profile (id, bundle, Free/Full, Steam, language, defines).
4. **Defines**: document the game's own symbols (e.g. `ADS` → required when `free`, forbidden when `full`).
5. **Build**: ⚡ on a profile builds it now; tick several profiles + languages for a batch and review the plan table.

Upgrading from 1.x? See **Migrating to 2.0** below.

## 🧩 Extending GameConfig per game (game-specific fields & editions)

`GameConfig` is intentionally *not* sealed. Every game can add its own serialized fields by deriving from it
in the game project — the package keeps working with the base type and the inspector renders the extras
automatically.

```csharp
// Assets/_Game/Scripts/_Game/QueensGardenConfig.cs  (game project, Assembly-CSharp)
public class QueensGardenConfig : GameConfig
{
    [Header("Queen's Garden 6")]
    public bool CollectorEdition = true;
    public bool AnalyticsEnabled = true;
    public string MacAppStoreID = "";

    // Declared variants: "ce" / "se" (see Build Variants below)
    public override void ApplyBuildVariant(string variantId)
        => CollectorEdition = variantId == "ce";

    public override string GetBuildVariantGameName(string variantId)
        => variantId == "ce" ? GameName + " - CE" : GameName;
}
```

The asset keeps its GUID — only the `m_Script` type changes — so all existing values survive the migration.

### Build Variants (editions)

Add entries to **Build Variants** in the GameConfig inspector:

| id | displayName | outputSuffix | enabledByDefault |
|---|---|---|---|
| `ce` | Collector's Edition | `_CE` | ✔ |
| `se` | Standard Edition | *(empty)* | ✔ |

The pipeline treats the list as data:

- **Matrix Batch** gains a *Game Variants* column and multiplies the build count.
- **Quick Build** gains a *Variant* dropdown.
- **CLI**: `-variant ce` (single) and `-matrixVariants "ce,se"` (matrix).
- The suffix is appended to the output folder (`..._Aug18 (1.0.16)_CE`) and recorded in the manifest
  (`variant`) and the build history badge.
- Before each build `ApplyBuildVariant(id)` runs, so the game maps the id to its own fields; the game
  can also override `GetBuildVariantGameName(id)` to change `PlayerSettings.productName` per edition.

No package changes are required to add a new edition to any game.

---

## 🌐 Build language

| Profile mode | What the build gets |
|---|---|
| **Auto** (default) | `ProjectBuildConfig.defaultLanguage` (normally `AutoDetect`: the game follows the device). |
| **Locked** | Always `lockedLanguage`. The output folder gets the language suffix (`_DE`) and `GameConfig.IsLanguageLocked` is true, so the game hides its language selector. Batch builds build a locked profile once, ignoring the selected languages. |

A **one-off override** (Build section picker, CLI `-language de`) wins over both and is recorded in the manifest
(`languageSource: Override`). It never changes the profile.

## 🧬 Defines

### The stack a build applies

1. **Global** — PlayerSettings scripting defines of the target group (Windows, Mac and Linux share *Standalone*).
2. **Preset** — `ProjectBuildConfig.definePresets` referenced by the profile.
3. **Profile** — the profile's own `scriptingDefines` (`!X` removes X for this build).
4. **Auto** — added by the pipeline (UnityNativeSocial's Steam define on Steam profiles when Steamworks.NET is installed).
5. **CLI** — `-defines` (manual CI only; the Forge no longer sends it).

Removals are applied before additions, and everything is restored after the build. asmdef `versionDefines` are shown as
*assembly-scoped*: they are defined automatically but **only inside that assembly**.

The same resolver (`DefineStackResolver`) feeds the build step, the window, the confirmation dialog, the manifest and the
catalog, so what you see is what is compiled.

### Declaring a package's defines (for package authors)

Add a `wagenheimer.defines` block to the package's `package.json`; Unity ignores the extra key and the pipeline picks it up:

```json
"wagenheimer": {
  "defines": [
    {
      "symbol": "WAGENHEIMER_NATIVESOCIAL_STEAM",
      "description": "Compiles NativeSocial's Steam backend.",
      "scope": "global",
      "autoFromPackage": "com.rlabrecque.steamworks.net",
      "requiredWhen": ["steam"],
      "forbiddenWhen": [],
      "platforms": ["Standalone"],
      "severity": "error",
      "docsUrl": "https://github.com/wagenheimer/UnityNativeSocial#steam"
    }
  ]
}
```

- `scope`: `global` (PlayerSettings/profile define, visible to every assembly) or `assembly` (asmdef `versionDefine`).
- `requiredWhen` / `forbiddenWhen`: rules over profile tags — `steam`, `free`, `full`, `demo`, `android`, `ios`, `mac`,
  `windows`, `linux`, `webgl`, `mobile`, `desktop`, `publisher:<Name>`, `id:<profileId>`. `+` = AND, `!` = NOT
  (e.g. `ios+free`, `android+!full`). Each list entry is an alternative (OR).
- `severity`: `error` (default) or `warning`.

The game declares its own symbols in **Defines ▸ Defines do jogo** (`ProjectBuildConfig.gameDefines`, same schema).
Undocumented profile defines are reported as warnings.

### Find usages and impact

**Defines ▸ Central** scans the code on a background thread (cached per file in `Library/BuildPipeline`) and lists, for
each symbol, every `#if`/`#elif`, `[Conditional]`, asmdef constraint (which drops the whole assembly), `versionDefine`,
`csc.rsp` entry and local `#define`, grouped by package, with click-to-open at the line. For each profile it shows how
many code locations change if the symbol is removed; **Profiles ▸ Defines ▸ Compare with** lists the blocks that differ
between two profiles (e.g. Free × Full). **Globals** shows the PlayerSettings defines per group, what changed since you
last reviewed them and every script that calls `SetScriptingDefineSymbols`.

## 🧳 Migrating a project with its own GameConfig.cs

Old games declare `GameConfig`, `Publisher`, `GameLanguage` and `GameVersion` themselves — the same global types this
package ships — so the project does not compile with both and Unity never loads the package. With Unity **closed**:

```powershell
./Tools/migrate-legacy-project.ps1 -ProjectPath "K:\Games\MyGame"          # dry run: what would change
./Tools/migrate-legacy-project.ps1 -ProjectPath "K:\Games\MyGame" -Apply   # migrate (backup in temp_migration_backup/)
```

Game-only GameConfig fields go to a generated `<Game>Config : GameConfig` (values kept), `GameConfig X;` declarations
are retyped to it, other types of the old file (e.g. `GameDate`) are kept, the asset's script GUID is fixed and the
package is added to `Packages/manifest.json`. Then open the project: the assistant asks to create the
ProjectBuildConfig and **Project ▸ Migração** lists what is left (e.g. game-only `Publisher` values).
Projects that already have the package (or no own GameConfig.cs) only get the in-editor question.

## 🧭 Migrating to 2.0

- Requires **Unity 6** (`6000.0`).
- `PublisherProfile.isSteam` is now an explicit flag. The first load fills it from the 1.x heuristic (publisher `Steam`,
  or "steam" in the id/name) and the validator warns when a profile looks like Steam but is not flagged.
- **Editor builds now apply the profile defines.** In 1.x only the CLI did; Quick Build/Matrix/Steam builds from the
  editor compiled without the profile's `scriptingDefines` (e.g. a Free build without its ad define).
- The CLI matrix now expands define presets (1.x used only the profile's own list).
- `-language` is a one-off override; without it the profile decides. `build.ps1`/`build.sh` no longer send `-language AutoDetect` by default.
- The wagenheimer.com keystore vault is retired: `-vaultUrl/-vaultProfile/-vaultToken` are gone and
  `keystoreSource: RemoteVault` became `AppDeployHubVault` (same serialized value — pick the credential in **Signing**).
- Menus moved under `Tools ▸ Wagenheimer ▸ Build Pipeline`. Profile Matrix, Define Presets and Steam Upload are now
  sections of the window; the *Steam Upload...* menu opens the Steam section.
- Catalog schema **4** adds `languageMode`, `lockedLanguage`, `isSteam`, `notes`, `finalDefines`, `defineStack`,
  `defineRuleViolations`, `globalDefines` and `defineRegistry`. AppDeployHub.Forge reads it and shows defines/language
  read-only.

## 💻 Unity CLI & Headless Builds

You can run builds from PowerShell, Command Prompt, or CI/CD without opening the Unity Editor GUI.

### Single Target Build
```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "k:\Games\Green Sauce Games\Storm-Tale2" `
  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.Build `
  -publisher BigFish `
  -language en `
  -platform Windows64 `
  -cheat false `
  -logFile "build_bigfish.log"
```

### Matrix Batch Build
```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "k:\Games\Green Sauce Games\Storm-Tale2" `
  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildMatrix `
  -matrixPublishers "BigFish,Steam,GameHouse" `
  -matrixLanguages "en,de,fr" `
  -cheatOff true `
  -cheatOn false `
  -logFile "build_matrix.log"
```

### Automated Scripts (`build.ps1` / `build.sh`)
Ready-to-use runners are provided inside `Tools/` — `build.ps1` for Windows, `build.sh` for
macOS/Linux CI agents (both resolve the Editor from `ProjectSettings/ProjectVersion.txt`):
```powershell
# Windows — single build:
./build.ps1 -projectPath "k:\Games\Green Sauce Games\Storm-Tale2" -publisher "BigFish" -language "en"

# Windows — matrix build:
./build.ps1 -projectPath "k:\Games\Green Sauce Games\Storm-Tale2" -matrix -matrixPublishers "BigFish,Steam" -matrixLanguages "en,de"
```
```bash
# macOS/Linux CI — select a profile by stable id, emit a JSON manifest:
./build.sh --project /Users/ci/work/Storm-Tale2 --build-profile stormtale-full-android --manifest out.json
./build.sh --project . --matrix --matrix-profiles "st-free-android,st-full-android" --manifest out.json
```

### CI / headless output — `-manifest`
Pass `-manifest <path>` to write a machine-readable JSON summary instead of scraping the log.
`Build` writes one entry; `BuildMatrix` writes an array under `builds`. Each entry:
`success`, `result`, `platform`, `profileId`, `defines`, `version`, `buildNumber`,
`outputDirectory`, `artifactPath`, `xcodeProjectPath` (iOS only), `sizeBytes`, `errors`,
`warnings`, `durationSeconds`, `errorMessage` — plus `variant` when a game variant was built.
Each entry also carries `appliedDefines` (final symbols), `profileDefines` (declared by the profile) and
`cliOverrides` (defines re-added by `-defines` that the profile removes). `cliOverrides` is the
runtime proof of a dashboard override; the step also logs a warning when it is not empty.

---

## 🔧 CLI Argument Reference

| Flag | Values | Description |
|---|---|---|
| `-buildProfile` | Profile `id` (or publisher name) | **Preferred.** Selects a `PublisherProfile` by its stable `id`; overrides `-publisher`/`-platform`. An `id` that does not exist in the ProjectBuildConfig **fails the build** (the target would otherwise compile without its bundle id, ad and tracking defines). |
| `-allowMissingProfile` | `true` / `false` | Escape hatch for the check above: log a warning and continue with `-publisher`/`-platform` instead of failing. |
| `-publisher` | `BigFish`, `Steam`, `GoogleAndroidFull`, etc. | Target store/publisher profile (used when `-buildProfile` is absent). |
| `-language` | `en`, `de`, `fr`, `English`, `German`, `auto`... | **One-off** language for this build (wins over a locked profile, recorded as `languageSource: Override`). Omit it to let the profile decide. |
| `-platform` | `Windows64`, `Android`, `macOS`, `iOS`, `Linux64`, `WebGL` | Target platform. |
| `-defines` | `"A;B;C"` | Extra symbols on top of the profile stack (manual CI only — the Forge no longer sends it). `!X` removes X for this build. A CLI symbol that undoes a profile removal is reported in `cliOverrides`. |
| `-variant` | `ce`, `se`, any declared id | Game-specific build variant for a single build (applied via `ApplyBuildVariant`). |
| `-matrixVariants` | `ce,se` | Variants for matrix generation (defaults to the GameConfig's `enabledByDefault` entries). |
| `-manifest` | Full path | Write a JSON result manifest (see above). |
| `-cheat` | `true` / `false` | Enable or disable CheatMode. |
| `-development` | `true` / `false` | Generate a Unity Development Build. |
| `-demo` | `true` / `false` | Generate a Demo version. |
| `-version` | `Major.Minor.Build` (e.g. `1.0.5`) | Override version numbers before building. |
| `-matrixProfiles` | Comma-separated profile ids | **Preferred** selector for matrix generation. |
| `-matrixPublishers` | Comma-separated names | Publishers for matrix generation (legacy). |
| `-matrixLanguages` | Comma-separated codes/names | Languages for matrix generation. |
| `-outputPath` | Full path | Custom output executable or directory. |
| `-autoRun` | `true` / `false` | (Android) Install and run on connected device; opens Android Logcat. |
| `-keystoreSource` | `LocalDisk` / `AppDeployHubVault` | Android keystore source for this build (`-keystorePath` forces `LocalDisk`). |

---

## 🔐 Android signing (AppDeployHub vault)

Editor builds can read the Android keystore from the **AppDeployHub vault** (`/api/v1/vault`), the same single source the
Forge CI uses:

1. **Signing** section → *Origem* = `AppDeployHubVault`, set the AppDeployHub URL.
2. Paste an API key with `Purpose = vault` (AppDeployHub → Cofre → Conectar o Forge). It is stored per machine
   (EditorPrefs) or read from `APPDEPLOYHUB_VAULT_KEY` — never in the asset.
3. **List keystores** → *Use this* on the `AndroidKeystore` credential.

Before an Android build, `SetupKeystoreStep` downloads it into `Library/KeystoreCache/` (gitignored), injects the passwords
in memory and clears them after the build. `LocalDisk` (keystore path + password env vars) remains available, and CI
passes `-keystorePath` + `ANDROID_KEYSTORE_PASS`/`ANDROID_KEYALIAS_PASS` exactly as before.

## 📱 Android Auto-Run & Android Logcat

When testing on physical devices or emulators, pass `-autoRun` in CLI or check the option in the UI:
```powershell
./build.ps1 -publisher GoogleAndroidFree -development -autoRun
```
- **Instant Deployment**: Forces generation of `.apk` (bypassing `.aab`) and runs `adb install -r` automatically.
- **Auto-Launch**: Launches the game directly on your device.
- **Logcat Stream**: Automatically executes `Window > Analysis > Android Logcat` in the Unity Editor so you can monitor console logs immediately.

---

## 🔌 Extensible Lifecycle Hooks

You can easily register custom pre-build or post-build actions in your project editor scripts without touching the package:

```csharp
using Wagenheimer.BuildPipeline.Editor;
using UnityEditor.Build.Reporting;
using UnityEditor;

[InitializeOnLoad]
public static class CustomGameBuildHooks
{
    static CustomGameBuildHooks()
    {
        BuildPipelineRunner.RegisterPostStep(new DiscordNotificationStep());
    }
}

public class DiscordNotificationStep : IPostBuildStep
{
    public int Order => 100;

    public bool ExecutePostBuild(BuildContext context, BuildReport report)
    {
        // Send webhook or notification with report.summary.totalSize
        return true;
    }
}
```

---

## 📄 License

MIT License. Copyright (c) 2026 Cezar Wagenheimer.

---

---

## Build profile catalog & validation

### `ProjectBuildConfig.catalog.json`

Derived, machine-readable snapshot of the profile matrix, written next to the asset whenever it is
imported (and by the CLI). The `.asset` stays the source of truth; the JSON exists so AppDeployHub
and CI do not have to parse Unity YAML.

```json
{
  "schema": 1,
  "configHash": "sha1 do asset",
  "tracking": { "requireTracking": true, "scriptingDefine": "FTDOTD_TRACKING", "adsDefine": "FTDOTD_MONETIZATION" },
  "profiles": [
    {
      "id": "iOSFree",
      "platform": "iOS",
      "isFullGame": false,
      "bundleIdentifier": "com.company.game",
      "scriptingDefines": ["FTDOTD_MONETIZATION", "FTDOTD_TRACKING"],
      "effectiveDefines": ["FTDOTD_MONETIZATION", "FTDOTD_TRACKING"],
      "removedDefines": []
    }
  ]
}
```

- `scriptingDefines`: exactly as typed (including `!DEFINE` removals).
- `effectiveDefines`: what survives after the removals — what the build actually gets from the profile.
- `tracking`: mirrored from `com.wagenheimer.tracking` (`null` when the package is absent).
- `configHash`: lets a consumer detect a stale catalog.

Menu: **Tools ▸ Wagenheimer ▸ Build Pipeline ▸ Export Profile Catalog** / **Validate Profiles**.

### CLI

```bash
# write the catalog (default: next to the asset)
Unity -batchmode -quit -projectPath . \
  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.ExportProfileCatalog \
  -output BuildProfiles/ProjectBuildConfig.catalog.json

# validate the matrix, exit 1 on errors
Unity -batchmode -quit -projectPath . \
  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.ValidateProfiles
```

### Pre-build gate

`ValidateProfilesStep` (order 4) runs on every build and aborts on **errors**; warnings only log.
Pass `-allowProfileErrors` for an emergency build.

| Rule | Severity |
|---|---|
| Mobile profile without an explicit `id` | warning |
| Duplicate `id` | error |
| Android/iOS profile without a bundle id | error |
| Free and Full of the same store sharing the bundle id | error |
| Invalid define token (space/comma/semicolon) | error |
| Same define added and removed in one profile | error |
| Full profile applying the ad define | error |
| Free iOS profile without the ad define | warning |
| Tracking define used outside iOS | warning |
| Duplicate `outputSubfolder` | warning (Steam desktop siblings are exempt: they get automatic `Windows`/`Linux`/`Mac` subfolders) |

Steam desktop profiles can share a single `outputSubfolder` (for example `Builds/Publishers/Steam/`).
The pipeline appends `Windows`, `Linux` or `Mac` for the build platform, so the three siblings never
write into the same directory. A profile may also use the optional `{Platform}` token explicitly.

### Duplicate Free ↔ Full

The profile card has **→ Full / → Free**: it duplicates the profile and fixes the publisher,
id, display name, bundle id, output folder and the ad/tracking defines (Full gets `!adsDefine`, Free
gets `adsDefine` plus the tracking define on iOS). The same logic runs headless:

```bash
Unity -batchmode -quit -projectPath . \
  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.DuplicateProfileAsCounterpart \
  -profile iOSFree          # dry-run: prints the counterpart

# add -apply to append it to ProjectBuildConfig
```

### Define presets

`ProjectBuildConfig.definePresets` holds reusable groups (`id`, `displayName`, `defines[]`). A profile
references them in `Profile.definePresets`; the build expands presets **before** the profile's own
`scriptingDefines`, and the catalog exports the expanded list (`presetIds` keeps the reference).
The inspector shows the presets card, and the validator fails on unknown or duplicate preset ids.

### Profile matrix

**Profiles ▸ Matriz & validação** shows every profile with platform, Free/Full, bundle,
output and the effective defines, plus:

- a **Free ↔ Full diff** per store (defines only in Free / only in Full, shared bundle/output);
- the **validator issues** per row (tooltip with the messages);
- the **define presets** and how many profiles use each;
- **one-click fixes** for the unambiguous findings (id, bundle, output, ad define, preset);
- **New Profile...** (store + Free/Full template) and **Define Presets...** editors.

The catalog schema is now **3** (adds `presetIds` and a `validation` block with the linter result);
consumers ignore unknown fields, as before.

### CI

```bash
# Linux/macOS
UNITY_BIN=/path/Unity ./Tools/validate-profiles.sh /path/to/project

# Windows
./Tools/validate-profiles.ps1 -Project C:\path\to\project -Unity C:\path\Unity.exe
```

Both exit with code 1 when the validator reports an error, so a PR job can gate on it. The same
number appears in `ProjectBuildConfig.catalog.json` under `validation`.

### Wagenheimer Unity ecosystem

[Package Hub](https://github.com/wagenheimer/UnityPackageHub) ·
[Unity Utils](https://github.com/wagenheimer/UnityUtils) ·
[Build Pipeline](https://github.com/wagenheimer/UnityBuildPipeline) ·
[Cloud Save](https://github.com/wagenheimer/UnityCloudSave) ·
[Rate Control](https://github.com/wagenheimer/UnityRateControl) ·
[IAP Helper](https://github.com/wagenheimer/UnityIAPHelper) ·
[LevelPlay Helper](https://github.com/wagenheimer/UnityLevelPlayHelper) ·
[Native Social](https://github.com/wagenheimer/UnityNativeSocial) ·
[Rewired Helper](https://github.com/wagenheimer/RewiredHelper) ·
[Timeline Typewriter](https://github.com/wagenheimer/UnityTimelineTypewriter) ·
[Tk2d Porter](https://github.com/wagenheimer/UnityTk2dPorter) ·
[Tracking](https://github.com/wagenheimer/UnityTracking) ·
[I2 Localization](https://github.com/wagenheimer/UnityI2Localization)

Explore every project at **[wagenheimer.com/opensource](https://wagenheimer.com/opensource)**.
