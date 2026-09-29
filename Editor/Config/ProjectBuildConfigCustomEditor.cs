using System;
using System.Collections.Generic;
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

            // Live status: which publisher profile this will actually use, its resolved output folder, and
            // — specifically for a Steam profile — whether the NativeSocial scripting define is set, so this
            // is visible right where the build is triggered instead of only inside the Publishers list or the
            // separate Steam Upload window.
            var statusBox = new VisualElement { style = { marginBottom = 8 } };
            quickCard.Add(statusBox);

            void RefreshQuickBuildStatus()
            {
                statusBox.Clear();
                statusBox.Add(BuildQuickBuildStatus(config));
            }

            platField.RegisterValueChangedCallback(_ => RefreshQuickBuildStatus());
            pubField.RegisterValueChangedCallback(_ => RefreshQuickBuildStatus());
            RefreshQuickBuildStatus();

            var runBtn = new Button(() => ConfirmAndRunQuickBuild(config))
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
            var pubsBody = new VisualElement();
            pubsCard.Add(pubsBody);
            root.Add(pubsCard);

            void RefreshPublishersBody()
            {
                pubsBody.Clear();
                config.publishers ??= new System.Collections.Generic.List<PublisherProfile>();
                var steamProfiles = config.publishers.Where(p => p.IsSteamProfile).ToList();

                if (steamProfiles.Count == 0)
                {
                    var noSteam = BuildPipelineUIStyle.CreateCallout(
                        "No Steam publisher profile is configured — there's nowhere for Steam-only settings " +
                        "(build output folder, the NativeSocial scripting define) to live, and a Steam quick-build " +
                        "won't find a profile to use.", "warning");
                    var addSteamBtn = new Button(() =>
                    {
                        config.publishers.Add(new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows64, false, true, false));
                        EditorUtility.SetDirty(config);
                        AssetDatabase.SaveAssetIfDirty(config);
                        serializedObject.Update();
                        RefreshPublishersBody();
                    })
                    { text = "➕ Add Steam Profile" };
                    addSteamBtn.AddToClassList("bp-btn");
                    addSteamBtn.AddToClassList("bp-btn--primary");
                    addSteamBtn.style.marginTop = 6;
                    noSteam.Add(addSteamBtn);
                    pubsBody.Add(noSteam);
                }
                else
                {
                    var missingDefine = steamProfiles
                        .Where(p => !(p.scriptingDefines ?? new System.Collections.Generic.List<string>()).Contains(PublisherProfile.NativeSocialSteamDefine))
                        .ToList();
                    if (missingDefine.Count > 0)
                    {
                        var names = string.Join(", ", missingDefine.Select(p => p.displayName));
                        var warning = BuildPipelineUIStyle.CreateCallout(
                            $"⚠ {missingDefine.Count} Steam profile(s) ({names}) don't have '{PublisherProfile.NativeSocialSteamDefine}' in Scripting Defines. " +
                            "The build pipeline auto-adds it at build time when Steamworks.NET is detected, but adding it here makes it explicit and keeps it working even without that safety net.",
                            "warning");
                        var fixBtn = new Button(() =>
                        {
                            foreach (var p in missingDefine)
                            {
                                p.scriptingDefines ??= new System.Collections.Generic.List<string>();
                                p.scriptingDefines.Add(PublisherProfile.NativeSocialSteamDefine);
                            }
                            EditorUtility.SetDirty(config);
                            AssetDatabase.SaveAssetIfDirty(config);
                            serializedObject.Update();
                            RefreshPublishersBody();
                        })
                        { text = $"🛠 Add define to {missingDefine.Count} Steam profile(s)" };
                        fixBtn.AddToClassList("bp-btn");
                        fixBtn.AddToClassList("bp-btn--primary");
                        fixBtn.style.marginTop = 6;
                        warning.Add(fixBtn);
                        pubsBody.Add(warning);
                    }

                    foreach (var steamProfile in steamProfiles)
                        pubsBody.Add(BuildSteamFolderCard(config, steamProfile));
                }

                var pubProp = serializedObject.FindProperty("publishers");
                pubsBody.Add(new PropertyField(pubProp, "Configured Publishers"));

                var restorePubsBtn = new Button(() =>
                {
                    if (EditorUtility.DisplayDialog("Restore Default Publishers", "Reset publisher list to default presets?", "RESTORE", "CANCEL"))
                    {
                        config.publishers = PublisherProfile.GetDefaultProfiles();
                        EditorUtility.SetDirty(config);
                        AssetDatabase.SaveAssetIfDirty(config);
                        serializedObject.Update();
                        RefreshPublishersBody();
                    }
                })
                { text = "↺ Restore Default Publishers List" };
                restorePubsBtn.AddToClassList("bp-btn");
                restorePubsBtn.style.marginTop = 6;
                pubsBody.Add(restorePubsBtn);
            }

            RefreshPublishersBody();

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

        private const string DefaultSteamOutputSubfolder = "Builds/Publishers/Steam/";

        /// <summary>
        /// Prominent, dedicated "where do Steam builds go" control for one Steam profile — the generic
        /// reorderable Publishers array buries <see cref="PublisherProfile.outputSubfolder"/> behind several
        /// clicks and shows no resolved path, so a user has to do the Path.Combine math in their head to know
        /// where a Steam build will actually land. This shows the resolved absolute path live, and a folder
        /// picker: picking an absolute folder here works because <c>Path.Combine(root, sub, ...)</c> discards
        /// everything before the last rooted (absolute) segment, so an absolute outputSubfolder fully overrides
        /// the global build root for just this profile — no extra field needed.
        /// </summary>
        private VisualElement BuildSteamFolderCard(ProjectBuildConfig config, PublisherProfile steamProfile)
        {
            var card = new VisualElement();
            card.AddToClassList("bp-card");
            card.style.marginTop = 8;
            card.style.marginBottom = 8;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.backgroundColor = new Color(0.15f, 0.18f, 0.24f, 0.6f);
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 4;

            var title = new Label($"📁 Steam Build Output Folder — \"{steamProfile.displayName}\" profile");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 12;
            title.style.marginBottom = 4;
            card.Add(title);

            var resolvedLabel = new Label();
            resolvedLabel.style.fontSize = 11;
            resolvedLabel.style.color = new Color(0.6f, 0.85f, 0.6f);
            resolvedLabel.style.whiteSpace = WhiteSpace.Normal;
            resolvedLabel.style.marginBottom = 6;
            card.Add(resolvedLabel);

            void RefreshResolvedPreview()
            {
                var resolved = ResolveSteamOutputPreview(config, steamProfile);
                resolvedLabel.text = $"→ {resolved}";
            }

            var subRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var subField = new TextField("Subfolder / absolute path") { value = steamProfile.outputSubfolder, style = { flexGrow = 1 } };
            subField.RegisterValueChangedCallback(evt =>
            {
                steamProfile.outputSubfolder = evt.newValue;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(config);
                RefreshResolvedPreview();
            });
            subRow.Add(subField);
            card.Add(subRow);

            RefreshResolvedPreview();

            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };

            var browseBtn = new Button(() =>
            {
                var start = Directory.Exists(ResolveSteamOutputPreview(config, steamProfile))
                    ? ResolveSteamOutputPreview(config, steamProfile)
                    : config.GetEffectiveBuildOutputRoot(steamProfile.platform);
                var picked = EditorUtility.OpenFolderPanel("Choose Steam Build Output Folder", start, "");
                if (string.IsNullOrEmpty(picked)) return;

                steamProfile.outputSubfolder = picked.EndsWith("/") || picked.EndsWith("\\") ? picked : picked + "/";
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(config);
                subField.SetValueWithoutNotify(steamProfile.outputSubfolder);
                RefreshResolvedPreview();
            })
            { text = "📁 Choose Folder..." };
            browseBtn.AddToClassList("bp-btn");
            browseBtn.style.marginRight = 4;
            btnRow.Add(browseBtn);

            var openBtn = new Button(() =>
            {
                var resolved = ResolveSteamOutputPreview(config, steamProfile);
                if (Directory.Exists(resolved))
                    EditorUtility.RevealInFinder(resolved);
                else
                    EditorUtility.DisplayDialog("Folder Not Found",
                        $"'{resolved}' doesn't exist yet — it's created the first time a Steam build runs.", "OK");
            })
            { text = "📂 Open Folder" };
            openBtn.AddToClassList("bp-btn");
            openBtn.style.marginRight = 4;
            btnRow.Add(openBtn);

            var resetBtn = new Button(() =>
            {
                steamProfile.outputSubfolder = DefaultSteamOutputSubfolder;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(config);
                subField.SetValueWithoutNotify(steamProfile.outputSubfolder);
                RefreshResolvedPreview();
            })
            { text = "↺ Reset to Default" };
            resetBtn.AddToClassList("bp-btn");
            btnRow.Add(resetBtn);

            card.Add(btnRow);
            return card;
        }

        /// <summary>Best-effort absolute preview of where a Steam build for this profile would land — same
        /// combine logic as <c>BuildContext.ResolveOutputPaths</c>'s Windows/Linux branch, minus the
        /// per-build filename folder (version/date/language), since this is shown before any build ran.</summary>
        private static string ResolveSteamOutputPreview(ProjectBuildConfig config, PublisherProfile steamProfile)
        {
            var root = config.GetEffectiveBuildOutputRoot(steamProfile.platform);
            var sub = string.IsNullOrEmpty(steamProfile.outputSubfolder) ? DefaultSteamOutputSubfolder : steamProfile.outputSubfolder;
            try
            {
                return Path.GetFullPath(Path.Combine(root, sub));
            }
            catch
            {
                return Path.Combine(root, sub);
            }
        }

        /// <summary>Live "what will actually happen" readout for the selected Quick Build publisher: which
        /// profile it resolves to (or that none exists — a Quick Build with an unmatched Publisher enum
        /// silently falls back to a generic profile-less build), its resolved output folder, and — the
        /// specific visibility gap that was asked for — whether a Steam profile has the NativeSocial
        /// scripting define set, with a direct shortcut to the Steam Upload window instead of having to go
        /// find it in the raw Publishers list.</summary>
        private VisualElement BuildQuickBuildStatus(ProjectBuildConfig config)
        {
            var box = new VisualElement();
            var prof = config.publishers?.FirstOrDefault(p => p.publisher == _quickPublisher);

            if (prof == null)
            {
                box.Add(BuildPipelineUIStyle.CreateCallout(
                    $"⚠ No Publisher Profile matches '{_quickPublisher}' — this Quick Build will run with generic defaults (no per-publisher output folder, splash, or scripting defines).",
                    "warning"));
                return box;
            }

            var outDir = ResolveQuickBuildOutputPreview(config, prof, _quickPlatform);
            box.Add(new Label($"📁 Profile: {prof.displayName}  →  {outDir}")
            { style = { fontSize = 10, color = new Color(0.7f, 0.75f, 0.8f), whiteSpace = WhiteSpace.Normal, marginBottom = 2 } });

            if (prof.IsSteamProfile)
            {
                var hasDefine = (prof.scriptingDefines ?? new List<string>()).Contains(PublisherProfile.NativeSocialSteamDefine);
                var steamRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap } };
                steamRow.Add(new Label(hasDefine
                    ? $"✔ Steam: '{PublisherProfile.NativeSocialSteamDefine}' is set on this profile."
                    : $"⚠ Steam: '{PublisherProfile.NativeSocialSteamDefine}' is NOT in this profile's Scripting Defines (the pipeline auto-adds it at build time if Steamworks.NET is detected, but it's not explicit here).")
                { style = { fontSize = 10, color = hasDefine ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.9f, 0.7f, 0.4f), whiteSpace = WhiteSpace.Normal, flexGrow = 1 } });

                var openSteamBtn = new Button(SteamUploadWindow.Open) { text = "🎮 Steam Upload..." };
                openSteamBtn.AddToClassList("bp-btn");
                openSteamBtn.style.marginLeft = 6;
                steamRow.Add(openSteamBtn);
                box.Add(steamRow);
            }

            return box;
        }

        private static string ResolveQuickBuildOutputPreview(ProjectBuildConfig config, PublisherProfile prof, PlatformType platform)
        {
            var root = config.GetEffectiveBuildOutputRoot(platform);
            var sub = (prof.outputSubfolder ?? "Builds/").Replace("{Publisher}", prof.publisher.ToString());
            try { return Path.GetFullPath(Path.Combine(root, sub)); }
            catch { return Path.Combine(root, sub); }
        }

        private void ConfirmAndRunQuickBuild(ProjectBuildConfig config)
        {
            if (config.publishers == null || config.publishers.Count == 0)
            {
                EditorUtility.DisplayDialog("Quick Build", "No publisher list configured.\nUse 'Restore Default Publishers List' below.", "OK");
                return;
            }

            var prof = config.publishers.FirstOrDefault(p => p.publisher == _quickPublisher);
            var outDir = prof != null ? ResolveQuickBuildOutputPreview(config, prof, _quickPlatform) : "(no matching profile — generic defaults)";
            var flags = string.Join(", ", new[]
            {
                _quickCheat ? "Cheat" : null,
                _quickDevBuild ? "Development" : null,
                _quickPlatform == PlatformType.Android ? (_quickAppBundle ? ".aab" : ".apk") : null,
                _quickAutoRun ? "Auto Run" : null
            }.Where(f => f != null));

            var steamNote = "";
            if (prof != null && prof.IsSteamProfile)
            {
                var hasDefine = (prof.scriptingDefines ?? new List<string>()).Contains(PublisherProfile.NativeSocialSteamDefine);
                steamNote = hasDefine
                    ? $"\nSteam define: ✔ {PublisherProfile.NativeSocialSteamDefine} is set."
                    : $"\nSteam define: ⚠ {PublisherProfile.NativeSocialSteamDefine} not explicit on this profile (auto-added at build time if Steamworks.NET is present).";
            }

            var message =
                $"Platform: {_quickPlatform}\n" +
                $"Publisher: {_quickPublisher}{(prof != null ? $" ({prof.displayName})" : " — NO MATCHING PROFILE")}\n" +
                $"Language: {_quickLanguage}\n" +
                $"Flags: {(string.IsNullOrEmpty(flags) ? "(none)" : flags)}\n" +
                $"Output: {outDir}" +
                steamNote +
                "\n\nThis will run the full build pipeline now (pre/post steps included) and may take several minutes. Proceed?";

            if (!EditorUtility.DisplayDialog("Confirm Quick Build", message, "▶️ Build Now", "Cancel"))
                return;

            RunQuickBuild(config);
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
