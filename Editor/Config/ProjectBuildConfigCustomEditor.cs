using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    [CustomEditor(typeof(ProjectBuildConfig))]
    public class ProjectBuildConfigCustomEditor : UnityEditor.Editor
    {
        private static bool _foldQuick = true;
        private static bool _foldVault = true;
        private static bool _foldPublishers = true;
        private static bool _foldLanguages = true;
        private static bool _foldPaths = false;

        private static bool _showVaultToken = false;
        private static bool _showLocalPass = false;
        private string _vaultStatus = "";
        private string _localStatus = "";

        private Publisher _quickPublisher = Publisher.BigFish;
        private PlatformType _quickPlatform = PlatformType.Windows64;
        private GameLanguage _quickLanguage = GameLanguage.AutoDetect;
        private bool _quickCheat = false;
        private bool _quickDevBuild = false;
        private bool _quickAppBundle = true;
        private bool _quickAutoRun = false;

        private void OnEnable()
        {
            _foldQuick = EditorPrefs.GetBool("ProjectBuildConfig_FoldQuick", true);
            _foldVault = EditorPrefs.GetBool("ProjectBuildConfig_FoldVault", true);
            _foldPublishers = EditorPrefs.GetBool("ProjectBuildConfig_FoldPublishers", true);
            _foldLanguages = EditorPrefs.GetBool("ProjectBuildConfig_FoldLanguages", true);
            _foldPaths = EditorPrefs.GetBool("ProjectBuildConfig_FoldPaths", false);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var config = (ProjectBuildConfig)target;

            // 1. Header Banner
            DrawHeader(config);

            // 2. Quick Build (Run Build From Here) + Last Build Actions
            DrawQuickBuildSection(config);
            EditorGUILayout.Space(6);

            // 3. Android Keystore & Remote Vault (Categorized & with Test Buttons!)
            DrawKeystoreVaultSection(config);
            EditorGUILayout.Space(6);

            // 4. Publishers & Stores (Human-readable cards instead of Element 0..17)
            DrawPublishersSection(config);
            EditorGUILayout.Space(6);

            // 5. Languages Matrix (Clean grid of language toggles)
            DrawLanguagesSection(config);
            EditorGUILayout.Space(6);

            // 6. Output Paths & Scenes
            DrawPathsAndScenesSection(config);

            serializedObject.ApplyModifiedProperties();
        }

        #region Section: Quick Build
        private void DrawQuickBuildSection(ProjectBuildConfig config)
        {
            _foldQuick = DrawSectionHeader("🚀 0. Run Build Now (Quick Build)", _foldQuick, "ProjectBuildConfig_FoldQuick");
            if (!_foldQuick) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.HelpBox(
                "Triggers an immediate build without opening the Build Pipeline Window.\n" +
                "Uses the publisher's profile (output folder, splash, ZIP) and the linked GameConfig.\n" +
                "Note: .aab is not executable — to test on device, use .apk (uncheck '.aab').",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            _quickPlatform = (PlatformType)EditorGUILayout.EnumPopup("Platform", _quickPlatform);
            _quickPublisher = (Publisher)EditorGUILayout.EnumPopup("Publisher", _quickPublisher);
            EditorGUILayout.EndHorizontal();

            _quickLanguage = (GameLanguage)EditorGUILayout.EnumPopup("Language", _quickLanguage);

            EditorGUILayout.BeginHorizontal();
            _quickCheat = EditorGUILayout.ToggleLeft("Cheat", _quickCheat, GUILayout.Width(70));
            _quickDevBuild = EditorGUILayout.ToggleLeft("Development", _quickDevBuild, GUILayout.Width(100));
            if (_quickPlatform == PlatformType.Android)
                _quickAppBundle = EditorGUILayout.ToggleLeft(".aab", _quickAppBundle, GUILayout.Width(55));
            _quickAutoRun = EditorGUILayout.ToggleLeft("Auto Run", _quickAutoRun);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            GUI.backgroundColor = new Color(0.25f, 0.70f, 0.45f);
            if (GUILayout.Button("▶️  RUN BUILD NOW", GUILayout.Height(32)))
            {
                RunQuickBuild(config);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(4);
            DrawLastBuildRow();

            EditorGUILayout.EndVertical();
        }

        private void RunQuickBuild(ProjectBuildConfig config)
        {
            if (config.publishers == null || config.publishers.Count == 0)
            {
                EditorUtility.DisplayDialog("Quick Build", "No publisher list configured.\nUse 'Restore Default Publisher List' in the Publishers section.", "OK");
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
                EditorUtility.DisplayDialog("Build Failed", $"Build failed with {res.TotalErrors} error(s).\nCheck the Console for details.", "OK");
            }
        }

        private void DrawLastBuildRow()
        {
            var last = BuildHistory.LastSuccessful;
            if (last == null) return;

            var sizeMb = last.totalSize > 0 ? $"{last.totalSize / (1024.0 * 1024.0):F1} MB" : "";
            var info = $"{last.Time:dd/MM HH:mm}  |  {last.platform}  |  {last.publisher}  |  {last.language}{(last.cheatMode ? " | CHEAT" : "")}  {(string.IsNullOrEmpty(sizeMb) ? "" : $"|  {sizeMb}")}";

            EditorGUILayout.LabelField("Last Build:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(last.outputPath, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.18f, 0.60f, 0.90f);
            if (GUILayout.Button("📂 Open Folder", GUILayout.Height(24)))
            {
                BuildHistory.OpenFolder(last);
            }
            GUI.backgroundColor = new Color(0.25f, 0.70f, 0.45f);
            using (new EditorGUI.DisabledScope(!BuildHistory.CanRun(last)))
            {
                var runLabel = last.platform == "Android" ? "▶️ Install & Run" : "▶️ Run";
                if (GUILayout.Button(runLabel, GUILayout.Height(24)))
                {
                    BuildHistory.Run(last);
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            if (last.IsAab)
            {
                EditorGUILayout.HelpBox("ℹ️ The last build is a .aab (Play Store upload format). It cannot be run directly. To test on device, produce a .apk (uncheck '.aab' in Quick Build).", MessageType.None);
            }
        }
        #endregion

        #region Header
        private void DrawHeader(ProjectBuildConfig config)
        {
            var isPro = EditorGUIUtility.isProSkin;
            var bgColor = isPro ? new Color(0.12f, 0.14f, 0.18f) : new Color(0.88f, 0.90f, 0.94f);

            var rect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.DrawRect(rect, bgColor);

            GUILayout.Space(6);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isPro ? new Color(0.25f, 0.85f, 0.95f) : new Color(0.05f, 0.45f, 0.70f) }
            };
            var projTitle = !string.IsNullOrEmpty(config.projectName) ? config.projectName : "PROJECT BUILD PIPELINE";
            EditorGUILayout.LabelField(projTitle.ToUpperInvariant(), titleStyle);

            var subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isPro ? new Color(0.65f, 0.70f, 0.75f) : new Color(0.35f, 0.40f, 0.45f) }
            };
            var linkedName = config.gameConfig != null ? config.gameConfig.name : "None (click Migrate)";
            EditorGUILayout.LabelField($"GENERAL BUILD CONFIGURATION  •  GameConfig: {linkedName}", subStyle);

            GUILayout.Space(6);

            // Open Build Pipeline Button
            GUI.backgroundColor = new Color(0.18f, 0.60f, 0.90f);
            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                fixedHeight = 32
            };
            if (GUILayout.Button("🚀  OPEN BUILD WINDOW (BUILD PIPELINE WINDOW)", btnStyle))
            {
                Debug.Log("[BuildPipeline] 'OPEN BUILD WINDOW' button clicked from ProjectBuildConfig Inspector.");
                BuildPipelineWindow.ShowWindow();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "📌 This file is build INFRASTRUCTURE only (paths, keystore, publishers, languages).\n" +
                "Game version, Android Bundle Code and iOS Build Number are NOT here — they live on GameConfig (" +
                linkedName + "), listed above.",
                MessageType.Info);

            if (config.gameConfig != null)
            {
                if (GUILayout.Button($"📂 Open GameConfig ({linkedName})", EditorStyles.miniButton, GUILayout.Height(20)))
                {
                    Selection.activeObject = config.gameConfig;
                    EditorGUIUtility.PingObject(config.gameConfig);
                }
            }

            DrawSaveStatusBanner(config);

            EditorGUILayout.Space(6);
        }

        private void DrawSaveStatusBanner(ProjectBuildConfig config)
        {
            bool isDirty = EditorUtility.IsDirty(config);
            var isPro = EditorGUIUtility.isProSkin;

            GUILayout.Space(4);
            var bgColor = isDirty
                ? (isPro ? new Color(0.40f, 0.24f, 0.05f) : new Color(1.00f, 0.90f, 0.70f))
                : (isPro ? new Color(0.10f, 0.24f, 0.15f) : new Color(0.82f, 0.93f, 0.85f));

            var rect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUI.DrawRect(rect, bgColor);
            GUILayout.Space(2);

            var textStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = isPro ? Color.white : new Color(0.15f, 0.15f, 0.15f) }
            };

            if (isDirty)
            {
                EditorGUILayout.LabelField("⚠ Changes NOT saved to disk yet — they won't show up in 'git status'.", textStyle);
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.90f, 0.55f, 0.15f);
                if (GUILayout.Button("💾  Save Now", GUILayout.Width(140), GUILayout.Height(24)))
                {
                    AssetDatabase.SaveAssetIfDirty(config);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[BuildPipeline] ProjectBuildConfig saved to disk.");
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                EditorGUILayout.LabelField("✔ Everything saved to disk.", textStyle);
            }

            GUILayout.Space(2);
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region Section: Keystore Vault
        private void DrawKeystoreVaultSection(ProjectBuildConfig config)
        {
            _foldVault = DrawSectionHeader("🔐 1. Android Keystore & Central Credentials (Vault)", _foldVault, "ProjectBuildConfig_FoldVault");
            if (!_foldVault) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            config.keystoreSource = (KeystoreSource)EditorGUILayout.EnumPopup("Keystore Mode", config.keystoreSource);

            if (config.keystoreSource == KeystoreSource.RemoteVault)
            {
                EditorGUILayout.HelpBox(
                    "🌐 REMOTE VAULT MODE ACTIVE:\n" +
                    "No certificate or password is versioned in Git.\n" +
                    "The pipeline queries the secure server via HTTPS Bearer token, downloads the .keystore into memory in a local temp cache (Library/KeystoreCache/), and wipes the passwords from PlayerSettings.Android immediately after the build.",
                    MessageType.Info);

                EditorGUILayout.Space(4);
                config.vaultUrl = EditorGUILayout.TextField("Vault API URL", config.vaultUrl);

                EditorGUILayout.BeginHorizontal();
                config.vaultProfileId = EditorGUILayout.TextField("Vault Profile ID", config.vaultProfileId);
                if (GUILayout.Button("Auto-Detect", GUILayout.Width(95)))
                {
                    config.vaultProfileId = "";
                    EditorUtility.SetDirty(config);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (_showVaultToken)
                    config.vaultTokenFallback = EditorGUILayout.TextField("Access Token / API Key", config.vaultTokenFallback);
                else
                    config.vaultTokenFallback = EditorGUILayout.PasswordField("Access Token / API Key", config.vaultTokenFallback);

                if (GUILayout.Button(_showVaultToken ? "Hide" : "Show", GUILayout.Width(65)))
                    _showVaultToken = !_showVaultToken;
                EditorGUILayout.EndHorizontal();

                config.vaultTokenEnvVar = EditorGUILayout.TextField("Environment Variable (CI/CD)", config.vaultTokenEnvVar);

                EditorGUILayout.Space(6);

                // Validation Button for Web Vault
                var viewWidth = EditorGUIUtility.currentViewWidth;
                if (viewWidth < 450)
                {
                    GUI.backgroundColor = new Color(0.2f, 0.7f, 0.4f);
                    if (GUILayout.Button("🔌 Test Vault Connection & Download Keystore", GUILayout.Height(28)))
                    {
                        TestVaultConnection(config);
                    }
                    GUI.backgroundColor = Color.white;

                    if (GUILayout.Button("🌐 Open Web Panel (/admin/keystores)", GUILayout.Height(26)))
                    {
                        Application.OpenURL("https://wagenheimer.com/admin/keystores");
                    }
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    GUI.backgroundColor = new Color(0.2f, 0.7f, 0.4f);
                    if (GUILayout.Button("🔌 Test Vault Connection & Download Keystore", GUILayout.Height(28)))
                    {
                        TestVaultConnection(config);
                    }
                    GUI.backgroundColor = Color.white;

                    if (GUILayout.Button("🌐 Open Web Panel (/admin/keystores)", GUILayout.Height(28), GUILayout.Width(220)))
                    {
                        Application.OpenURL("https://wagenheimer.com/admin/keystores");
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (!string.IsNullOrEmpty(_vaultStatus))
                {
                    EditorGUILayout.HelpBox(_vaultStatus, _vaultStatus.StartsWith("✓") ? MessageType.Info : MessageType.Error);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("💾 LOCAL DISK MODE ACTIVE: Uses a physical keystore file stored on your computer.", MessageType.None);

                EditorGUILayout.BeginHorizontal();
                config.androidKeystorePath = EditorGUILayout.TextField("Keystore Path (Win)", config.androidKeystorePath);
                if (GUILayout.Button("Browse...", GUILayout.Width(75)))
                {
                    var file = EditorUtility.OpenFilePanel("Select the .keystore file", Path.GetDirectoryName(config.androidKeystorePath), "keystore,jks");
                    if (!string.IsNullOrEmpty(file))
                    {
                        config.androidKeystorePath = file;
                        EditorUtility.SetDirty(config);
                    }
                }
                EditorGUILayout.EndHorizontal();

                config.androidKeyAlias = EditorGUILayout.TextField("Key Alias Name", config.androidKeyAlias);

                EditorGUILayout.BeginHorizontal();
                if (_showLocalPass)
                {
                    config.androidKeystorePassFallback = EditorGUILayout.TextField("Keystore Password", config.androidKeystorePassFallback);
                    config.androidKeyaliasPassFallback = EditorGUILayout.TextField("Alias Password", config.androidKeyaliasPassFallback);
                }
                else
                {
                    config.androidKeystorePassFallback = EditorGUILayout.PasswordField("Keystore Password", config.androidKeystorePassFallback);
                    config.androidKeyaliasPassFallback = EditorGUILayout.PasswordField("Alias Password", config.androidKeyaliasPassFallback);
                }
                if (GUILayout.Button(_showLocalPass ? "Hide" : "Show", GUILayout.Width(65)))
                    _showLocalPass = !_showLocalPass;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(6);

                // Validation Button for Local Disk
                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.85f);
                if (GUILayout.Button("🔍 Validate Local Keystore File (Integrity and Passwords)", GUILayout.Height(28)))
                {
                    var val = KeystoreVaultClient.ValidateLocalKeystore(
                        config.GetEffectiveKeystorePath(),
                        config.androidKeyAlias,
                        config.GetEffectiveKeystorePassword(),
                        config.GetEffectiveKeyaliasPassword());

                    if (val.Success)
                    {
                        _localStatus = $"✓ Local Keystore Valid!\n{val.Message}";
                        EditorUtility.DisplayDialog("Local Validation: Success!", _localStatus, "OK");
                    }
                    else
                    {
                        _localStatus = $"✗ Local validation error:\n{val.Message}";
                        EditorUtility.DisplayDialog("Local Validation: Error", _localStatus, "OK");
                    }
                }
                GUI.backgroundColor = Color.white;

                if (!string.IsNullOrEmpty(_localStatus))
                {
                    EditorGUILayout.HelpBox(_localStatus, _localStatus.StartsWith("✓") ? MessageType.Info : MessageType.Error);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void TestVaultConnection(ProjectBuildConfig config)
        {
            var token = KeystoreVaultClient.GetEffectiveToken(config);
            var bundle = config.gameConfig != null ? config.gameConfig.DefaultBundleIdentifier : config.defaultBundleIdentifier;
            var res = KeystoreVaultClient.FetchCredentialsSync(config.vaultUrl, config.vaultProfileId, bundle, token);
            if (res.Success && res.Credentials != null)
            {
                _vaultStatus = $"✓ Vault connection successful!\nCertificate: {res.Credentials.KeystoreFileName}\nCached at: {res.Credentials.KeystorePath}\nAlias: {res.Credentials.KeyAliasName}";
                EditorUtility.DisplayDialog("Vault Test: Success!", _vaultStatus, "OK");
            }
            else
            {
                _vaultStatus = $"✗ Failed to query the Vault:\n{res.Message}";
                EditorUtility.DisplayDialog("Vault Test: Error", _vaultStatus, "OK");
            }
        }
        #endregion

        #region Section: Publishers
        private void DrawPublishersSection(ProjectBuildConfig config)
        {
            var count = config.publishers != null ? config.publishers.Count : 0;
            _foldPublishers = DrawSectionHeader($"🏬 2. Publishers & Stores ({count} configured)", _foldPublishers, "ProjectBuildConfig_FoldPublishers");
            if (!_foldPublishers) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.HelpBox(
                "💡 WHAT IS THIS SECTION FOR?\n" +
                "Each store (Steam, Big Fish, Google Play, etc.) has specific requirements: output folder, publisher splash logos, and automatic .ZIP creation.\n" +
                "The pipeline uses these profiles to automatically generate the correct builds in 'Quick Build' and 'Matrix Batch Builder'.",
                MessageType.Info);

            if (config.publishers == null || config.publishers.Count == 0)
            {
                if (GUILayout.Button("Restore Default Publisher List", GUILayout.Height(26)))
                {
                    config.PopulateDefaults();
                    EditorUtility.SetDirty(config);
                }
                EditorGUILayout.EndVertical();
                return;
            }

            // Desktop Windows Stores
            DrawPublisherGroup("🖥️ Desktop Stores (Windows)", config, PlatformType.Windows64);
            EditorGUILayout.Space(4);

            // Mobile Stores
            DrawPublisherGroup("📱 Mobile Stores (Android & iOS)", config, PlatformType.Android, PlatformType.iOS);
            EditorGUILayout.Space(4);

            // Mac Stores
            DrawPublisherGroup("🍏 Mac Stores (macOS)", config, PlatformType.macOS);

            EditorGUILayout.EndVertical();
        }

        private void DrawPublisherGroup(string headerTitle, ProjectBuildConfig config, params PlatformType[] platforms)
        {
            var list = config.publishers.Where(p => platforms.Contains(p.platform)).ToList();
            if (list.Count == 0) return;

            var isPro = EditorGUIUtility.isProSkin;
            var headerBg = isPro ? new Color(0.16f, 0.18f, 0.22f) : new Color(0.85f, 0.87f, 0.90f);

            var rect = GUILayoutUtility.GetRect(18, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, headerBg);
            GUI.Label(new Rect(rect.x + 6, rect.y + 2, rect.width, rect.height), headerTitle, EditorStyles.boldLabel);

            foreach (var pub in list)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                var title = !string.IsNullOrEmpty(pub.displayName) ? pub.displayName : pub.publisher.ToString();
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(170));

                pub.requiresSplash = EditorGUILayout.ToggleLeft("Splash Logo", pub.requiresSplash, GUILayout.Width(95));
                pub.zipAfterBuild = EditorGUILayout.ToggleLeft("Create ZIP", pub.zipAfterBuild, GUILayout.Width(85));
                pub.isFullGame = EditorGUILayout.ToggleLeft("Full", pub.isFullGame, GUILayout.Width(50));

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUIUtility.labelWidth = 100;
                pub.outputSubfolder = EditorGUILayout.TextField("Output Folder", pub.outputSubfolder);
                if (pub.platform == PlatformType.Android || pub.platform == PlatformType.iOS)
                {
                    EditorGUIUtility.labelWidth = 70;
                    pub.bundleIdentifier = EditorGUILayout.TextField("Bundle ID", pub.bundleIdentifier);
                }
                EditorGUIUtility.labelWidth = 0;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }
        }
        #endregion

        #region Section: Languages
        private void DrawLanguagesSection(ProjectBuildConfig config)
        {
            var enabledCount = config.languages != null ? config.languages.Count(l => l.enabled) : 0;
            var totalCount = config.languages != null ? config.languages.Count : 0;
            _foldLanguages = DrawSectionHeader($"🌐 3. Language Matrix ({enabledCount}/{totalCount} active)", _foldLanguages, "ProjectBuildConfig_FoldLanguages");
            if (!_foldLanguages) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.HelpBox(
                "💡 WHAT IS THIS SECTION FOR?\n" +
                "Defines which languages are enabled for this game.\n" +
                "The 'Matrix Batch Builder' will use the languages checked as 'Active' to automatically compile all localized executables.",
                MessageType.Info);

            if (config.languages == null || config.languages.Count == 0)
            {
                if (GUILayout.Button("Restore Default Languages", GUILayout.Height(26)))
                {
                    config.PopulateDefaults();
                    EditorUtility.SetDirty(config);
                }
                EditorGUILayout.EndVertical();
                return;
            }

            // Quick Selection Toolbar
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", EditorStyles.miniButtonLeft))
            {
                foreach (var l in config.languages) l.enabled = true;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button("Deselect All", EditorStyles.miniButtonMid))
            {
                foreach (var l in config.languages) l.enabled = false;
                EditorUtility.SetDirty(config);
            }
            if (GUILayout.Button("EFIGS Only (EN, FR, IT, DE, ES)", EditorStyles.miniButtonRight))
            {
                var efigs = new[] { GameLanguage.English, GameLanguage.French, GameLanguage.Italian, GameLanguage.German, GameLanguage.Spanish };
                foreach (var l in config.languages)
                    l.enabled = efigs.Contains(l.language);
                EditorUtility.SetDirty(config);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // 2-Column Grid of Languages
            for (var i = 0; i < config.languages.Count; i += 2)
            {
                EditorGUILayout.BeginHorizontal();
                var l1 = config.languages[i];
                l1.enabled = EditorGUILayout.ToggleLeft($"{l1.Name} ({l1.language})", l1.enabled, GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.45f));

                if (i + 1 < config.languages.Count)
                {
                    var l2 = config.languages[i + 1];
                    l2.enabled = EditorGUILayout.ToggleLeft($"{l2.Name} ({l2.language})", l2.enabled);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Section: Paths & Scenes
        private void DrawPathsAndScenesSection(ProjectBuildConfig config)
        {
            _foldPaths = DrawSectionHeader("📁 4. Output Folders, Scenes & Archiving", _foldPaths, "ProjectBuildConfig_FoldPaths");
            if (!_foldPaths) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            config.projectName = EditorGUILayout.TextField("Project Name", config.projectName);
            config.buildOutputRoot = EditorGUILayout.TextField("Build Output Root (Windows)", config.buildOutputRoot);
            config.macBuildOutputRoot = EditorGUILayout.TextField("Build Output Root (macOS)", config.macBuildOutputRoot);
            config.splashMasterFolder = EditorGUILayout.TextField("Splash Logos Master Folder", config.splashMasterFolder);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Scenes Used in the Build:", EditorStyles.boldLabel);
            config.publisherSplashScenePath = EditorGUILayout.TextField("  Publisher Splash Scene", config.publisherSplashScenePath);
            config.defaultSplashScenePath = EditorGUILayout.TextField("  Default Splash Scene", config.defaultSplashScenePath);
            config.levelEditorScenePath = EditorGUILayout.TextField("  Level Editor Scene", config.levelEditorScenePath);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Compression and Archiving:", EditorStyles.boldLabel);
            config.enableZipArchiving = EditorGUILayout.Toggle("Enable ZIP Archiving", config.enableZipArchiving);
            config.useExternalSevenZip = EditorGUILayout.Toggle("Use External 7-Zip", config.useExternalSevenZip);
            if (config.useExternalSevenZip)
            {
                config.sevenZipExecutable = EditorGUILayout.TextField("7-Zip Executable", config.sevenZipExecutable);
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Helpers
        private bool DrawSectionHeader(string title, bool isExpanded, string prefKey)
        {
            var isPro = EditorGUIUtility.isProSkin;
            var headerColor = isPro ? new Color(0.18f, 0.22f, 0.28f) : new Color(0.85f, 0.88f, 0.92f);
            var rect = GUILayoutUtility.GetRect(18, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, headerColor);

            var foldStyle = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                normal = { textColor = isPro ? Color.white : Color.black },
                onNormal = { textColor = isPro ? Color.white : Color.black }
            };

            var newExpanded = EditorGUI.Foldout(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4), isExpanded, title, true, foldStyle);
            if (newExpanded != isExpanded)
            {
                EditorPrefs.SetBool(prefKey, newExpanded);
            }
            return newExpanded;
        }
        #endregion
    }
}
