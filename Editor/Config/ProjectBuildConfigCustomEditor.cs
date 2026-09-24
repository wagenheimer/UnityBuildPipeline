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
            if (config != null)
            {
                _quickLanguage = config.defaultLanguage;
            }

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

            // 8. Languages & Localizations
            var langsCard = BuildPipelineUIStyle.CreateCard("Languages & Localizations", "Default build language and optional batch matrix configuration");

            var defLangProp = serializedObject.FindProperty("defaultLanguage");
            if (defLangProp != null)
            {
                var defLangField = new PropertyField(defLangProp, "Default Build Language");
                defLangField.tooltip = "Primary language used for builds. Auto Detect produces a multi-language build (standard for mobile stores and modern Steam).";
                langsCard.Add(defLangField);
            }

            var langHint = BuildPipelineUIStyle.CreateCallout(
                "💡 Auto Detect compiles a single multi-language build where the game auto-detects device language or lets the player switch in-game. Select a specific language only when making a dedicated regional or portal-exclusive build.",
                "info");
            langHint.style.marginTop = 6;
            langHint.style.marginBottom = 10;
            langsCard.Add(langHint);

            // Optional Batch Matrix Foldout
            var langProp = serializedObject.FindProperty("languages");
            var matrixFoldout = new Foldout
            {
                text = "Batch Matrix Languages (Optional / Multi-Builds)",
                value = false
            };
            matrixFoldout.style.marginTop = 4;
            matrixFoldout.style.paddingTop = 6;
            matrixFoldout.style.borderTopWidth = 1;
            matrixFoldout.style.borderTopColor = new Color(1f, 1f, 1f, 0.1f);

            var matrixDesc = new Label("Select which languages will be generated when running automated batch runs from the Matrix tab.");
            matrixDesc.style.fontSize = 11;
            matrixDesc.style.color = new Color(0.7f, 0.7f, 0.7f);
            matrixDesc.style.whiteSpace = WhiteSpace.Normal;
            matrixDesc.style.marginBottom = 8;
            matrixFoldout.Add(matrixDesc);

            // Toolbar: Select All / None / Reset
            var actionsRow = new VisualElement();
            actionsRow.style.flexDirection = FlexDirection.Row;
            actionsRow.style.alignItems = Align.Center;
            actionsRow.style.marginBottom = 8;

            var selAllBtn = new Button(() =>
            {
                if (config.languages != null)
                {
                    foreach (var l in config.languages) l.enabled = true;
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    serializedObject.Update();
                }
            }) { text = "Select All" };
            selAllBtn.AddToClassList("bp-btn");
            selAllBtn.style.height = 22;
            selAllBtn.style.paddingLeft = 8;
            selAllBtn.style.paddingRight = 8;
            selAllBtn.style.fontSize = 11;
            selAllBtn.style.marginRight = 4;
            actionsRow.Add(selAllBtn);

            var selNoneBtn = new Button(() =>
            {
                if (config.languages != null)
                {
                    foreach (var l in config.languages) l.enabled = false;
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    serializedObject.Update();
                }
            }) { text = "Select None" };
            selNoneBtn.AddToClassList("bp-btn");
            selNoneBtn.style.height = 22;
            selNoneBtn.style.paddingLeft = 8;
            selNoneBtn.style.paddingRight = 8;
            selNoneBtn.style.fontSize = 11;
            selNoneBtn.style.marginRight = 4;
            actionsRow.Add(selNoneBtn);

            var restoreLangsBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Restore Default Languages", "Reset language list to default presets?", "RESTORE", "CANCEL"))
                {
                    config.languages = LanguageProfile.GetDefaultLanguages();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    serializedObject.Update();
                }
            }) { text = "↺ Reset Presets" };
            restoreLangsBtn.AddToClassList("bp-btn");
            restoreLangsBtn.style.height = 22;
            restoreLangsBtn.style.paddingLeft = 8;
            restoreLangsBtn.style.paddingRight = 8;
            restoreLangsBtn.style.fontSize = 11;
            actionsRow.Add(restoreLangsBtn);

            matrixFoldout.Add(actionsRow);

            // Tags / Pills Container
            var tagsContainer = new VisualElement();
            tagsContainer.style.flexDirection = FlexDirection.Row;
            tagsContainer.style.flexWrap = Wrap.Wrap;
            tagsContainer.style.marginBottom = 10;

            void RefreshTags()
            {
                tagsContainer.Clear();
                if (config.languages == null) return;

                for (int i = 0; i < config.languages.Count; i++)
                {
                    var lp = config.languages[i];
                    var tagBtn = new Button();
                    tagBtn.AddToClassList("bp-btn");
                    tagBtn.style.flexDirection = FlexDirection.Row;
                    tagBtn.style.alignItems = Align.Center;
                    tagBtn.style.paddingLeft = 8;
                    tagBtn.style.paddingRight = 8;
                    tagBtn.style.paddingTop = 3;
                    tagBtn.style.paddingBottom = 3;
                    tagBtn.style.marginRight = 6;
                    tagBtn.style.marginBottom = 6;
                    tagBtn.style.borderTopLeftRadius = 4;
                    tagBtn.style.borderTopRightRadius = 4;
                    tagBtn.style.borderBottomLeftRadius = 4;
                    tagBtn.style.borderBottomRightRadius = 4;

                    void UpdateTagVisual()
                    {
                        if (lp.enabled)
                        {
                            tagBtn.text = $"✓  {lp.Name}";
                            tagBtn.style.backgroundColor = new Color(0.18f, 0.45f, 0.72f, 0.85f);
                            tagBtn.style.borderTopColor = new Color(0.25f, 0.6f, 0.95f, 1f);
                            tagBtn.style.borderBottomColor = new Color(0.25f, 0.6f, 0.95f, 1f);
                            tagBtn.style.borderLeftColor = new Color(0.25f, 0.6f, 0.95f, 1f);
                            tagBtn.style.borderRightColor = new Color(0.25f, 0.6f, 0.95f, 1f);
                            tagBtn.style.color = Color.white;
                        }
                        else
                        {
                            tagBtn.text = $"✕  {lp.Name}";
                            tagBtn.style.backgroundColor = new Color(0.22f, 0.22f, 0.25f, 0.6f);
                            tagBtn.style.borderTopColor = new Color(0.35f, 0.35f, 0.38f, 0.6f);
                            tagBtn.style.borderBottomColor = new Color(0.35f, 0.35f, 0.38f, 0.6f);
                            tagBtn.style.borderLeftColor = new Color(0.35f, 0.35f, 0.38f, 0.6f);
                            tagBtn.style.borderRightColor = new Color(0.35f, 0.35f, 0.38f, 0.6f);
                            tagBtn.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                        }
                    }

                    UpdateTagVisual();

                    tagBtn.clicked += () =>
                    {
                        lp.enabled = !lp.enabled;
                        UpdateTagVisual();
                        EditorUtility.SetDirty(config);
                        AssetDatabase.SaveAssetIfDirty(config);
                    };

                    tagsContainer.Add(tagBtn);
                }
            }

            RefreshTags();

            selAllBtn.clicked += RefreshTags;
            selNoneBtn.clicked += RefreshTags;
            restoreLangsBtn.clicked += RefreshTags;

            matrixFoldout.Add(tagsContainer);

            // Raw list foldout for edge cases
            var rawListFoldout = new Foldout { text = "Customize Languages List (Raw Reorderable Array)", value = false };
            rawListFoldout.style.opacity = 0.85f;
            rawListFoldout.Add(new PropertyField(langProp, "Languages Array"));
            matrixFoldout.Add(rawListFoldout);

            langsCard.Add(matrixFoldout);
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
