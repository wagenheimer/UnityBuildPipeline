# Unity Build Pipeline (`com.wagenheimer.buildpipeline`)

A modular, multi-project build automation pipeline for Unity. Designed for multi-store portfolio game development, featuring publisher presets, language matrix generation, post-processing hooks, a modern UI Toolkit interface, and 100% headless automation via Unity CLI and PowerShell.

[![openupm](https://img.shields.io/npm/v/com.wagenheimer.buildpipeline?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.wagenheimer.buildpipeline/)
[![License: MIT](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

---

## ✨ Features

- **Store & Publisher Management**: Out-of-the-box profiles for Steam, Big Fish Games, GameHouse, Alawar, itch.io, Google Play, Amazon Appstore, iOS App Store, Mac App Store, Samsung Galaxy Store, and direct site distribution.
- **Language Matrix Batch Generator**: Automatically compile dozens or hundreds of target combinations (Publishers × Languages × Cheat Mode ON/OFF × Dev Builds) with real-time progress and cancellation.
- **Headless Unity CLI & CI/CD**: 100% callable outside Unity via `-executeMethod` and PowerShell. Never hangs on `EditorUtility.DisplayDialog`. Returns proper exit codes (`0` on success, `1` on error).
- **Post-Processing Lifecycle**: Built-in splash screen copying (`splash_{publisher}.jpg` into `StreamingAssets/splash1.jpg`), obsolete file cleanup (`UnityCrashHandler*.exe`), and automatic ZIP archiving (.NET `ZipFile` or 7-Zip).
- **Modern UI Toolkit Window**: Clean, responsive EditorWindow with Quick Build action cards, Matrix configuration, and live CLI command generator.
- **Unified Runtime `GameConfig`**: Single centralized ScriptableObject for versioning, date stamps, publisher/language enums, cursor textures, and flags, completely backward compatible with existing gameplay code (`MainBase.cs`, etc.).

---

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
    "com.wagenheimer.buildpipeline": "https://github.com/wagenheimer/UnityBuildPipeline.git#v1.0.0"
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

1. Go to **Tools > Build Pipeline > Open Build Window** (or `Window > Build Pipeline`).
2. If this is the first time running in the project, the system will automatically create `Assets/_Game/Settings/ProjectBuildConfig.asset` and link your existing `GameConfig.asset`.
3. Use the **Quick Build** tab to build for any common target with a single click.
4. Use the **Matrix Batch Builder** tab to select multiple publishers and languages, then click **Generate Builds**.

---

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
`warnings`, `durationSeconds`, `errorMessage`.

---

## 🔧 CLI Argument Reference

| Flag | Values | Description |
|---|---|---|
| `-buildProfile` | Profile `id` (or publisher name) | **Preferred.** Selects a `PublisherProfile` by its stable `id`; overrides `-publisher`/`-platform`. |
| `-publisher` | `BigFish`, `Steam`, `GoogleAndroidFull`, etc. | Target store/publisher profile (used when `-buildProfile` is absent). |
| `-language` | `en`, `de`, `fr`, `English`, `German`, etc. | Language code or name. |
| `-platform` | `Windows64`, `Android`, `macOS`, `iOS`, `Linux64`, `WebGL` | Target platform. |
| `-defines` | `"A;B;C"` | Extra scripting define symbols for this build, applied then restored. |
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
| `-vaultUrl` | HTTPS URL | Endpoint for remote keystore vault. |
| `-vaultProfile` | Profile ID | Slug identifier of the keystore in the vault. |
| `-vaultToken` | Bearer Token | Secret authentication token for the vault. |
| `-keystoreSource` | `LocalDisk` / `RemoteVault` | Choose between local disk or remote vault. |

---

## 🔐 Centralized Keystore & Credentials Vault

To ensure digital certificates (`.keystore` / `.jks`) and signing passwords are never committed into git repositories, `com.wagenheimer.buildpipeline` integrates an **Online Keystore Vault** architecture.

### How It Works
1. **Fetch on Demand**: Before compiling an Android build, `SetupKeystoreStep` calls the configured Vault API via HTTPS using Bearer token authentication.
2. **Local Caching**: The binary `.keystore` is cached into `Library/KeystoreCache/{filename}` (which is already gitignored).
3. **In-Memory Injection**: Passwords (`keystorePass`, `keyaliasPass`) and alias names are injected directly into `PlayerSettings.Android` in memory.
4. **Post-Build Zeroing**: After the build completes (success or failure), `PlayerSettings.Android` passwords are wiped clean to prevent leakage.

### Self-Hosting Your Own Vault
This build pipeline is completely vendor-neutral. Any developer or studio can host their own vault using ASP.NET Core, Node.js, Python, or Go by implementing two simple endpoints:

#### Endpoint 1: Fetch Keystore by Profile ID
`GET /api/vault/keystore/{profileId}`
- **Headers**: `Authorization: Bearer <SecretToken>`
- **Response JSON**:
```json
{
  "profileId": "greensauce-master",
  "name": "Green Sauce Games (Upload Certificate)",
  "company": "Green Sauce Games",
  "keystoreFileName": "GreenSauceKeystore (Upload Certificate).keystore",
  "keystoreBase64": "<base64-encoded-binary-keystore>",
  "keystorePass": "secret-pass",
  "keyAliasName": "upload key",
  "keyAliasPass": "secret-pass"
}
```

#### Endpoint 2: Fetch Keystore by Bundle ID (Auto-Discovery)
`GET /api/vault/by-bundle/{bundleId}`
- Allows Unity to omit the profile ID entirely and fetch the exact keystore matching the project's Android package name (e.g. `com.company.gamename`).

---

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
