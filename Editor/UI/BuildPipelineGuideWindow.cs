using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal class BuildPipelineGuideWindow : EditorWindow
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Documentation & Integration Guide", priority = 111)]
        public static void Open()
        {
            var w = GetWindow<BuildPipelineGuideWindow>("Build Pipeline — Documentation & Guide");
            w.minSize = new Vector2(560, 520);
            w.Show();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexGrow = 1;
            BuildPipelineUIStyle.Apply(root);

            // Banner
            var banner = new VisualElement();
            banner.style.backgroundColor = new Color(0.18f, 0.55f, 0.90f);
            banner.style.paddingTop = 14;
            banner.style.paddingBottom = 14;
            banner.style.paddingLeft = 16;
            banner.style.paddingRight = 16;
            banner.style.marginBottom = 10;

            var titleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var iconLabel = new Label("🚀") { style = { fontSize = 22, marginRight = 8 } };
            var titleLabel = new Label("Unity Build Pipeline Guide")
            {
                style =
                {
                    fontSize = 18,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = Color.white
                }
            };
            titleRow.Add(iconLabel);
            titleRow.Add(titleLabel);
            banner.Add(titleRow);

            var subtitle = new Label("Complete guide for multi-store builds, matrices, Unity CLI & automation")
            {
                style =
                {
                    fontSize = 11,
                    marginTop = 4,
                    color = new Color(0.88f, 0.94f, 1f)
                }
            };
            banner.Add(subtitle);
            root.Add(banner);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 12;
            scroll.style.paddingRight = 12;
            scroll.style.paddingBottom = 20;
            root.Add(scroll);

            // 1. Two-Tier Architecture
            var card1 = BuildPipelineUIStyle.CreateCard("#1 Two-Tier Architecture (Build vs Runtime)",
                "The build pipeline cleanly separates build infrastructure from gameplay:\n\n" +
                "• ProjectBuildConfig.asset (Editor): Stores output paths, keystores, publisher definitions, and matrix presets. Has NO version/build-number fields — it only holds a reference to your GameConfig. Never shipped in builds.\n" +
                "• GameConfig.asset (Runtime, e.g. \"YourGame.asset\"): The single SOURCE OF TRUTH for GameVersion, AndroidBundleVersionCode, and iOSBuildNumber. Kept in your project and referenced by MainBase.cs. Automatically synchronized before every build with active publisher, language, version, and cheat mode.\n\n" +
                "Rule of thumb: version/build numbers → always edit on GameConfig.asset. Paths/keystore/publishers → always edit on ProjectBuildConfig.asset.");

            card1.Add(CreateCodeBox(
                "// Access runtime settings in your game code:\n" +
                "Publisher activeStore = Main.main.Config.Publisher;\n" +
                "GameLanguage lang = Main.main.Config.GameLanguage;\n" +
                "string version = MainBase.Version;\n" +
                "bool isCheat = MainBase.Config.CheatMode;"));
            scroll.Add(card1);

            // Comparison Table
            scroll.Add(CreateComparisonTable());

            // 2. Where Build Numbers Live
            var card2 = BuildPipelineUIStyle.CreateCard("#2 Where Build Numbers Really Live (and git diffs)",
                "AndroidBundleVersionCode / iOSBuildNumber are stored ONLY on GameConfig.asset. Editing them from the Inspector (or the +1 / Sync buttons) also pushes the value live into PlayerSettings.Android.bundleVersionCode / PlayerSettings.iOS.buildNumber — which is what ProjectSettings/ProjectSettings.asset stores on disk.\n\n" +
                "Both writes only mark the assets 'dirty' in memory (EditorUtility.SetDirty). Nothing reaches disk — and therefore nothing shows up in 'git status' — until Unity actually saves the project.\n\n" +
                "If you bumped the number and see no git diff: press Ctrl+S (File → Save Project), or use the '💾 Save Now' button that appears at the top of the Build Pipeline window whenever there are unsaved changes. Then check git again:\n" +
                "• Assets/_Game/YourGame.asset (GameConfig — source of truth)\n" +
                "• ProjectSettings/ProjectSettings.asset (Unity's PlayerSettings mirror)");
            scroll.Add(card2);

            // 3. Quick Build Dashboard
            var card3 = BuildPipelineUIStyle.CreateCard("#3 Quick Build Dashboard",
                "Open Tools → Build Pipeline → Open Build Window.\n\n" +
                "The 'Quick Build' tab provides 1-click builds for the most common configurations:\n" +
                "• Big Fish Games, Steam, GameHouse, Green Sauce Games (Windows & macOS)\n" +
                "• Google Play (Full/Free AAB or Debug APK)\n" +
                "• Amazon Appstore (Full/Free APK)\n" +
                "• iOS App Store (Full/Free Xcode projects)\n" +
                "• Mac App Store & Mac Game Store");
            scroll.Add(card3);

            // 4. Matrix Batch Builder
            var card4 = BuildPipelineUIStyle.CreateCard("#4 Language & Publisher Matrix Batch Builder",
                "Easily generate dozens or hundreds of localized builds in one run:\n\n" +
                "1. Select the target Publishers (Windows desktop stores).\n" +
                "2. Select the target Languages.\n" +
                "3. Toggle Cheat ON and/or Cheat OFF.\n" +
                "4. Check 'Development Build' if generating testing builds.\n" +
                "5. Click 'Generate Builds'. The system displays real-time progress and handles cancellation safely.");
            var openWinBtn = new Button(BuildPipelineWindow.ShowWindow) { text = "⚡ Open Build Window" };
            openWinBtn.style.alignSelf = Align.FlexStart;
            openWinBtn.style.marginTop = 6;
            card4.Add(openWinBtn);
            scroll.Add(card4);

            // 5. Headless Unity CLI
            var card5 = BuildPipelineUIStyle.CreateCard("#5 100% Headless Unity CLI Automation",
                "Build any target or matrix directly from command line, CI/CD, or automated scripts without opening the Unity Editor GUI:\n\n" +
                "• Never hangs on EditorUtility.DisplayDialog (dialogs are bypassed in batchmode).\n" +
                "• Exits with standard return codes: 0 on success, 1 on failure.\n\n" +
                "CLI Arguments:\n" +
                "  -publisher <Name>      Target publisher (BigFish, Steam, GoogleAndroidFull, etc.)\n" +
                "  -language <Code/Name>  Language (en, de, fr, English, German, etc.)\n" +
                "  -platform <Platform>   Windows64, Android, macOS, iOS\n" +
                "  -cheat <true|false>    Enable or disable Cheat Mode\n" +
                "  -version <X.Y.Z>       Override version numbers before building");
            card5.Add(CreateCodeBox(
                "& \"Unity.exe\" -batchmode -nographics -quit `\n" +
                "  -projectPath \"k:\\Games\\Green Sauce Games\\YourProject\" `\n" +
                "  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.Build `\n" +
                "  -publisher BigFish `\n" +
                "  -language en `\n" +
                "  -logFile \"build_bigfish.log\""));
            scroll.Add(card5);

            // 6. PowerShell Script
            var card6 = BuildPipelineUIStyle.CreateCard("#6 Automated PowerShell Script (build.ps1)",
                "A cross-project PowerShell runner is included in Tools/build.ps1.\n" +
                "It automatically finds your Unity Editor from ProjectSettings/ProjectVersion.txt and handles batch execution:\n\n" +
                "Examples:\n" +
                "• Single build:\n" +
                "  ./build.ps1 -projectPath \"k:\\Games\\...\\YourGame\" -publisher \"BigFish\" -language \"en\"\n\n" +
                "• Matrix build:\n" +
                "  ./build.ps1 -projectPath \"k:\\Games\\...\\YourGame\" -matrix -matrixPublishers \"BigFish,Steam\" -matrixLanguages \"en,de\"");
            card6.Add(CreateCodeBox("./build.ps1 -publisher BigFish -language en -development"));
            scroll.Add(card6);

            // 7. Post-Processing & Archiving
            var card7 = BuildPipelineUIStyle.CreateCard("#7 Post-Processing Lifecycle & Archiving",
                "The pipeline includes automated post-build processors:\n\n" +
                "• CopyPublisherSplashStep: Copies splash_{publisher}.jpg into StreamingAssets/splash1.jpg for stores requiring custom logos.\n" +
                "• CleanupObsoleteFilesStep: Deletes UnityCrashHandler*.exe and redundant backup folders.\n" +
                "• ZipArchiveStep: Creates ZIP archives in your Dropbox/archive folder using built-in .NET ZipFile (or 7-Zip if configured in ProjectBuildConfig).");
            scroll.Add(card7);

            // 8. Remote Keystore Vault
            var card8 = BuildPipelineUIStyle.CreateCard("#8 Remote Keystore Vault & Credentials Security",
                "Keep credentials and signing certificates out of git entirely with the Centralized Keystore Vault:\n\n" +
                "• Remote Vault Mode: Before compilation, UnityBuildPipeline calls /api/vault/keystore/{id} (or /by-bundle/{bundleId}), downloads the .keystore into Library/KeystoreCache/ (gitignored), and injects passwords purely in memory.\n" +
                "• Post-Build Hygiene: Passwords in PlayerSettings.Android are immediately zeroed out after build execution.\n" +
                "• Open-Source Self-Hosting: Anyone can host their own vault using any web framework (ASP.NET, Node.js, Go, PHP) by returning JSON with { keystoreBase64, keystorePass, keyAliasName, keyAliasPass } protected by Bearer auth.\n" +
                "• Admin Portal: Integrated into WagenheimerDotCom (/admin/keystores) with profile management, file upload, bundle mapping, and copyable secrets.");
            card8.Add(CreateCodeBox(
                "$env:VAULT_SECRET_TOKEN = \"my-secret-token\"\n" +
                "./build.ps1 -publisher GoogleAndroidFree -development -autoRun"));
            scroll.Add(card8);

            // 9. Android Auto-Run & Logcat
            var card9 = BuildPipelineUIStyle.CreateCard("#9 Android Auto-Run & Logcat Integration",
                "When building Android for testing, use -autoRun (or enable Auto-Run in the window):\n\n" +
                "• Automatically generates an .apk (instead of .aab) and deploys directly to connected USB / Wi-Fi adb devices.\n" +
                "• Launches the game on the device immediately after installation.\n" +
                "• In Unity Editor, automatically opens the Android Logcat window (Window → Analysis → Android Logcat) to stream runtime logs without manual setup.");
            scroll.Add(card9);

            // 10. Extensible Lifecycle Hooks
            var card10 = BuildPipelineUIStyle.CreateCard("#10 Extensible Lifecycle Hooks",
                "Extend build logic for any game without editing the package code by registering custom steps:");
            card10.Add(CreateCodeBox(
                "using Wagenheimer.BuildPipeline.Editor;\n" +
                "using UnityEditor.Build.Reporting;\n" +
                "using UnityEditor;\n\n" +
                "[InitializeOnLoad]\n" +
                "public static class GameHooks\n" +
                "{\n" +
                "    static GameHooks() {\n" +
                "        BuildPipelineRunner.RegisterPostStep(new MyDiscordNotifier());\n" +
                "    }\n" +
                "}\n\n" +
                "public class MyDiscordNotifier : IPostBuildStep {\n" +
                "    public int Order => 100;\n" +
                "    public bool ExecutePostBuild(BuildContext ctx, BuildReport report) {\n" +
                "        // Notify webhook with ctx.ProjectName and report.summary.totalSize\n" +
                "        return true;\n" +
                "    }\n" +
                "}"));
            scroll.Add(card10);

            // 11. Migration Wizard
            var card11 = BuildPipelineUIStyle.CreateCard("#11 1-Click Migration Wizard for Other Projects",
                "To migrate any other Green Sauce Games project (Secrets of Magic, Ancient Relics, etc.):\n\n" +
                "1. Add \"com.wagenheimer.buildpipeline\" to Packages/manifest.json.\n" +
                "2. Delete local duplicate GameConfig.cs and GameConfigEditor.cs.\n" +
                "3. Click Tools → Build Pipeline → Migrate or Create Project Config.\n" +
                "4. All bundle IDs, names, and paths will be auto-populated into ProjectBuildConfig.asset!");

            var migBtn = new Button(() => EditorApplication.ExecuteMenuItem("Tools/Wagenheimer/Build Pipeline/Migrate or Create Project Config"))
            {
                text = "⚡ Run Migration Wizard Now"
            };
            migBtn.style.alignSelf = Align.FlexStart;
            migBtn.style.marginTop = 6;
            card11.Add(migBtn);
            scroll.Add(card11);
        }

        private VisualElement CreateComparisonTable()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 10;

            // Runtime Column
            var runtimeCol = new VisualElement();
            runtimeCol.style.flexGrow = 1;
            runtimeCol.style.flexBasis = Length.Percent(50);
            runtimeCol.style.marginRight = 5;
            runtimeCol.style.backgroundColor = new Color(0.10f, 0.22f, 0.16f);
            runtimeCol.style.borderTopWidth = 3;
            runtimeCol.style.borderTopColor = new Color(0.30f, 0.85f, 0.55f);
            runtimeCol.style.paddingTop = 8;
            runtimeCol.style.paddingBottom = 8;
            runtimeCol.style.paddingLeft = 10;
            runtimeCol.style.paddingRight = 10;

            var rTitle = new Label("🎮 GameConfig.asset");
            rTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            rTitle.style.color = Color.white;
            runtimeCol.Add(rTitle);

            var rBadge = BuildPipelineUIStyle.CreateBadge("SHIPS INSIDE BUILD", "ok");
            rBadge.style.alignSelf = Align.FlexStart;
            rBadge.style.marginTop = 4;
            rBadge.style.marginBottom = 6;
            runtimeCol.Add(rBadge);

            runtimeCol.Add(new Label("• Version (Major.Minor.Build)"));
            runtimeCol.Add(new Label("• Android Bundle Version Code"));
            runtimeCol.Add(new Label("• iOS / macOS Build Number"));
            runtimeCol.Add(new Label("• Bundle IDs, icons, game title"));
            runtimeCol.Add(new Label("• Read by the game at runtime"));

            row.Add(runtimeCol);

            // Editor Column
            var editorCol = new VisualElement();
            editorCol.style.flexGrow = 1;
            editorCol.style.flexBasis = Length.Percent(50);
            editorCol.style.marginLeft = 5;
            editorCol.style.backgroundColor = new Color(0.20f, 0.16f, 0.26f);
            editorCol.style.borderTopWidth = 3;
            editorCol.style.borderTopColor = new Color(0.70f, 0.50f, 0.95f);
            editorCol.style.paddingTop = 8;
            editorCol.style.paddingBottom = 8;
            editorCol.style.paddingLeft = 10;
            editorCol.style.paddingRight = 10;

            var eTitle = new Label("🛠️ ProjectBuildConfig.asset");
            eTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            eTitle.style.color = Color.white;
            editorCol.Add(eTitle);

            var eBadge = BuildPipelineUIStyle.CreateBadge("NEVER SHIPS (Editor-only)", "info");
            eBadge.style.alignSelf = Align.FlexStart;
            eBadge.style.marginTop = 4;
            eBadge.style.marginBottom = 6;
            editorCol.Add(eBadge);

            editorCol.Add(new Label("• Output paths (Dropbox, local)"));
            editorCol.Add(new Label("• Android keystore & remote vault"));
            editorCol.Add(new Label("• Publisher profiles & store rules"));
            editorCol.Add(new Label("• Enabled localization languages"));
            editorCol.Add(new Label("• Only exists during build phase"));

            row.Add(editorCol);

            return row;
        }

        private VisualElement CreateCodeBox(string code)
        {
            var box = new VisualElement();
            box.AddToClassList("bp-code-box");
            box.style.marginTop = 6;

            var label = new Label(code);
            label.AddToClassList("bp-code-text");
            label.style.unityFontStyleAndWeight = FontStyle.Normal;
            box.Add(label);

            return box;
        }
    }
}
