using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class BuildPipelineWindow : EditorWindow
    {
        [MenuItem("Tools/Build Pipeline/Open Build Window", priority = 0)]
        [MenuItem("Window/Build Pipeline", priority = 205)]
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

        [MenuItem("Tools/Build Pipeline/Reset Window Position (Center Screen)", priority = 50)]
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
        private VisualElement _contentContainer;
        private int _selectedTab = 0; // 0 = Quick Build, 1 = Matrix, 2 = Recent Builds, 3 = Keystore & Vault, 4 = CLI, 5 = Project Config

        private bool[] _selectedPublishers;
        private bool[] _selectedLanguages;
        private bool _matrixCheatOn = false;
        private bool _matrixCheatOff = true;
        private bool _matrixDevBuild = false;

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

                RebuildUI();
                Debug.Log("[BuildPipeline] BuildPipelineWindow UI visual elements loaded successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildPipeline] Error initializing BuildPipelineWindow GUI: {ex}");
                rootVisualElement.Clear();
                var errBox = new HelpBox($"Erro ao inicializar a interface do Build Pipeline:\n{ex.Message}\n\n{ex.StackTrace}", HelpBoxMessageType.Error);
                rootVisualElement.Add(errBox);
                var retryBtn = new Button(() => RebuildUI()) { text = "🔄 Tentar Novamente" };
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

            // Header: Title & Project Identity + Quick Action Buttons
            _root.Add(CreateHeaderBar());

            // Live save/dirty status — mirrors the banner in GameConfig/ProjectBuildConfig Inspectors
            _root.Add(CreateSaveStatusBar());

            // Version Management Bar
            _root.Add(CreateVersionBar());

            // Tab Buttons (segmented control)
            _root.Add(CreateTabBar());

            _contentContainer = new ScrollView(ScrollViewMode.Vertical);
            _contentContainer.style.flexGrow = 1;
            _contentContainer.style.flexShrink = 1;
            _root.Add(_contentContainer);

            RebuildContent();
        }

        private static readonly Color ColAccent = new(0.20f, 0.75f, 0.90f);
        private static readonly Color ColPanelBg = new(0.18f, 0.18f, 0.18f);
        private static readonly Color ColDim = new(0.65f, 0.65f, 0.65f);
        private static readonly Color ColWarnBg = new(0.40f, 0.24f, 0.05f);
        private static readonly Color ColWarnAccent = new(0.95f, 0.65f, 0.15f);
        private static readonly Color ColOkBg = new(0.10f, 0.22f, 0.15f);
        private static readonly Color ColOkAccent = new(0.30f, 0.80f, 0.50f);

        private VisualElement CreateHeaderBar()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 10;
            header.style.paddingBottom = 10;
            header.style.borderBottomWidth = 2;
            header.style.borderBottomColor = ColAccent;

            var titleBox = new VisualElement();
            var titleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var titleIcon = new Label("🚀") { style = { fontSize = 18, marginRight = 6 } };
            var titleLabel = new Label("Unity Build Pipeline");
            titleLabel.style.fontSize = 18;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = ColAccent;
            titleRow.Add(titleIcon);
            titleRow.Add(titleLabel);
            titleBox.Add(titleRow);

            var projName = _config != null && !string.IsNullOrEmpty(_config.projectName)
                ? _config.projectName
                : (!string.IsNullOrEmpty(Application.productName) ? Application.productName : "Untitled Project");
            string gameConfigName = "Nenhum vinculado";
            try
            {
                if (_config != null && _config.gameConfig != null)
                    gameConfigName = _config.gameConfig.name;
            }
            catch
            {
                gameConfigName = "Nenhum vinculado";
            }
            var subTitle = new Label($"📁 Projeto: {projName}    •    🎮 GameConfig: {gameConfigName}");
            subTitle.style.fontSize = 11;
            subTitle.style.marginTop = 2;
            subTitle.style.color = ColDim;
            titleBox.Add(subTitle);
            header.Add(titleBox);

            var headerButtons = new VisualElement();
            headerButtons.style.flexDirection = FlexDirection.Row;

            var guideBtn = new Button(() => BuildPipelineGuideWindow.Open()) { text = "📖 Guia" };
            guideBtn.tooltip = "Documentação completa: arquitetura, CLI, matriz de builds, vault de keystore.";
            guideBtn.style.height = 26;
            guideBtn.style.marginRight = 6;
            guideBtn.style.fontSize = 11;
            headerButtons.Add(guideBtn);

            var updateBtn = new Button(() => UpdateChecker.CheckForUpdate(force: true)) { text = "🔄 Atualizações" };
            updateBtn.tooltip = "Verifica se há uma versão mais nova do pacote UnityBuildPipeline.";
            updateBtn.style.height = 26;
            updateBtn.style.fontSize = 11;
            headerButtons.Add(updateBtn);

            header.Add(headerButtons);
            return header;
        }

        /// <summary>
        /// Makes the exact moment a version/build-number edit reaches disk (and therefore git) explicit,
        /// instead of relying on the user remembering to press Ctrl+S.
        /// </summary>
        private VisualElement CreateSaveStatusBar()
        {
            bool isDirty = _config != null && _config.gameConfig != null && EditorUtility.IsDirty(_config.gameConfig);

            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.justifyContent = Justify.SpaceBetween;
            bar.style.paddingTop = 5;
            bar.style.paddingBottom = 5;
            bar.style.paddingLeft = 10;
            bar.style.paddingRight = 10;
            bar.style.marginBottom = 8;
            bar.style.borderTopLeftRadius = 4;
            bar.style.borderTopRightRadius = 4;
            bar.style.borderBottomLeftRadius = 4;
            bar.style.borderBottomRightRadius = 4;
            bar.style.backgroundColor = isDirty ? ColWarnBg : ColOkBg;

            var label = new Label(isDirty
                ? "⚠ Alterações em memória — ainda NÃO gravadas em disco (não aparecem no 'git status')."
                : "✔ Tudo salvo em disco.");
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = Color.white;
            bar.Add(label);

            if (isDirty)
            {
                var saveBtn = new Button(() =>
                {
                    AssetDatabase.SaveAssetIfDirty(_config.gameConfig);
                    if (_config != null) AssetDatabase.SaveAssetIfDirty(_config);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[BuildPipeline] Configuração salva em disco. Confira o 'git status' agora.");
                    RebuildUI();
                })
                { text = "💾  Salvar Agora" };
                saveBtn.style.height = 22;
                saveBtn.style.fontSize = 11;
                saveBtn.style.backgroundColor = ColWarnAccent;
                saveBtn.style.color = Color.white;
                saveBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                bar.Add(saveBtn);
            }

            return bar;
        }

        private VisualElement CreateTabBar()
        {
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginBottom = 10;

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
                tabBtn.style.flexGrow = 1;
                tabBtn.style.height = 30;
                tabBtn.style.fontSize = 11;
                tabBtn.style.marginLeft = 0;
                tabBtn.style.marginRight = i < tabs.Length - 1 ? 2 : 0;
                tabBtn.style.borderTopLeftRadius = 4;
                tabBtn.style.borderTopRightRadius = 4;
                tabBtn.style.borderBottomLeftRadius = 0;
                tabBtn.style.borderBottomRightRadius = 0;
                tabBtn.style.borderBottomWidth = 3;

                if (isSelected)
                {
                    tabBtn.style.backgroundColor = new Color(0.16f, 0.30f, 0.38f);
                    tabBtn.style.borderBottomColor = ColAccent;
                    tabBtn.style.color = Color.white;
                    tabBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                else
                {
                    tabBtn.style.borderBottomColor = new Color(0, 0, 0, 0);
                    var normalBg = tabBtn.style.backgroundColor;
                    tabBtn.RegisterCallback<PointerEnterEvent>(_ => tabBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.30f));
                    tabBtn.RegisterCallback<PointerLeaveEvent>(_ => tabBtn.style.backgroundColor = normalBg);
                }

                tabRow.Add(tabBtn);
            }

            return tabRow;
        }

        private VisualElement CreateVersionBar()
        {
            var card = new VisualElement();
            card.style.backgroundColor = ColPanelBg;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.marginBottom = 10;
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;

            var versionText = "v1.0.0";
            var dateText = DateTime.Now.ToString("MMM dd, yyyy");
            int andCode = PlayerSettings.Android.bundleVersionCode > 0 ? PlayerSettings.Android.bundleVersionCode : 1;
            string iosNum = !string.IsNullOrEmpty(PlayerSettings.iOS.buildNumber) ? PlayerSettings.iOS.buildNumber : "1";

            if (_config != null && _config.gameConfig != null)
            {
                try
                {
                    if (_config.gameConfig.GameVersion != null)
                        versionText = $"v{_config.gameConfig.GameVersion.GameVersionAsTextWithBetaLabel}";
                    if (_config.gameConfig.VersionDate != null)
                        dateText = _config.gameConfig.VersionDate.AsText;
                    if (_config.gameConfig.AndroidBundleVersionCode > 0)
                        andCode = _config.gameConfig.AndroidBundleVersionCode;
                    if (!string.IsNullOrEmpty(_config.gameConfig.iOSBuildNumber))
                        iosNum = _config.gameConfig.iOSBuildNumber;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BuildPipeline] Version read warning: {ex.Message}");
                }
            }

            // Row 1: section title + live summary
            var titleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            var sectionTitle = new Label("📦 Versão & Build Numbers");
            sectionTitle.style.fontSize = 11;
            sectionTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            sectionTitle.style.color = ColDim;
            titleRow.Add(sectionTitle);

            var spacer0 = new VisualElement { style = { flexGrow = 1 } };
            titleRow.Add(spacer0);

            var summaryLabel = new Label($"{versionText}  •  {dateText}  •  🤖 #{andCode}  •  🍎 #{iosNum}");
            summaryLabel.style.fontSize = 11;
            summaryLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            summaryLabel.style.color = Color.white;
            titleRow.Add(summaryLabel);
            card.Add(titleRow);

            // Row 2: grouped clusters, separated visually so it's obvious what each button changes
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexEnd, flexWrap = Wrap.Wrap } };

            var btnMajor = new Button(() =>
            {
                if (_config != null && _config.gameConfig != null && _config.gameConfig.GameVersion != null)
                {
                    _config.gameConfig.GameVersion.Major++;
                    _config.gameConfig.GameVersion.Minor = 0;
                    _config.gameConfig.GameVersion.Build = 0;
                    EditorUtility.SetDirty(_config.gameConfig);
                    RebuildUI();
                }
            })
            { text = "+ Major", tooltip = "Incrementa a versão principal (X.0.0) e zera Minor/Build." };
            btnMajor.style.width = 58;

            var btnMinor = new Button(() =>
            {
                if (_config != null && _config.gameConfig != null && _config.gameConfig.GameVersion != null)
                {
                    _config.gameConfig.GameVersion.Minor++;
                    _config.gameConfig.GameVersion.Build = 0;
                    EditorUtility.SetDirty(_config.gameConfig);
                    RebuildUI();
                }
            })
            { text = "+ Minor", tooltip = "Incrementa a versão secundária (x.X.0) e zera Build." };
            btnMinor.style.width = 58;

            var btnBuild = new Button(() =>
            {
                if (_config != null && _config.gameConfig != null && _config.gameConfig.GameVersion != null)
                {
                    _config.gameConfig.GameVersion.Build++;
                    EditorUtility.SetDirty(_config.gameConfig);
                    RebuildUI();
                }
            })
            { text = "+ Build", tooltip = "Incrementa apenas o número de build (x.x.X)." };
            btnBuild.style.width = 58;

            var btnToday = new Button(() =>
            {
                if (_config != null && _config.gameConfig != null && _config.gameConfig.VersionDate != null)
                {
                    var now = DateTime.Now;
                    _config.gameConfig.VersionDate.Day = now.Day;
                    _config.gameConfig.VersionDate.Month = now.Month;
                    _config.gameConfig.VersionDate.Year = now.Year;
                    EditorUtility.SetDirty(_config.gameConfig);
                    RebuildUI();
                }
            })
            { text = "📅 Hoje", tooltip = "Define a data de release como hoje." };
            btnToday.style.width = 58;

            row.Add(CreateButtonGroup("VERSÃO DO JOGO", btnMajor, btnMinor, btnBuild, btnToday));
            row.Add(CreateVerticalSeparator());

            var btnAnd = new Button(() =>
            {
                int next = andCode + 1;
                PlayerSettings.Android.bundleVersionCode = next;
                if (_config != null && _config.gameConfig != null)
                {
                    _config.gameConfig.AndroidBundleVersionCode = next;
                    EditorUtility.SetDirty(_config.gameConfig);
                }
                RebuildUI();
            })
            { text = "+1 Android", tooltip = "Incrementa AndroidBundleVersionCode (obrigatório subir a cada release na Google Play)." };
            btnAnd.style.width = 82;
            row.Add(CreateButtonGroup("🤖 ANDROID BUNDLE CODE", btnAnd));
            row.Add(CreateVerticalSeparator());

            var btnIos = new Button(() =>
            {
                int.TryParse(iosNum, out int curIos);
                string nextIos = (curIos + 1).ToString();
                PlayerSettings.iOS.buildNumber = nextIos;
                PlayerSettings.macOS.buildNumber = nextIos;
                if (_config != null && _config.gameConfig != null)
                {
                    _config.gameConfig.iOSBuildNumber = nextIos;
                    EditorUtility.SetDirty(_config.gameConfig);
                }
                RebuildUI();
            })
            { text = "+1 iOS", tooltip = "Incrementa iOSBuildNumber (CFBundleVersion) para App Store / TestFlight." };
            btnIos.style.width = 58;
            row.Add(CreateButtonGroup("🍎 iOS / macOS BUILD", btnIos));
            row.Add(CreateVerticalSeparator());

            var autoBumpToggle = new Toggle("Auto +1 ao buildar")
            {
                value = EditorPrefs.GetBool("BuildPipeline_AutoBumpOnBuild", false),
                tooltip = "Incrementa automaticamente o build number (Android/iOS/macOS) sempre que um build for gerado por esta janela."
            };
            autoBumpToggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool("BuildPipeline_AutoBumpOnBuild", evt.newValue);
            });
            row.Add(CreateButtonGroup("AUTOMAÇÃO", autoBumpToggle));

            card.Add(row);
            return card;
        }

        private VisualElement CreateButtonGroup(string label, params VisualElement[] controls)
        {
            var group = new VisualElement { style = { marginRight = 10 } };
            var groupLabel = new Label(label);
            groupLabel.style.fontSize = 9;
            groupLabel.style.color = ColDim;
            groupLabel.style.marginBottom = 3;
            group.Add(groupLabel);

            var controlsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            foreach (var c in controls)
            {
                c.style.marginRight = 4;
                controlsRow.Add(c);
            }
            group.Add(controlsRow);
            return group;
        }

        private VisualElement CreateVerticalSeparator()
        {
            var sep = new VisualElement();
            sep.style.width = 1;
            sep.style.marginRight = 10;
            sep.style.marginLeft = 0;
            sep.style.backgroundColor = new Color(0.32f, 0.32f, 0.32f);
            sep.style.alignSelf = Align.Stretch;
            return sep;
        }

        private void RebuildContent()
        {
            _contentContainer.Clear();
            switch (_selectedTab)
            {
                case 0:
                    BuildQuickBuildTab();
                    break;
                case 1:
                    BuildMatrixTab();
                    break;
                case 2:
                    BuildHistoryTab();
                    break;
                case 3:
                    BuildKeystoreVaultTab();
                    break;
                case 4:
                    BuildCliTab();
                    break;
                case 5:
                    BuildConfigTab();
                    break;
            }
        }

        #region Quick Build Tab
        private void BuildQuickBuildTab()
        {
            var container = new VisualElement();

            var desc = new Label("Execute standard release and debug builds with a single click:");
            desc.style.marginBottom = 10;
            desc.style.color = new Color(0.7f, 0.7f, 0.7f);
            container.Add(desc);

            var groupDesktop = CreatePlatformSection("Desktop (Windows & macOS)", new[]
            {
                (Publisher.BigFish, "Big Fish Games (Full, Splash)", PlatformType.Windows64, false, false, false, false),
                (Publisher.Steam, "Steam (Full)", PlatformType.Windows64, false, false, false, false),
                (Publisher.GameHouse, "GameHouse (Full, Splash)", PlatformType.Windows64, false, false, false, false),
                (Publisher.GreenSauceGames, "Green Sauce Games (Direct Full)", PlatformType.Windows64, false, false, false, false),
                (Publisher.Default, "Windows (Cheat Mode ON)", PlatformType.Windows64, true, false, false, false),
                (Publisher.MacAppStore, "Mac App Store", PlatformType.macOS, false, false, false, false),
                (Publisher.MacGameStore, "Mac Game Store (Full, Splash)", PlatformType.macOS, false, false, false, false),
            });
            container.Add(groupDesktop);

            var groupMobile = CreatePlatformSection("Mobile (Android & iOS)", new[]
            {
                (Publisher.GoogleAndroidFree, "📱 Android Free (DEBUG + AutoRun + Logcat)", PlatformType.Android, false, false, true, true),
                (Publisher.GoogleAndroidFull, "Google Play (Full .aab)", PlatformType.Android, false, true, false, false),
                (Publisher.GoogleAndroidFree, "Google Play (Free .aab)", PlatformType.Android, false, true, false, false),
                (Publisher.GoogleAndroidFree, "Google Play (Debug .apk)", PlatformType.Android, false, false, false, true),
                (Publisher.AmazonAndroidFull, "Amazon Store (Full .apk)", PlatformType.Android, false, false, false, false),
                (Publisher.AmazonAndroidFree, "Amazon Store (Free .apk)", PlatformType.Android, false, false, false, false),
                (Publisher.iOSFull, "iOS (Full Xcode Project)", PlatformType.iOS, false, true, false, false),
                (Publisher.iOSFree, "iOS (Free Xcode Project)", PlatformType.iOS, false, false, false, false),
            });
            container.Add(groupMobile);

            _contentContainer.Add(container);
        }

        private VisualElement CreatePlatformSection(string title, (Publisher pub, string label, PlatformType plat, bool cheat, bool appBundle, bool autoRun, bool devBuild)[] items)
        {
            var section = new VisualElement();
            section.style.marginBottom = 16;

            var header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 13;
            header.style.marginBottom = 6;
            header.style.color = new Color(0.85f, 0.85f, 0.85f);
            section.Add(header);

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;

            foreach (var item in items)
            {
                var card = new Button(() =>
                {
                    RunQuickBuild(item.pub, item.plat, item.cheat, item.appBundle, item.autoRun, item.devBuild);
                })
                { text = item.label };

                card.style.width = item.autoRun ? 280 : 210;
                card.style.height = 34;
                card.style.marginBottom = 6;
                card.style.marginRight = 6;
                if (item.autoRun)
                {
                    card.style.backgroundColor = new Color(0.15f, 0.55f, 0.35f);
                    card.style.color = Color.white;
                    card.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                grid.Add(card);
            }

            section.Add(grid);
            return section;
        }

        private void RunQuickBuild(Publisher pub, PlatformType plat, bool cheat, bool appBundle, bool autoRun = false, bool devBuild = false)
        {
            if (EditorPrefs.GetBool("BuildPipeline_AutoBumpOnBuild", false))
            {
                if (plat == PlatformType.Android)
                {
                    int next = (PlayerSettings.Android.bundleVersionCode > 0 ? PlayerSettings.Android.bundleVersionCode : 0) + 1;
                    PlayerSettings.Android.bundleVersionCode = next;
                    if (_config != null && _config.gameConfig != null)
                    {
                        _config.gameConfig.AndroidBundleVersionCode = next;
                        EditorUtility.SetDirty(_config.gameConfig);
                    }
                }
                else if (plat == PlatformType.iOS || plat == PlatformType.macOS)
                {
                    int.TryParse(PlayerSettings.iOS.buildNumber, out int curIos);
                    string nextIos = (curIos + 1).ToString();
                    PlayerSettings.iOS.buildNumber = nextIos;
                    PlayerSettings.macOS.buildNumber = nextIos;
                    if (_config != null && _config.gameConfig != null)
                    {
                        _config.gameConfig.iOSBuildNumber = nextIos;
                        EditorUtility.SetDirty(_config.gameConfig);
                    }
                }
                AssetDatabase.SaveAssets();
            }

            var prof = _config.publishers.FirstOrDefault(p => p.publisher == pub);
            var ctx = new BuildContext
            {
                Config = _config,
                Publisher = pub,
                PublisherProfile = prof,
                Language = GameLanguage.AutoDetect,
                Platform = plat,
                CheatMode = cheat,
                DevelopmentBuild = devBuild,
                Demo = prof != null && prof.isDemo,
                AppBundle = appBundle,
                AutoRun = autoRun
            };

            var res = BuildPipelineRunner.Execute(ctx);
            if (res.Success)
            {
                EditorUtility.DisplayDialog("Build Succeeded", $"Build finished successfully in {res.Duration:mm\\:ss}!\nPath: {res.OutputPath}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Build Failed", $"Build failed with {res.TotalErrors} errors.\nCheck console for details.", "OK");
            }
        }
        #endregion

        #region Matrix Tab
        private void BuildMatrixTab()
        {
            var container = new VisualElement();

            var headerBox = new VisualElement();
            headerBox.style.flexDirection = FlexDirection.Row;
            headerBox.style.justifyContent = Justify.SpaceBetween;
            headerBox.style.alignItems = Align.Center;
            headerBox.style.marginBottom = 8;

            var selPubs = _selectedPublishers != null ? _selectedPublishers.Count(p => p) : 0;
            var selLangs = _selectedLanguages != null ? _selectedLanguages.Count(l => l) : 0;
            var cheatsCount = (_matrixCheatOn ? 1 : 0) + (_matrixCheatOff ? 1 : 0);
            var totalBuilds = selPubs * selLangs * cheatsCount;

            var summaryLabel = new Label($"Selected: {selPubs} Publishers  x  {selLangs} Languages  x  {cheatsCount} Cheat modes  =  <b>{totalBuilds} Total Builds</b>");
            summaryLabel.style.fontSize = 13;
            summaryLabel.style.color = new Color(0.9f, 0.9f, 0.2f);
            headerBox.Add(summaryLabel);

            container.Add(headerBox);

            // Checkbox Columns: Publishers & Languages
            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.marginBottom = 12;

            // Publishers Column
            var pubCol = new VisualElement();
            pubCol.style.flexGrow = 1;
            pubCol.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            pubCol.style.paddingTop = 6;
            pubCol.style.paddingBottom = 6;
            pubCol.style.paddingLeft = 8;
            pubCol.style.paddingRight = 8;
            pubCol.style.marginRight = 6;
            pubCol.style.borderTopLeftRadius = 4;
            pubCol.style.borderTopRightRadius = 4;
            pubCol.style.borderBottomLeftRadius = 4;
            pubCol.style.borderBottomRightRadius = 4;

            var pubHeader = new VisualElement();
            pubHeader.style.flexDirection = FlexDirection.Row;
            pubHeader.style.justifyContent = Justify.SpaceBetween;
            pubHeader.Add(new Label("Publishers (Windows)") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            var pubSelectAll = new Button(() =>
            {
                for (var i = 0; i < _selectedPublishers.Length; i++) _selectedPublishers[i] = true;
                RebuildContent();
            })
            { text = "All" };
            pubSelectAll.style.width = 40;

            var pubSelectNone = new Button(() =>
            {
                for (var i = 0; i < _selectedPublishers.Length; i++) _selectedPublishers[i] = false;
                RebuildContent();
            })
            { text = "None" };
            pubSelectNone.style.width = 45;

            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            btnRow.Add(pubSelectAll);
            btnRow.Add(pubSelectNone);
            pubHeader.Add(btnRow);
            pubCol.Add(pubHeader);

            for (var i = 0; i < _config.publishers.Count; i++)
            {
                var idx = i;
                var pub = _config.publishers[i];
                if (pub.platform != PlatformType.Windows64) continue;

                var toggle = new Toggle(pub.displayName) { value = _selectedPublishers[idx] };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _selectedPublishers[idx] = evt.newValue;
                    RebuildContent();
                });
                pubCol.Add(toggle);
            }
            columns.Add(pubCol);

            // Languages Column
            var langCol = new VisualElement();
            langCol.style.flexGrow = 1;
            langCol.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            langCol.style.paddingTop = 6;
            langCol.style.paddingBottom = 6;
            langCol.style.paddingLeft = 8;
            langCol.style.paddingRight = 8;
            langCol.style.borderTopLeftRadius = 4;
            langCol.style.borderTopRightRadius = 4;
            langCol.style.borderBottomLeftRadius = 4;
            langCol.style.borderBottomRightRadius = 4;

            var langHeader = new VisualElement();
            langHeader.style.flexDirection = FlexDirection.Row;
            langHeader.style.justifyContent = Justify.SpaceBetween;
            langHeader.Add(new Label("Languages") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            var langSelectAll = new Button(() =>
            {
                for (var i = 0; i < _selectedLanguages.Length; i++) _selectedLanguages[i] = true;
                RebuildContent();
            })
            { text = "All" };
            langSelectAll.style.width = 40;

            var langSelectNone = new Button(() =>
            {
                for (var i = 0; i < _selectedLanguages.Length; i++) _selectedLanguages[i] = false;
                RebuildContent();
            })
            { text = "None" };
            langSelectNone.style.width = 45;

            var langBtnRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            langBtnRow.Add(langSelectAll);
            langBtnRow.Add(langSelectNone);
            langHeader.Add(langBtnRow);
            langCol.Add(langHeader);

            for (var i = 0; i < _config.languages.Count; i++)
            {
                var idx = i;
                var lang = _config.languages[i];
                var toggle = new Toggle($"{lang.Name} ({lang.language})") { value = _selectedLanguages[idx] };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _selectedLanguages[idx] = evt.newValue;
                    RebuildContent();
                });
                langCol.Add(toggle);
            }
            columns.Add(langCol);

            container.Add(columns);

            // Options & Action Bar
            var optionsBox = new VisualElement();
            optionsBox.style.flexDirection = FlexDirection.Row;
            optionsBox.style.alignItems = Align.Center;
            optionsBox.style.marginBottom = 12;

            var togCheatOff = new Toggle("Cheat OFF") { value = _matrixCheatOff };
            togCheatOff.RegisterValueChangedCallback(e => { _matrixCheatOff = e.newValue; RebuildContent(); });
            optionsBox.Add(togCheatOff);

            var togCheatOn = new Toggle("Cheat ON") { value = _matrixCheatOn };
            togCheatOn.style.marginLeft = 16;
            togCheatOn.RegisterValueChangedCallback(e => { _matrixCheatOn = e.newValue; RebuildContent(); });
            optionsBox.Add(togCheatOn);

            var togDev = new Toggle("Development Build") { value = _matrixDevBuild };
            togDev.style.marginLeft = 16;
            togDev.RegisterValueChangedCallback(e => { _matrixDevBuild = e.newValue; });
            optionsBox.Add(togDev);

            container.Add(optionsBox);

            var buildMatrixBtn = new Button(() =>
            {
                RunMatrixBuild();
            })
            { text = $"Generate {totalBuilds} Builds" };
            buildMatrixBtn.style.height = 38;
            buildMatrixBtn.style.fontSize = 14;
            buildMatrixBtn.style.backgroundColor = totalBuilds > 0 ? new Color(0.18f, 0.65f, 0.35f) : new Color(0.3f, 0.3f, 0.3f);
            buildMatrixBtn.style.color = Color.white;
            buildMatrixBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            buildMatrixBtn.SetEnabled(totalBuilds > 0);
            container.Add(buildMatrixBtn);

            _contentContainer.Add(container);
        }

        private void RunMatrixBuild()
        {
            var pubs = _config.publishers.Where((p, idx) => _selectedPublishers[idx]).ToList();
            var langs = _config.languages.Where((l, idx) => _selectedLanguages[idx]).Select(l => l.language).ToList();

            var count = 0;
            var total = pubs.Count * langs.Count * ((_matrixCheatOn ? 1 : 0) + (_matrixCheatOff ? 1 : 0));

            if (!EditorUtility.DisplayDialog("Confirm Matrix Build", $"Generate {total} builds now?", "YES, START", "CANCEL"))
                return;

            try
            {
                foreach (var lang in langs)
                {
                    foreach (var pub in pubs)
                    {
                        if (_matrixCheatOff)
                        {
                            count++;
                            EditorUtility.DisplayProgressBar("Building Matrix...", $"[{count}/{total}] {pub.displayName} ({lang})", (float)count / total);
                            var ctx = new BuildContext
                            {
                                Config = _config,
                                Publisher = pub.publisher,
                                PublisherProfile = pub,
                                Language = lang,
                                Platform = pub.platform,
                                CheatMode = false,
                                DevelopmentBuild = _matrixDevBuild,
                                Demo = pub.isDemo
                            };
                            BuildPipelineRunner.Execute(ctx);
                        }

                        if (_matrixCheatOn)
                        {
                            count++;
                            EditorUtility.DisplayProgressBar("Building Matrix...", $"[{count}/{total}] {pub.displayName} ({lang}) [CHEAT]", (float)count / total);
                            var ctx = new BuildContext
                            {
                                Config = _config,
                                Publisher = pub.publisher,
                                PublisherProfile = pub,
                                Language = lang,
                                Platform = pub.platform,
                                CheatMode = true,
                                DevelopmentBuild = _matrixDevBuild,
                                Demo = pub.isDemo
                            };
                            BuildPipelineRunner.Execute(ctx);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Matrix Complete", $"Generated {count} builds.", "OK");
            }
        }
        #endregion

        #region Recent Builds Tab
        private void BuildHistoryTab()
        {
            var container = new VisualElement();

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 10;

            var desc = new Label("Last 30 builds recorded on this machine. Open the output folder or run them again (APK installs to the connected device):");
            desc.style.color = new Color(0.7f, 0.7f, 0.7f);
            headerRow.Add(desc);

            var clearBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Clear Build History", "Remove all entries from the build history?", "CLEAR", "CANCEL"))
                {
                    BuildHistory.Clear();
                    RebuildContent();
                }
            })
            { text = "🗑 Clear" };
            clearBtn.style.height = 22;
            clearBtn.style.fontSize = 10;
            headerRow.Add(clearBtn);

            container.Add(headerRow);

            // Toolchain status line so the user can verify which bundletool/adb will run AAB/APKs.
            var btPath = BuildHistory.GetBundletoolPath();
            var btLabel = new Label($"bundletool.jar: {(string.IsNullOrEmpty(btPath) ? "NOT FOUND (will offer download)" : btPath)}");
            btLabel.style.fontSize = 10;
            btLabel.style.whiteSpace = WhiteSpace.Normal;
            btLabel.style.marginBottom = 8;
            btLabel.style.color = string.IsNullOrEmpty(btPath) ? new Color(0.9f, 0.6f, 0.3f) : new Color(0.5f, 0.8f, 0.5f);
            container.Add(btLabel);

            var entries = BuildHistory.Entries;
            if (entries.Count == 0)
            {
                var empty = new Label("No builds recorded yet. Run a build from Quick Build or Matrix to populate this list.");
                empty.style.color = new Color(0.5f, 0.5f, 0.5f);
                empty.style.paddingTop = 20;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                container.Add(empty);
                _contentContainer.Add(container);
                return;
            }

            foreach (var entry in entries)
            {
                container.Add(CreateHistoryEntryRow(entry));
            }

            _contentContainer.Add(container);
        }

        private VisualElement CreateHistoryEntryRow(BuildHistoryEntry entry)
        {
            var row = new VisualElement();
            row.style.backgroundColor = entry.success ? new Color(0.16f, 0.18f, 0.16f) : new Color(0.20f, 0.14f, 0.14f);
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.paddingLeft = 10;
            row.style.paddingRight = 10;
            row.style.marginBottom = 4;
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;

            var info = new Label(
                $"{(entry.success ? "✓" : "✗")} {entry.Time:dd/MM/yyyy HH:mm}  |  {entry.platform}  |  {entry.publisher}  |  {entry.language}" +
                $"{(entry.cheatMode ? "  |  CHEAT" : "")}{(entry.developmentBuild ? "  |  DEV" : "")}" +
                $"  |  {entry.durationSeconds:F0}s{(entry.totalSize > 0 ? $"  |  {entry.totalSize / (1024.0 * 1024.0):F1} MB" : "")}");
            info.style.fontSize = 11;
            info.style.color = entry.success ? new Color(0.75f, 0.85f, 0.75f) : new Color(0.9f, 0.6f, 0.6f);
            info.style.whiteSpace = WhiteSpace.Normal;
            row.Add(info);

            var pathLbl = new Label(entry.outputPath);
            pathLbl.style.fontSize = 10;
            pathLbl.style.color = new Color(0.6f, 0.65f, 0.7f);
            pathLbl.style.whiteSpace = WhiteSpace.Normal;
            row.Add(pathLbl);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 4;

            var folderBtn = new Button(() => BuildHistory.OpenFolder(entry)) { text = "📂 Open Folder" };
            folderBtn.style.height = 20;
            folderBtn.style.fontSize = 10;
            folderBtn.style.marginRight = 6;
            btnRow.Add(folderBtn);

            var runLabel = entry.platform == "Android" ? (entry.IsAab ? "▶️ Install & Run (bundletool)" : "▶️ Install & Run") : "▶️ Run";
            var runBtn = new Button(() =>
            {
                BuildHistory.Run(entry);
            })
            { text = runLabel };
            runBtn.style.height = 20;
            runBtn.style.fontSize = 10;
            runBtn.SetEnabled(BuildHistory.CanRun(entry));
            btnRow.Add(runBtn);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            btnRow.Add(spacer);

            var removeBtn = new Button(() =>
            {
                BuildHistory.Remove(entry);
                RebuildContent();
            })
            { text = "✕" };
            removeBtn.style.width = 24;
            removeBtn.style.height = 20;
            btnRow.Add(removeBtn);

            row.Add(btnRow);

            return row;
        }
        #endregion

        #region Keystore & Vault Tab
        private void BuildKeystoreVaultTab()
        {
            var container = new VisualElement();

            // Header Banner
            var banner = new VisualElement();
            banner.style.backgroundColor = new Color(0.12f, 0.16f, 0.22f);
            banner.style.paddingTop = 10;
            banner.style.paddingBottom = 10;
            banner.style.paddingLeft = 14;
            banner.style.paddingRight = 14;
            banner.style.marginBottom = 14;
            banner.style.borderTopLeftRadius = 6;
            banner.style.borderTopRightRadius = 6;
            banner.style.borderBottomLeftRadius = 6;
            banner.style.borderBottomRightRadius = 6;
            banner.style.borderLeftWidth = 4;
            banner.style.borderLeftColor = new Color(0.2f, 0.7f, 1f);

            var bannerTitle = new Label("🔐 Android Keystore & Central Vault Management");
            bannerTitle.style.fontSize = 14;
            bannerTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            bannerTitle.style.color = new Color(0.3f, 0.8f, 1f);
            banner.Add(bannerTitle);

            var bannerDesc = new Label(
                "Gerencie e valide as credenciais de assinatura para builds Android (.apk e .aab). " +
                "O modo padrão recomendado é o Keystore Vault Centralizado (as chaves e senhas ficam na nuvem segura e nunca vão para o Git).");
            bannerDesc.style.fontSize = 11;
            bannerDesc.style.color = new Color(0.75f, 0.8f, 0.85f);
            bannerDesc.style.whiteSpace = WhiteSpace.Normal;
            bannerDesc.style.marginTop = 4;
            banner.Add(bannerDesc);

            container.Add(banner);

            // Card 1: Remote Keystore Vault
            var vaultCard = new VisualElement();
            vaultCard.style.backgroundColor = new Color(0.16f, 0.18f, 0.2f);
            vaultCard.style.paddingTop = 12;
            vaultCard.style.paddingBottom = 12;
            vaultCard.style.paddingLeft = 14;
            vaultCard.style.paddingRight = 14;
            vaultCard.style.marginBottom = 12;
            vaultCard.style.borderTopLeftRadius = 6;
            vaultCard.style.borderTopRightRadius = 6;
            vaultCard.style.borderBottomLeftRadius = 6;
            vaultCard.style.borderBottomRightRadius = 6;

            var vaultTitleRow = new VisualElement();
            vaultTitleRow.style.flexDirection = FlexDirection.Row;
            vaultTitleRow.style.justifyContent = Justify.SpaceBetween;
            vaultTitleRow.style.alignItems = Align.Center;
            vaultTitleRow.style.marginBottom = 8;

            var vaultTitle = new Label("🌐 Central Keystore Vault (wagenheimer.com)");
            vaultTitle.style.fontSize = 13;
            vaultTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            vaultTitle.style.color = new Color(0.4f, 0.85f, 0.5f);
            vaultTitleRow.Add(vaultTitle);

            var openWebBtn = new Button(() => Application.OpenURL("https://wagenheimer.com/admin/keystores"))
            {
                text = "Abrir Portal Web ↗"
            };
            openWebBtn.style.height = 24;
            openWebBtn.style.fontSize = 10;
            vaultTitleRow.Add(openWebBtn);
            vaultCard.Add(vaultTitleRow);

            var currentToken = KeystoreVaultClient.GetEffectiveToken(_config);
            var hasToken = !string.IsNullOrEmpty(currentToken);

            var vaultStatus = new VisualElement();
            vaultStatus.style.flexDirection = FlexDirection.Row;
            vaultStatus.style.marginBottom = 8;

            var tokenBadge = new Label(hasToken ? "● Token Ativo" : "○ Sem Token");
            tokenBadge.style.fontSize = 11;
            tokenBadge.style.color = hasToken ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.9f, 0.6f, 0.2f);
            tokenBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            vaultStatus.Add(tokenBadge);

            var vaultUrlText = _config != null && !string.IsNullOrEmpty(_config.vaultUrl) ? _config.vaultUrl : "https://wagenheimer.com/api/vault/keystore";
            var vaultUrlLabel = new Label($"  |  Endpoint: {vaultUrlText}");
            vaultUrlLabel.style.fontSize = 11;
            vaultUrlLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            vaultStatus.Add(vaultUrlLabel);

            var profileIdText = _config != null && !string.IsNullOrEmpty(_config.vaultProfileId) ? _config.vaultProfileId : "(auto por bundle id)";
            var profileIdLabel = new Label($"  |  Perfil: {profileIdText}");
            profileIdLabel.style.fontSize = 11;
            profileIdLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            vaultStatus.Add(profileIdLabel);
            vaultCard.Add(vaultStatus);

            var testVaultBtn = new Button(async () =>
            {
                EditorUtility.DisplayProgressBar("Vault Connection", "Consultando servidor de credenciais...", 0.5f);
                try
                {
                    var effectiveUrl = _config != null && !string.IsNullOrEmpty(_config.vaultUrl) ? _config.vaultUrl : "https://wagenheimer.com/api/vault/keystore";
                    var token = KeystoreVaultClient.GetEffectiveToken(_config);
                    var profId = _config != null ? _config.vaultProfileId : "";

                    var result = await KeystoreVaultClient.FetchCredentialsAsync(
                        effectiveUrl,
                        profId,
                        PlayerSettings.applicationIdentifier,
                        token);

                    EditorUtility.ClearProgressBar();
                    if (result.Success && result.Credentials != null)
                    {
                        EditorUtility.DisplayDialog("Sucesso - Conexão com Vault",
                            $"✅ Conexão com o Vault estabelecida com sucesso!\n\n" +
                            $"Perfil ID: {result.Credentials.ProfileId}\n" +
                            $"Arquivo: {result.Credentials.KeystoreFileName}\n" +
                            $"Key Alias: {result.Credentials.KeyAliasName}\n" +
                            $"Status: Autenticado e pronto para builds headless!", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Aviso - Vault",
                            $"Não foi possível obter as credenciais do Vault:\n\n{result.Message}\n\n" +
                            $"Dica: Verifique se o perfil '{profId}' está cadastrado no portal wagenheimer.com/admin/keystores e se o Token está correto.", "OK");
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            })
            {
                text = "⚡ Testar Conexão com o Vault Remoto"
            };
            testVaultBtn.style.height = 30;
            testVaultBtn.style.backgroundColor = new Color(0.15f, 0.45f, 0.65f);
            testVaultBtn.style.color = Color.white;
            testVaultBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            vaultCard.Add(testVaultBtn);

            container.Add(vaultCard);

            // Card 2: Local Keystore File
            var localCard = new VisualElement();
            localCard.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            localCard.style.paddingTop = 12;
            localCard.style.paddingBottom = 12;
            localCard.style.paddingLeft = 14;
            localCard.style.paddingRight = 14;
            localCard.style.marginBottom = 12;
            localCard.style.borderTopLeftRadius = 6;
            localCard.style.borderTopRightRadius = 6;
            localCard.style.borderBottomLeftRadius = 6;
            localCard.style.borderBottomRightRadius = 6;

            var localTitle = new Label("📁 Local Keystore File (Fallback / Offline)");
            localTitle.style.fontSize = 13;
            localTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            localTitle.style.color = new Color(0.9f, 0.75f, 0.3f);
            localTitle.style.marginBottom = 8;
            localCard.Add(localTitle);

            string ksPath = "";
            bool pathExists = false;
            try
            {
                ksPath = _config != null ? _config.GetEffectiveKeystorePath()?.Replace("\r", "")?.Replace("\n", "")?.Trim() : "";
                pathExists = !string.IsNullOrEmpty(ksPath) && File.Exists(ksPath);
            }
            catch
            {
                pathExists = false;
            }

            var localAlias = _config != null && !string.IsNullOrEmpty(_config.androidKeyAlias) ? _config.androidKeyAlias : "(vazio)";
            var localInfo = new Label($"Arquivo: {(string.IsNullOrEmpty(ksPath) ? "(não configurado)" : ksPath)}\nAlias: {localAlias}");
            localInfo.style.fontSize = 11;
            localInfo.style.color = pathExists ? new Color(0.4f, 0.85f, 0.4f) : new Color(0.7f, 0.7f, 0.7f);
            localInfo.style.marginBottom = 8;
            localCard.Add(localInfo);

            var testLocalBtn = new Button(() =>
            {
                if (_config == null) return;
                var safePath = _config.GetEffectiveKeystorePath()?.Replace("\r", "")?.Replace("\n", "")?.Trim();
                var val = KeystoreVaultClient.ValidateLocalKeystore(
                    safePath,
                    _config.androidKeyAlias,
                    _config.GetEffectiveKeystorePassword(),
                    _config.GetEffectiveKeyaliasPassword());

                if (val.Success)
                {
                    EditorUtility.DisplayDialog("Validação do Keystore Local", $"✅ {val.Message}", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Falha na Validação do Keystore Local", $"❌ {val.Message}", "OK");
                }
            })
            {
                text = "🔍 Testar & Validar Keystore Local (keytool.exe)"
            };
            testLocalBtn.style.height = 30;
            testLocalBtn.style.backgroundColor = new Color(0.25f, 0.4f, 0.25f);
            testLocalBtn.style.color = Color.white;
            testLocalBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            localCard.Add(testLocalBtn);

            container.Add(localCard);

            // Card 3: Security Best Practices
            var infoCard = new VisualElement();
            infoCard.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
            infoCard.style.paddingTop = 10;
            infoCard.style.paddingBottom = 10;
            infoCard.style.paddingLeft = 12;
            infoCard.style.paddingRight = 12;
            infoCard.style.borderTopLeftRadius = 4;
            infoCard.style.borderTopRightRadius = 4;
            infoCard.style.borderBottomLeftRadius = 4;
            infoCard.style.borderBottomRightRadius = 4;

            var tipTitle = new Label("💡 Boas Práticas de Segurança & CI/CD:");
            tipTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            tipTitle.style.fontSize = 11;
            tipTitle.style.marginBottom = 4;
            tipTitle.style.color = new Color(0.8f, 0.8f, 0.8f);
            infoCard.Add(tipTitle);

            var tipText = new Label(
                "• O Keystore Vault injeta credenciais em tempo de build diretamente nos PlayerSettings em memória.\n" +
                "• Nenhuma senha ou arquivo binário sensível de keystore precisa ser comitado nos repositórios Git.\n" +
                "• No CI/CD (GitHub Actions / Jenkins), defina a variável de ambiente VAULT_SECRET_TOKEN.\n" +
                "• No Unity Editor, você pode configurar o token em Configurações do Projeto ou salvá-lo com segurança nos EditorPrefs locais da sua máquina.");
            tipText.style.fontSize = 10;
            tipText.style.color = new Color(0.65f, 0.65f, 0.65f);
            tipText.style.whiteSpace = WhiteSpace.Normal;
            infoCard.Add(tipText);

            container.Add(infoCard);

            _contentContainer.Add(container);
        }
        #endregion

        #region CLI Tab
        private void BuildCliTab()
        {
            var container = new VisualElement();

            var info = new Label("Use the Unity CLI commands below to execute builds headlessly outside Unity (via PowerShell, CMD, or CI/CD pipelines):");
            info.style.marginBottom = 12;
            info.style.color = new Color(0.7f, 0.7f, 0.7f);
            container.Add(info);

            var unityPath = EditorApplication.applicationPath;
            var projPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            var singleCli = $"& \"{unityPath}\" -batchmode -nographics -quit `\n" +
                            $"  -projectPath \"{projPath}\" `\n" +
                            $"  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.Build `\n" +
                            $"  -publisher BigFish `\n" +
                            $"  -language English `\n" +
                            $"  -platform Windows64 `\n" +
                            $"  -cheat false `\n" +
                            $"  -logFile \"build_bigfish.log\"";

            var matrixCli = $"& \"{unityPath}\" -batchmode -nographics -quit `\n" +
                            $"  -projectPath \"{projPath}\" `\n" +
                            $"  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildMatrix `\n" +
                            $"  -matrixPublishers \"BigFish,Steam,GameHouse\" `\n" +
                            $"  -matrixLanguages \"English,German,French\" `\n" +
                            $"  -cheatOff true `\n" +
                            $"  -cheatOn false `\n" +
                            $"  -logFile \"build_matrix.log\"";

            container.Add(CreateCliBox("Single Target Build Command (PowerShell):", singleCli));
            container.Add(CreateCliBox("Matrix Batch Build Command (PowerShell):", matrixCli));

            var scriptInfo = new Label("A dedicated PowerShell runner script is also included at: \n`Packages/com.wagenheimer.buildpipeline/Tools/build.ps1`\n\nRun directly in terminal:\n`./build.ps1 -publisher BigFish -language de`");
            scriptInfo.style.marginTop = 12;
            scriptInfo.style.paddingTop = 8;
            scriptInfo.style.paddingBottom = 8;
            scriptInfo.style.paddingLeft = 10;
            scriptInfo.style.paddingRight = 10;
            scriptInfo.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            container.Add(scriptInfo);

            _contentContainer.Add(container);
        }

        private VisualElement CreateCliBox(string title, string command)
        {
            var box = new VisualElement();
            box.style.marginBottom = 14;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;

            var titleLbl = new Label(title);
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(titleLbl);

            var copyBtn = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = command;
                EditorUtility.DisplayDialog("Copied", "Command copied to clipboard!", "OK");
            })
            { text = "Copy Command" };
            copyBtn.style.width = 110;
            header.Add(copyBtn);

            box.Add(header);

            var text = new TextField { value = command, multiline = true };
            text.isReadOnly = true;
            text.style.marginTop = 4;
            // standard font used by default
            box.Add(text);

            return box;
        }
        #endregion

        #region Config Tab
        private void BuildConfigTab()
        {
            var container = new VisualElement();

            var editor = UnityEditor.Editor.CreateEditor(_config);
            var imgui = new IMGUIContainer(() =>
            {
                if (editor != null)
                {
                    editor.OnInspectorGUI();
                }
            });
            container.Add(imgui);

            _contentContainer.Add(container);
        }
        #endregion
    }
}
