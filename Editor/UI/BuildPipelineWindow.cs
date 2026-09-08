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
        public static void ShowWindow()
        {
            try
            {
                var win = GetWindow<BuildPipelineWindow>(typeof(SceneView));
                win.titleContent = new GUIContent("Build Pipeline", EditorGUIUtility.IconContent("BuildSettings.Editor").image);
                win.minSize = new Vector2(720, 540);
                win.Show();
                win.Focus();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Docked GetWindow fallback: {ex.Message}");
                var win = GetWindow<BuildPipelineWindow>("Build Pipeline");
                win.minSize = new Vector2(720, 540);
                win.Show();
                win.Focus();
            }
        }

        private ProjectBuildConfig _config;
        private VisualElement _root;
        private VisualElement _contentContainer;
        private int _selectedTab = 0; // 0 = Quick Build, 1 = Matrix, 2 = Keystore & Vault, 3 = CLI, 4 = Settings

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
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 12;
            header.style.paddingBottom = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);

            var titleBox = new VisualElement();
            var titleLabel = new Label("Unity Build Pipeline");
            titleLabel.style.fontSize = 18;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.2f, 0.8f, 0.9f);
            titleBox.Add(titleLabel);

            var projName = _config != null && !string.IsNullOrEmpty(_config.projectName) ? _config.projectName : "Storm Tale 2";
            string gameConfigName = "None";
            try
            {
                if (_config != null && _config.gameConfig != null)
                    gameConfigName = _config.gameConfig.name;
            }
            catch
            {
                gameConfigName = "None";
            }
            var subTitle = new Label($"Project: {projName}  |  Runtime Config: {gameConfigName}");
            subTitle.style.fontSize = 11;
            subTitle.style.color = new Color(0.7f, 0.7f, 0.7f);
            titleBox.Add(subTitle);
            header.Add(titleBox);

            var headerButtons = new VisualElement();
            headerButtons.style.flexDirection = FlexDirection.Row;

            var guideBtn = new Button(() => BuildPipelineGuideWindow.Open()) { text = "📖 Documentation & Guide" };
            guideBtn.style.height = 26;
            guideBtn.style.marginRight = 6;
            guideBtn.style.fontSize = 11;
            headerButtons.Add(guideBtn);

            var updateBtn = new Button(() => UpdateChecker.CheckForUpdate(force: true)) { text = "🔄 Check Updates" };
            updateBtn.style.height = 26;
            updateBtn.style.fontSize = 11;
            headerButtons.Add(updateBtn);

            header.Add(headerButtons);

            _root.Add(header);

            // Version Management Bar
            _root.Add(CreateVersionBar());

            // Tab Buttons
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginBottom = 10;

            string[] tabNames = { "⚡ Quick Build", "🏭 Matrix Batch Builder", "🔐 Keystore & Vault", "💻 CLI & Automation", "⚙️ Project Config" };
            for (var i = 0; i < tabNames.Length; i++)
            {
                var tabIndex = i;
                var tabBtn = new Button(() =>
                {
                    _selectedTab = tabIndex;
                    RebuildUI();
                })
                { text = tabNames[i] };
                tabBtn.style.flexGrow = 1;
                tabBtn.style.height = 28;
                tabBtn.style.fontSize = 11;
                if (_selectedTab == tabIndex)
                {
                    tabBtn.style.backgroundColor = new Color(0.2f, 0.45f, 0.7f);
                    tabBtn.style.color = Color.white;
                    tabBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                tabRow.Add(tabBtn);
            }
            _root.Add(tabRow);

            _contentContainer = new ScrollView(ScrollViewMode.Vertical);
            _contentContainer.style.flexGrow = 1;
            _contentContainer.style.flexShrink = 1;
            _root.Add(_contentContainer);

            RebuildContent();
        }

        private VisualElement CreateVersionBar()
        {
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            card.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.marginBottom = 10;
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;

            var versionText = "v1.0.0";
            var dateText = DateTime.Now.ToString("MMM dd, yyyy");
            if (_config != null && _config.gameConfig != null)
            {
                try
                {
                    if (_config.gameConfig.GameVersion != null)
                        versionText = $"v{_config.gameConfig.GameVersion.GameVersionAsTextWithBetaLabel}";
                    if (_config.gameConfig.VersionDate != null)
                        dateText = _config.gameConfig.VersionDate.AsText;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BuildPipeline] Version read warning: {ex.Message}");
                }
            }

            var verLabel = new Label($"Version: {versionText}  ({dateText})");
            verLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            verLabel.style.fontSize = 13;
            verLabel.style.flexGrow = 1;
            card.Add(verLabel);

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
            { text = "+ Major" };
            btnMajor.style.width = 65;

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
            { text = "+ Minor" };
            btnMinor.style.width = 65;

            var btnBuild = new Button(() =>
            {
                if (_config != null && _config.gameConfig != null && _config.gameConfig.GameVersion != null)
                {
                    _config.gameConfig.GameVersion.Build++;
                    EditorUtility.SetDirty(_config.gameConfig);
                    RebuildUI();
                }
            })
            { text = "+ Build" };
            btnBuild.style.width = 65;

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
            { text = "Today" };
            btnToday.style.width = 60;

            card.Add(btnMajor);
            card.Add(btnMinor);
            card.Add(btnBuild);
            card.Add(btnToday);

            return card;
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
                    BuildKeystoreVaultTab();
                    break;
                case 3:
                    BuildCliTab();
                    break;
                case 4:
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
