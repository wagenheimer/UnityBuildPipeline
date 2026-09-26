using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class BuildPipelineWindow : EditorWindow
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Dashboard...", priority = 0)]
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Open Build Window", priority = 10)]
        [MenuItem("Window/Wagenheimer/Build Pipeline", priority = 205)]
        public static void ShowWindow()
        {
            Debug.Log("[BuildPipeline] 🚀 Opening Build Pipeline Window...");
            try
            {
                var win = GetWindow<BuildPipelineWindow>(utility: false, title: "Build Pipeline", focus: true);
                win.titleContent = new GUIContent("Build Pipeline", EditorGUIUtility.IconContent("BuildSettings.Editor").image);
                win.minSize = new Vector2(760, 560);

                // Check if the window is off-screen or positioned on a disconnected monitor, and center it
                var mainPos = EditorGUIUtility.GetMainWindowPosition();
                bool isOffscreen = !win.docked && (
                    win.position.x >= mainPos.x + mainPos.width - 80 ||
                    win.position.x + win.position.width <= mainPos.x + 80 ||
                    win.position.y >= mainPos.y + mainPos.height - 80 ||
                    win.position.y < mainPos.y - 100 ||
                    win.position.width < 300 || win.position.height < 300
                );

                if (isOffscreen)
                {
                    float w = Mathf.Min(880f, mainPos.width * 0.85f);
                    float h = Mathf.Min(660f, mainPos.height * 0.85f);
                    float x = mainPos.x + (mainPos.width - w) * 0.5f;
                    float y = mainPos.y + (mainPos.height - h) * 0.5f;
                    win.position = new Rect(x, y, w, h);
                    Debug.Log($"[BuildPipeline] Window was off-screen; repositioned to center: {win.position}");
                }

                win.Show();
                win.Focus();
                win.Repaint();
                Debug.Log($"[BuildPipeline] ✓ Build Pipeline Window is now active and focused at: {win.position} (docked: {win.docked})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildPipeline] ❌ Failed to open BuildPipelineWindow: {ex}");
            }
        }

        [MenuItem("Tools/Wagenheimer/Build Pipeline/Reset Window Position (Center Screen)", priority = 114)]
        public static void ResetPosition()
        {
            var win = GetWindow<BuildPipelineWindow>(utility: false, title: "Build Pipeline", focus: true);
            var mainPos = EditorGUIUtility.GetMainWindowPosition();
            float w = Mathf.Min(880f, mainPos.width * 0.85f);
            float h = Mathf.Min(660f, mainPos.height * 0.85f);
            float x = mainPos.x + (mainPos.width - w) * 0.5f;
            float y = mainPos.y + (mainPos.height - h) * 0.5f;
            win.position = new Rect(x, y, w, h);
            win.Show();
            win.Focus();
            win.Repaint();
            Debug.Log($"[BuildPipeline] ✓ Build Pipeline Window position reset to center: {win.position}");
        }

        private ProjectBuildConfig _config;
        private VisualElement _root;
        private ScrollView _contentContainer;
        private int _selectedTab = 0; // 0=Quick Build, 1=Matrix, 2=History, 3=Vault, 4=CLI, 5=Config

        private bool[] _selectedPublishers;
        private bool[] _selectedLanguages;

        private void OnEnable()
        {
            try
            {
                _config = LegacyGameConfigMigrator.FindOrCreateProjectBuildConfig();
                InitMatrixArrays();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Window OnEnable warning: {ex.Message}");
            }
        }

        private void InitMatrixArrays()
        {
            if (_config == null) return;
            if (_config.publishers == null) _config.publishers = new System.Collections.Generic.List<PublisherProfile>();
            if (_config.languages == null) _config.languages = new System.Collections.Generic.List<LanguageProfile>();

            if (_selectedPublishers == null || _selectedPublishers.Length != _config.publishers.Count)
                _selectedPublishers = new bool[_config.publishers.Count];

            if (_selectedLanguages == null || _selectedLanguages.Length != _config.languages.Count)
            {
                _selectedLanguages = new bool[_config.languages.Count];
                for (var i = 0; i < _config.languages.Count; i++)
                {
                    if (_config.languages[i] != null)
                        _selectedLanguages[i] = _config.languages[i].enabled;
                }
            }
        }

        public void CreateGUI()
        {
            try
            {
                _root = rootVisualElement;
                _root.style.flexGrow = 1;
                _root.style.height = Length.Percent(100);
                _root.style.paddingTop = 10;
                _root.style.paddingBottom = 10;
                _root.style.paddingLeft = 12;
                _root.style.paddingRight = 12;

                BuildPipelineUIStyle.Apply(_root);
                RebuildUI();
                Debug.Log("[BuildPipeline] BuildPipelineWindow UI visual elements loaded successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildPipeline] Error initializing BuildPipelineWindow GUI: {ex}");
                rootVisualElement.Clear();
                var errBox = new HelpBox($"Failed to initialize Build Pipeline UI:\n{ex.Message}\n\n{ex.StackTrace}", HelpBoxMessageType.Error);
                rootVisualElement.Add(errBox);
                var retryBtn = new Button(RebuildUI) { text = "🔄 Retry" };
                retryBtn.style.height = 30;
                rootVisualElement.Add(retryBtn);
            }
        }

        private void RebuildUI()
        {
            _root.Clear();

            try
            {
                if (_config == null)
                    _config = LegacyGameConfigMigrator.FindOrCreateProjectBuildConfig();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] FindOrCreateProjectBuildConfig: {ex.Message}");
            }

            InitMatrixArrays();

            // 1. Unified Header (Project title, linked config, live version summary & steppers, save status)
            var headerView = new BuildPipelineHeaderView(_config, RebuildUI);
            _root.Add(headerView.Root);

            // 2. Tab Bar
            _root.Add(CreateTabBar());

            // 3. Tab Content Container
            _contentContainer = new ScrollView(ScrollViewMode.Vertical);
            _contentContainer.style.flexGrow = 1;
            _contentContainer.style.flexShrink = 1;
            _root.Add(_contentContainer);

            RebuildContent();
        }

        private VisualElement CreateTabBar()
        {
            var tabRow = new VisualElement();
            tabRow.AddToClassList("bp-tab-bar");
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.flexShrink = 0;
            tabRow.style.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 1f);
            tabRow.style.borderTopWidth = 1;
            tabRow.style.borderBottomWidth = 1;
            tabRow.style.borderLeftWidth = 1;
            tabRow.style.borderRightWidth = 1;
            tabRow.style.borderTopColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            tabRow.style.borderBottomColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            tabRow.style.borderLeftColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            tabRow.style.borderRightColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            tabRow.style.borderTopLeftRadius = 6;
            tabRow.style.borderTopRightRadius = 6;
            tabRow.style.borderBottomLeftRadius = 6;
            tabRow.style.borderBottomRightRadius = 6;
            tabRow.style.paddingTop = 3;
            tabRow.style.paddingBottom = 3;
            tabRow.style.paddingLeft = 3;
            tabRow.style.paddingRight = 3;
            tabRow.style.marginBottom = 12;

            (string icon, string label)[] tabs =
            {
                ("⚡", "Quick Build"),
                ("🏭", "Matrix Batch"),
                ("🕘", "Recent Builds"),
                ("🔐", "Keystore & Vault"),
                ("💻", "CLI & Automation"),
                ("⚙️", "Project Config"),
            };

            for (var i = 0; i < tabs.Length; i++)
            {
                var tabIndex = i;
                var isSelected = _selectedTab == tabIndex;

                var tabBtn = new Button(() =>
                {
                    _selectedTab = tabIndex;
                    RebuildUI();
                })
                { text = $"{tabs[i].icon} {tabs[i].label}" };

                tabBtn.AddToClassList("bp-tab-button");
                tabBtn.style.flexGrow = 1;
                tabBtn.style.flexShrink = 0;
                tabBtn.style.height = 30;
                tabBtn.style.marginLeft = 2;
                tabBtn.style.marginRight = 2;
                tabBtn.style.marginTop = 0;
                tabBtn.style.marginBottom = 0;
                tabBtn.style.paddingLeft = 6;
                tabBtn.style.paddingRight = 6;
                tabBtn.style.paddingTop = 0;
                tabBtn.style.paddingBottom = 0;
                tabBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                tabBtn.style.fontSize = 11;
                tabBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                tabBtn.style.borderTopLeftRadius = 4;
                tabBtn.style.borderTopRightRadius = 4;
                tabBtn.style.borderBottomLeftRadius = 4;
                tabBtn.style.borderBottomRightRadius = 4;

                if (isSelected)
                {
                    tabBtn.AddToClassList("bp-tab-button--active");
                    tabBtn.style.backgroundColor = new Color(0.17f, 0.24f, 0.36f, 1f);
                    tabBtn.style.color = Color.white;
                    tabBtn.style.borderBottomWidth = 2;
                    tabBtn.style.borderBottomColor = new Color(0.2f, 0.6f, 0.86f, 1f);
                    tabBtn.style.borderTopWidth = 1;
                    tabBtn.style.borderLeftWidth = 1;
                    tabBtn.style.borderRightWidth = 1;
                    tabBtn.style.borderTopColor = new Color(0.24f, 0.35f, 0.5f, 1f);
                    tabBtn.style.borderLeftColor = new Color(0.24f, 0.35f, 0.5f, 1f);
                    tabBtn.style.borderRightColor = new Color(0.24f, 0.35f, 0.5f, 1f);
                }
                else
                {
                    tabBtn.style.backgroundColor = Color.clear;
                    tabBtn.style.color = new Color(0.63f, 0.63f, 0.67f, 1f);
                    tabBtn.style.borderTopWidth = 0;
                    tabBtn.style.borderBottomWidth = 0;
                    tabBtn.style.borderLeftWidth = 0;
                    tabBtn.style.borderRightWidth = 0;
                }

                tabRow.Add(tabBtn);
            }

            return tabRow;
        }

        private void RebuildContent()
        {
            _contentContainer.Clear();
            switch (_selectedTab)
            {
                case 0:
                    _contentContainer.Add(new BuildPipelineQuickBuildView(_config, RebuildUI).Root);
                    break;
                case 1:
                    _contentContainer.Add(new BuildPipelineMatrixView(_config, _selectedPublishers, _selectedLanguages, RebuildUI).Root);
                    break;
                case 2:
                    _contentContainer.Add(new BuildPipelineHistoryView(RebuildUI).Root);
                    break;
                case 3:
                    _contentContainer.Add(new BuildPipelineVaultView(_config, RebuildUI).Root);
                    break;
                case 4:
                    _contentContainer.Add(new BuildPipelineCliView().Root);
                    break;
                case 5:
                    _contentContainer.Add(new BuildPipelineConfigView(_config, RebuildUI).Root);
                    break;
            }
        }
    }

    internal sealed class BuildPipelineConfigView
    {
        public VisualElement Root { get; }

        public BuildPipelineConfigView(ProjectBuildConfig config, Action onRebuild)
        {
            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            if (config == null)
            {
                Root.Add(BuildPipelineUIStyle.CreateCallout("No ProjectBuildConfig asset found. Click 'Create ProjectBuildConfig' or locate one in your project.", "warn"));
                return;
            }

            var so = new SerializedObject(config);
            Root.Bind(so);

            // Callout
            var info = BuildPipelineUIStyle.CreateCallout(
                "Global project build settings, target paths, keystore profiles, and store publisher definitions. Edits in these fields are tracked in memory until saved.",
                "info");
            Root.Add(info);

            // 1. Runtime Reference & Project Identity
            var identityCard = BuildPipelineUIStyle.CreateCard("Runtime Reference & Project Identity", "Game configuration link and application package names");
            identityCard.Add(new PropertyField(so.FindProperty("gameConfig"), "Runtime GameConfig.asset"));
            identityCard.Add(new PropertyField(so.FindProperty("projectName"), "Project Name"));
            identityCard.Add(new PropertyField(so.FindProperty("defaultBundleIdentifier"), "Default Bundle ID"));
            identityCard.Add(new PropertyField(so.FindProperty("gameNameJapanese"), "Japanese Game Name"));
            Root.Add(identityCard);

            // 2. Output Paths & Scenes
            var pathsCard = BuildPipelineUIStyle.CreateCard("Output Directories & Scene Paths", "Destination folders and splash/level editor scene assets");
            pathsCard.Add(new PropertyField(so.FindProperty("buildOutputRoot"), "Build Output (Windows)"));
            pathsCard.Add(new PropertyField(so.FindProperty("macBuildOutputRoot"), "Build Output (macOS)"));
            pathsCard.Add(new PropertyField(so.FindProperty("splashMasterFolder"), "Splash Master Folder"));
            pathsCard.Add(new PropertyField(so.FindProperty("publisherSplashScenePath"), "Publisher Splash Scene"));
            pathsCard.Add(new PropertyField(so.FindProperty("defaultSplashScenePath"), "Default Splash Scene"));
            pathsCard.Add(new PropertyField(so.FindProperty("levelEditorScenePath"), "Level Editor Scene"));
            Root.Add(pathsCard);

            // 3. Android Keystore & Remote Vault
            var vaultCard = BuildPipelineUIStyle.CreateCard("Android Keystore & Remote Vault", "Local signing keystore paths or secure remote vault credentials");
            vaultCard.Add(new PropertyField(so.FindProperty("keystoreSource"), "Keystore Source"));

            var localBox = new VisualElement();
            localBox.Add(new PropertyField(so.FindProperty("androidKeystorePath"), "Keystore Path (Windows)"));
            localBox.Add(new PropertyField(so.FindProperty("androidKeystorePathMac"), "Keystore Path (macOS)"));
            localBox.Add(new PropertyField(so.FindProperty("androidKeyAlias"), "Key Alias"));
            localBox.Add(new PropertyField(so.FindProperty("androidKeystorePassFallback"), "Keystore Password"));
            localBox.Add(new PropertyField(so.FindProperty("androidKeyaliasPassFallback"), "Keyalias Password"));
            vaultCard.Add(localBox);

            var remoteBox = new VisualElement();
            remoteBox.Add(new PropertyField(so.FindProperty("vaultUrl"), "Vault Endpoint URL"));
            remoteBox.Add(new PropertyField(so.FindProperty("vaultProfileId"), "Vault Profile ID"));
            remoteBox.Add(new PropertyField(so.FindProperty("vaultTokenFallback"), "Vault Token (Fallback)"));
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
            Root.Add(vaultCard);

            // 4. Publishers & Stores
            var pubsCard = BuildPipelineUIStyle.CreateCard("Publishers & Store Profiles", "Custom publishers, splash overlays, and store build variants");
            var pubProp = so.FindProperty("publishers");
            pubsCard.Add(new PropertyField(pubProp, "Configured Publishers"));

            var restorePubsBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Restore Default Publishers", "Reset publisher list to default presets?", "RESTORE", "CANCEL"))
                {
                    config.publishers = PublisherProfile.GetDefaultProfiles();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    onRebuild?.Invoke();
                }
            })
            { text = "↺ Restore Default Publishers List" };
            restorePubsBtn.AddToClassList("bp-btn");
            restorePubsBtn.style.marginTop = 6;
            pubsCard.Add(restorePubsBtn);
            Root.Add(pubsCard);

            // 5. Languages Matrix
            var langsCard = BuildPipelineUIStyle.CreateCard("Languages & Localizations", "Matrix languages, display names, and code mappings");
            var langProp = so.FindProperty("languages");
            langsCard.Add(new PropertyField(langProp, "Configured Languages"));

            var restoreLangsBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Restore Default Languages", "Reset language list to default presets?", "RESTORE", "CANCEL"))
                {
                    config.languages = LanguageProfile.GetDefaultLanguages();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                    onRebuild?.Invoke();
                }
            })
            { text = "↺ Restore Default Languages List" };
            restoreLangsBtn.AddToClassList("bp-btn");
            restoreLangsBtn.style.marginTop = 6;
            langsCard.Add(restoreLangsBtn);
            Root.Add(langsCard);

            // 6. Archiving & Compression
            var zipCard = BuildPipelineUIStyle.CreateCard("Archiving & 7-Zip Compression", "Automated ZIP packaging for Steam, BigFish, and standalone archives");
            zipCard.Add(new PropertyField(so.FindProperty("enableZipArchiving"), "Enable ZIP Archiving"));
            zipCard.Add(new PropertyField(so.FindProperty("useExternalSevenZip"), "Use External 7-Zip"));
            zipCard.Add(new PropertyField(so.FindProperty("sevenZipExecutable"), "7-Zip Executable Path"));
            Root.Add(zipCard);

            // 7. Bottom Save Status Bar
            Root.Add(BuildPipelineUIStyle.CreateSaveStatusBar(config, onRebuild));
        }
    }
}
