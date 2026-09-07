using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal class BuildPipelineGuideWindow : EditorWindow
    {
        Vector2 _scroll;

        static Color ColBg => EditorGUIUtility.isProSkin
            ? new Color(0.16f, 0.16f, 0.18f) : new Color(0.82f, 0.82f, 0.84f);
        static Color ColCard => EditorGUIUtility.isProSkin
            ? new Color(0.20f, 0.20f, 0.22f) : new Color(0.90f, 0.90f, 0.92f);
        static Color ColCode => EditorGUIUtility.isProSkin
            ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.95f, 0.95f, 0.97f);
        static Color ColText => EditorGUIUtility.isProSkin
            ? new Color(0.92f, 0.92f, 0.95f) : Color.black;
        static Color ColDim => EditorGUIUtility.isProSkin
            ? new Color(0.55f, 0.55f, 0.60f) : new Color(0.30f, 0.30f, 0.33f);
        static readonly Color ColAccent = new(0.18f, 0.55f, 0.90f);
        static readonly Color ColCodeText = new(0.65f, 0.85f, 0.45f);

        [MenuItem("Tools/Wagenheimer/Build Pipeline/Documentation & Integration Guide", priority = 0)]
        [MenuItem("Tools/Build Pipeline/Documentation & Integration Guide", priority = 0)]
        public static void Open()
        {
            var w = GetWindow<BuildPipelineGuideWindow>("Build Pipeline — Documentation & Guide");
            w.minSize = new Vector2(520, 480);
            w.Show();
        }

        void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 54), ColAccent);
            GUILayout.Space(8);
            var bannerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("🚀  Unity Build Pipeline Guide", bannerStyle, GUILayout.ExpandWidth(true));
            var subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.88f, 0.93f, 1f) },
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("Complete guide for multi-store builds, matrices, Unity CLI & automation", subStyle);
            GUILayout.Space(6);

            EditorGUI.DrawRect(new Rect(0, 54, position.width, position.height - 54), ColBg);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawCard(1, "Two-Tier Architecture (Build vs Runtime)",
                "The build pipeline cleanly separates build infrastructure from gameplay:\n\n" +
                "• ProjectBuildConfig.asset (Editor): Stores paths, keystores, publisher definitions, and matrix presets. Never shipped in builds.\n" +
                "• GameConfig.asset (Runtime): Kept in your project and referenced by MainBase.cs. Automatically synchronized before every build with the active publisher, language, version, and cheat mode.",
                code: "// Access runtime settings in your game code:\nPublisher activeStore = Main.main.Config.Publisher;\nGameLanguage lang = Main.main.Config.GameLanguage;\nstring version = MainBase.Version;\nbool isCheat = MainBase.Config.CheatMode;");

            DrawCard(2, "Quick Build Dashboard",
                "Open Tools → Build Pipeline → Open Build Window (or Window → Build Pipeline).\n\n" +
                "The 'Quick Build' tab provides 1-click builds for the most common configurations:\n" +
                "• Big Fish Games, Steam, GameHouse, Green Sauce Games\n" +
                "• Google Play (Full/Free AAB or Debug APK)\n" +
                "• Amazon Appstore (Full/Free APK)\n" +
                "• iOS App Store (Full/Free Xcode projects)\n" +
                "• Mac App Store & Mac Game Store");

            DrawCard(3, "Language & Publisher Matrix Batch Builder",
                "Easily generate dozens or hundreds of localized builds in one run:\n\n" +
                "1. Select the target Publishers (Windows desktop stores).\n" +
                "2. Select the target Languages.\n" +
                "3. Toggle Cheat ON and/or Cheat OFF.\n" +
                "4. Check 'Development Build' if generating testing builds.\n" +
                "5. Click 'Generate Builds'. The system displays real-time progress and handles cancellation safely.",
                link: "Open Build Window", url: "Tools/Build Pipeline/Open Build Window");

            DrawCard(4, "100% Headless Unity CLI Automation",
                "Build any target or matrix directly from command line, CI/CD, or automated scripts without opening the Unity Editor GUI:\n\n" +
                "• Never hangs on EditorUtility.DisplayDialog (dialogs are bypassed in batchmode).\n" +
                "• Exits with standard return codes: 0 on success, 1 on failure.\n\n" +
                "CLI Arguments:\n" +
                "  -publisher <Name>      Target publisher (BigFish, Steam, GoogleAndroidFull, etc.)\n" +
                "  -language <Code/Name>  Language (en, de, fr, English, German, etc.)\n" +
                "  -platform <Platform>   Windows64, Android, macOS, iOS\n" +
                "  -cheat <true|false>    Enable or disable Cheat Mode\n" +
                "  -version <X.Y.Z>       Override version numbers before building",
                code: "& \"Unity.exe\" -batchmode -nographics -quit `\n" +
                      "  -projectPath \"k:\\Games\\Green Sauce Games\\Storm-Tale2\" `\n" +
                      "  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.Build `\n" +
                      "  -publisher BigFish `\n" +
                      "  -language en `\n" +
                      "  -logFile \"build_bigfish.log\"");

            DrawCard(5, "Automated PowerShell Script (build.ps1)",
                "A cross-project PowerShell runner is included in Tools/build.ps1.\n" +
                "It automatically finds your Unity Editor from ProjectSettings/ProjectVersion.txt and handles batch execution:\n\n" +
                "Examples:\n" +
                "• Single build:\n" +
                "  ./build.ps1 -projectPath \"k:\\Games\\...\\Storm-Tale2\" -publisher \"BigFish\" -language \"en\"\n\n" +
                "• Matrix build:\n" +
                "  ./build.ps1 -projectPath \"k:\\Games\\...\\Storm-Tale2\" -matrix -matrixPublishers \"BigFish,Steam\" -matrixLanguages \"en,de\"",
                code: "# Run in terminal:\n./build.ps1 -publisher BigFish -language en -development");

            DrawCard(6, "Post-Processing Lifecycle & Archiving",
                "The pipeline includes automated post-build processors:\n\n" +
                "• CopyPublisherSplashStep: Copies splash_{publisher}.jpg into StreamingAssets/splash1.jpg for stores that require custom publisher logos.\n" +
                "• CleanupObsoleteFilesStep: Deletes UnityCrashHandler*.exe and backup folders.\n" +
                "• ZipArchiveStep: Creates ZIP archives in your Dropbox/archive folder using built-in .NET ZipFile (or 7-Zip if configured in ProjectBuildConfig).");

            DrawCard(7, "Remote Keystore Vault & Credentials Security",
                "Keep credentials and signing certificates out of git entirely with the Centralized Keystore Vault:\n\n" +
                "• Remote Vault Mode: Before compilation, UnityBuildPipeline calls /api/vault/keystore/{id} (or /by-bundle/{bundleId}), downloads the .keystore into Library/KeystoreCache/ (gitignored), and injects passwords purely in memory.\n" +
                "• Post-Build Hygiene: Passwords in PlayerSettings.Android are immediately zeroed out after build execution.\n" +
                "• Open-Source Self-Hosting: Anyone can host their own vault using any web framework (ASP.NET, Node.js, Go, PHP) by returning JSON with { keystoreBase64, keystorePass, keyAliasName, keyAliasPass } protected by Bearer auth.\n" +
                "• Admin Portal: Integrated into WagenheimerDotCom (/admin/keystores) with profile management, file upload, bundle mapping, and copyable secrets.",
                code: "// Example PowerShell headless Android build with Vault:\n$env:VAULT_SECRET_TOKEN = \"my-secret-token\"\n./build.ps1 -publisher GoogleAndroidFree -development -autoRun");

            DrawCard(8, "Android Auto-Run & Logcat Integration",
                "When building Android for testing, use -autoRun (or enable Auto-Run in the window):\n\n" +
                "• Automatically generates an .apk (instead of .aab) and deploys directly to connected USB / Wi-Fi adb devices.\n" +
                "• Launches the game on the device immediately after installation.\n" +
                "• In Unity Editor, automatically opens the Android Logcat window (Window → Analysis → Android Logcat) to stream runtime logs without manual setup.");

            DrawCard(9, "Extensible Lifecycle Hooks",
                "Extend build logic for any game without editing the package code by registering custom steps:",
                code: "using Wagenheimer.BuildPipeline.Editor;\nusing UnityEditor.Build.Reporting;\nusing UnityEditor;\n\n[InitializeOnLoad]\npublic static class GameHooks\n{\n    static GameHooks() {\n        BuildPipelineRunner.RegisterPostStep(new MyDiscordNotifier());\n    }\n}\n\npublic class MyDiscordNotifier : IPostBuildStep {\n    public int Order => 100;\n    public bool ExecutePostBuild(BuildContext ctx, BuildReport report) {\n        // Notify webhook with ctx.ProjectName and report.summary.totalSize\n        return true;\n    }\n}");

            DrawCard(10, "1-Click Migration Wizard for Other Projects",
                "To migrate any other Green Sauce Games project (Secrets of Magic, Ancient Relics, etc.):\n\n" +
                "1. Add \"com.wagenheimer.buildpipeline\" to Packages/manifest.json.\n" +
                "2. Delete the local duplicate GameConfig.cs and GameConfigEditor.cs.\n" +
                "3. Click Tools → Build Pipeline → Migrate or Create Project Config.\n" +
                "4. All bundle IDs, names, and paths will be auto-populated into ProjectBuildConfig.asset!",
                link: "Run Migration Wizard Now", url: "Tools/Build Pipeline/Migrate or Create Project Config");

            GUILayout.Space(16);
            EditorGUILayout.EndScrollView();
        }

        void DrawCard(int step, string title, string description, string code = null, string link = null, string url = null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                var numStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13,
                    normal = { textColor = ColAccent },
                    alignment = TextAnchor.MiddleLeft
                };
                GUILayout.Label($"#{step}", numStyle, GUILayout.Width(28));

                var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13,
                    normal = { textColor = ColText },
                    alignment = TextAnchor.MiddleLeft
                };
                GUILayout.Label(title, titleStyle);
            }

            GUILayout.Space(4);
            var descStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                normal = { textColor = ColText }
            };
            EditorGUILayout.LabelField(description, descStyle);

            if (!string.IsNullOrEmpty(code))
            {
                GUILayout.Space(6);
                var codeStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Normal,
                    normal = { textColor = ColCodeText }
                };
                EditorGUILayout.TextArea(code, codeStyle);
            }

            if (!string.IsNullOrEmpty(link) && !string.IsNullOrEmpty(url))
            {
                GUILayout.Space(4);
                if (GUILayout.Button(link, EditorStyles.linkLabel))
                {
                    if (url.StartsWith("http"))
                        Application.OpenURL(url);
                    else
                        EditorApplication.ExecuteMenuItem(url);
                }
            }

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }
    }
}
