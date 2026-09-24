using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    [CustomEditor(typeof(ProjectBuildConfig))]
    public class ProjectBuildConfigCustomEditor : UnityEditor.Editor
    {
        private PlatformType _quickPlatform = PlatformType.Windows64;
        private Publisher _quickPublisher = Publisher.BigFish;
        private GameLanguage _quickLanguage = GameLanguage.AutoDetect;
        private bool _quickCheat = false;
        private bool _quickDevBuild = false;
        private bool _quickAppBundle = true;
        private bool _quickAutoRun = false;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            BuildPipelineUIStyle.Apply(root);

            var config = (ProjectBuildConfig)target;

            // 1. Hero Header
            var headerCard = BuildPipelineUIStyle.CreateCard("Project Build Configuration", "Global project-level paths, keystores, and store publisher definitions");

            var topActions = new VisualElement();
            topActions.style.flexDirection = FlexDirection.Row;
            topActions.style.marginBottom = 6;

            var openWinBtn = new Button(BuildPipelineWindow.ShowWindow)
            {
                text = "🚀 Open Build Pipeline Window"
            };
            openWinBtn.AddToClassList("bp-btn");
            openWinBtn.AddToClassList("bp-btn--primary");
            openWinBtn.style.flexGrow = 1;
            openWinBtn.style.height = 30;
            openWinBtn.style.fontSize = 12;
            topActions.Add(openWinBtn);

            var guideBtn = new Button(BuildPipelineGuideWindow.Open)
            {
                text = "📖 Guide"
            };
            guideBtn.AddToClassList("bp-btn");
            guideBtn.style.height = 30;
            guideBtn.style.fontSize = 11;
            topActions.Add(guideBtn);

            headerCard.Add(topActions);
            root.Add(headerCard);

            // 2. Save Status Bar
            root.Add(BuildPipelineUIStyle.CreateSaveStatusBar(config));

            // 3. Quick Build Launcher Card
            var quickCard = BuildPipelineUIStyle.CreateCard("⚡ Run Build Now (Quick Build)", "Execute a build directly from this inspector without opening the main window");

            var row1 = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
            var platField = new EnumField("Platform", _quickPlatform) { style = { flexGrow = 1, marginRight = 6 } };
            platField.RegisterValueChangedCallback(e => _quickPlatform = (PlatformType)e.newValue);
            row1.Add(platField);

            var pubField = new EnumField("Publisher", _quickPublisher) { style = { flexGrow = 1 } };
            pubField.RegisterValueChangedCallback(e => _quickPublisher = (Publisher)e.newValue);
            row1.Add(pubField);
            quickCard.Add(row1);

            var row2 = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            var langField = new EnumField("Language", _quickLanguage) { style = { flexGrow = 1 } };
            langField.RegisterValueChangedCallback(e => _quickLanguage = (GameLanguage)e.newValue);
            row2.Add(langField);
            quickCard.Add(row2);

            var flagsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 8 } };
            var cheatTog = new Toggle("Cheat") { value = _quickCheat, style = { marginRight = 10 } };
            cheatTog.RegisterValueChangedCallback(e => _quickCheat = e.newValue);
            flagsRow.Add(cheatTog);

            var devTog = new Toggle("Development") { value = _quickDevBuild, style = { marginRight = 10 } };
            devTog.RegisterValueChangedCallback(e => _quickDevBuild = e.newValue);
            flagsRow.Add(devTog);

            var aabTog = new Toggle(".aab (App Bundle)") { value = _quickAppBundle, style = { marginRight = 10 } };
            aabTog.RegisterValueChangedCallback(e => _quickAppBundle = e.newValue);
            flagsRow.Add(aabTog);

            var autoTog = new Toggle("Auto Run") { value = _quickAutoRun };
            autoTog.RegisterValueChangedCallback(e => _quickAutoRun = e.newValue);
            flagsRow.Add(autoTog);
            quickCard.Add(flagsRow);

            var runBtn = new Button(() => RunQuickBuild(config))
            {
                text = "▶️ RUN QUICK BUILD NOW"
            };
            runBtn.AddToClassList("bp-btn");
            runBtn.AddToClassList("bp-btn--success");
            runBtn.style.height = 32;
            runBtn.style.fontSize = 12;
            quickCard.Add(runBtn);

            root.Add(quickCard);

            // 4. Runtime GameConfig & Project Identity
            var identityCard = BuildPipelineUIStyle.CreateCard("Runtime Reference & Project Identity");
            identityCard.Add(new PropertyField(serializedObject.FindProperty("gameConfig"), "Runtime GameConfig.asset"));
            identityCard.Add(new PropertyField(serializedObject.FindProperty("projectName"), "Project Name"));
            identityCard.Add(new PropertyField(serializedObject.FindProperty("defaultBundleIdentifier"), "Default Bundle ID"));
            identityCard.Add(new PropertyField(serializedObject.FindProperty("gameNameJapanese"), "Japanese Game Name"));
            root.Add(identityCard);

            // 5. Android Keystore & Remote Vault
            var vaultCard = BuildPipelineUIStyle.CreateCard("Android Keystore & Remote Vault");
            vaultCard.Add(new PropertyField(serializedObject.FindProperty("keystoreSource"), "Keystore Source"));

            var localBox = new VisualElement();
            localBox.Add(new PropertyField(serializedObject.FindProperty("androidKeystorePath"), "Keystore Path (Windows)"));
            localBox.Add(new PropertyField(serializedObject.FindProperty("androidKeystorePathMac"), "Keystore Path (macOS)"));
            localBox.Add(new PropertyField(serializedObject.FindProperty("androidKeyAlias"), "Key Alias"));
            localBox.Add(new PropertyField(serializedObject.FindProperty("androidKeystorePassFallback"), "Keystore Password"));
            localBox.Add(new PropertyField(serializedObject.FindProperty("androidKeyaliasPassFallback"), "Keyalias Password"));
            vaultCard.Add(localBox);

            var remoteBox = new VisualElement();
            remoteBox.Add(new PropertyField(serializedObject.FindProperty("vaultUrl"), "Vault Endpoint URL"));
            remoteBox.Add(new PropertyField(serializedObject.FindProperty("vaultProfileId"), "Vault Profile ID"));
            remoteBox.Add(new PropertyField(serializedObject.FindProperty("vaultTokenFallback"), "Vault Token (Fallback)"));
            vaultCard.Add(remoteBox);

            var testVaultBtn = new Button(async () =>
            {
                EditorUtility.DisplayProgressBar("Vault Connection", "Connecting...", 0.5f);
                try
                {
                    var result = await KeystoreVaultClient.FetchCredentialsAsync(
                        config.vaultUrl,
                        config.vaultProfileId,
                        PlayerSettings.applicationIdentifier,
                        KeystoreVaultClient.GetEffectiveToken(config));

                    EditorUtility.ClearProgressBar();
                    if (result.Success)
                    {
                        EditorUtility.DisplayDialog("Vault Connection", $"✅ Success!\nProfile: {result.Credentials.ProfileId}\nFile: {result.Credentials.KeystoreFileName}", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Vault Warning", $"Could not retrieve credentials:\n{result.Message}", "OK");
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            })
            { text = "⚡ Test Vault Connection" };
            testVaultBtn.AddToClassList("bp-btn");
            testVaultBtn.style.marginTop = 6;
            vaultCard.Add(testVaultBtn);

            root.Add(vaultCard);

            // 6. Output Paths & Scenes
            var pathsCard = BuildPipelineUIStyle.CreateCard("Output Directories & Scene Paths");
            pathsCard.Add(new PropertyField(serializedObject.FindProperty("buildOutputRoot"), "Build Output (Windows)"));
            pathsCard.Add(new PropertyField(serializedObject.FindProperty("macBuildOutputRoot"), "Build Output (macOS)"));
            pathsCard.Add(new PropertyField(serializedObject.FindProperty("splashMasterFolder"), "Splash Master Folder"));
            pathsCard.Add(new PropertyField(serializedObject.FindProperty("publisherSplashScenePath"), "Publisher Splash Scene"));
            pathsCard.Add(new PropertyField(serializedObject.FindProperty("defaultSplashScenePath"), "Default Splash Scene"));
            pathsCard.Add(new PropertyField(serializedObject.FindProperty("levelEditorScenePath"), "Level Editor Scene"));
            root.Add(pathsCard);

            // 7. Publishers & Stores
            var pubsCard = BuildPipelineUIStyle.CreateCard("Publishers & Store Profiles");
            var pubProp = serializedObject.FindProperty("publishers");
            pubsCard.Add(new PropertyField(pubProp, "Configured Publishers"));

            var restorePubsBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Restore Default Publishers", "Reset publisher list to default presets?", "RESTORE", "CANCEL"))
                {
                    config.publishers = PublisherProfile.GetDefaultProfiles();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                }
            })
            { text = "↺ Restore Default Publishers List" };
            restorePubsBtn.AddToClassList("bp-btn");
            restorePubsBtn.style.marginTop = 6;
            pubsCard.Add(restorePubsBtn);

            root.Add(pubsCard);

            // 8. Languages Matrix
            var langsCard = BuildPipelineUIStyle.CreateCard("Languages & Localizations");
            var langProp = serializedObject.FindProperty("languages");
            langsCard.Add(new PropertyField(langProp, "Configured Languages"));

            var restoreLangsBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Restore Default Languages", "Reset language list to default presets?", "RESTORE", "CANCEL"))
                {
                    config.languages = LanguageProfile.GetDefaultLanguages();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                }
            })
            { text = "↺ Restore Default Languages List" };
            restoreLangsBtn.AddToClassList("bp-btn");
            restoreLangsBtn.style.marginTop = 6;
            langsCard.Add(restoreLangsBtn);

            root.Add(langsCard);

            // 9. Archiving & Compression
            var zipCard = BuildPipelineUIStyle.CreateCard("Archiving & 7-Zip Compression");
            zipCard.Add(new PropertyField(serializedObject.FindProperty("enableZipArchiving"), "Enable ZIP Archiving"));
            zipCard.Add(new PropertyField(serializedObject.FindProperty("useExternalSevenZip"), "Use External 7-Zip"));
            zipCard.Add(new PropertyField(serializedObject.FindProperty("sevenZipExecutable"), "7-Zip Executable Path"));
            root.Add(zipCard);

            return root;
        }

        private void RunQuickBuild(ProjectBuildConfig config)
        {
            if (config.publishers == null || config.publishers.Count == 0)
            {
                EditorUtility.DisplayDialog("Quick Build", "No publisher list configured.\nUse 'Restore Default Publishers List' below.", "OK");
                return;
            }

            var prof = config.publishers.FirstOrDefault(p => p.publisher == _quickPublisher);
            var ctx = new BuildContext
            {
                Config = config,
                Publisher = _quickPublisher,
                PublisherProfile = prof,
                Language = _quickLanguage,
                Platform = _quickPlatform,
                CheatMode = _quickCheat,
                DevelopmentBuild = _quickDevBuild,
                Demo = prof != null && prof.isDemo,
                AppBundle = _quickAppBundle,
                AutoRun = _quickAutoRun
            };

            AssetDatabase.SaveAssets();

            var res = BuildPipelineRunner.Execute(ctx);
            if (res.Success)
            {
                var choice = EditorUtility.DisplayDialogComplex(
                    "Build Complete",
                    $"Build finished in {res.Duration:mm\\:ss}!\n\nPath: {res.OutputPath}",
                    "▶️ Run",
                    "OK",
                    "📂 Open Folder");

                var entry = BuildHistory.LastSuccessful;
                if (choice == 0 && entry != null)
                    BuildHistory.Run(entry);
                else if (choice == 2)
                    BuildHistory.OpenFolder(entry ?? new BuildHistoryEntry { outputPath = res.OutputPath });
            }
            else
            {
                EditorUtility.DisplayDialog("Build Failed", $"Build failed with {res.TotalErrors} error(s).\nCheck Console for details.", "OK");
            }
        }
    }
}
