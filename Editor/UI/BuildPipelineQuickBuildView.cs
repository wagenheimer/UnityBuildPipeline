using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal sealed class BuildPipelineQuickBuildView
    {
        public VisualElement Root { get; }

        private readonly ProjectBuildConfig _config;
        private readonly Action _onRebuild;

        public BuildPipelineQuickBuildView(ProjectBuildConfig config, Action onRebuild)
        {
            _config = config;
            _onRebuild = onRebuild;

            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            var descCallout = BuildPipelineUIStyle.CreateCallout("Execute standard release and debug builds with a single click. PlayerSettings, keystores, and splash screens are applied automatically.", "info");
            Root.Add(descCallout);

            // Options row: Auto-bump toggle and Quick Build Language
            var optionsRow = new VisualElement();
            optionsRow.style.flexDirection = FlexDirection.Row;
            optionsRow.style.alignItems = Align.Center;
            optionsRow.style.justifyContent = Justify.SpaceBetween;
            optionsRow.style.marginBottom = 10;

            bool autoBump = EditorPrefs.GetBool("BuildPipeline_AutoBumpOnBuild", false);
            var bumpToggle = new Toggle("Auto-increment Build Code / Number on Quick Build") { value = autoBump };
            bumpToggle.RegisterValueChangedCallback(e =>
            {
                EditorPrefs.SetBool("BuildPipeline_AutoBumpOnBuild", e.newValue);
            });
            optionsRow.Add(bumpToggle);

            var langContainer = new VisualElement();
            langContainer.style.flexDirection = FlexDirection.Row;
            langContainer.style.alignItems = Align.Center;

            var langLabel = new Label("Target Language:");
            langLabel.style.fontSize = 11;
            langLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            langLabel.style.marginRight = 6;
            langContainer.Add(langLabel);

            var currentLang = _config != null ? _config.defaultLanguage : GameLanguage.AutoDetect;
            var langField = new EnumField(currentLang);
            langField.style.minWidth = 120;
            langField.RegisterValueChangedCallback(e =>
            {
                if (_config != null)
                {
                    _config.defaultLanguage = (GameLanguage)e.newValue;
                    EditorUtility.SetDirty(_config);
                    AssetDatabase.SaveAssetIfDirty(_config);
                }
            });
            langContainer.Add(langField);
            optionsRow.Add(langContainer);

            Root.Add(optionsRow);

            // Desktop Platform Section
            var desktopCard = BuildPipelineUIStyle.CreateCard("Desktop Targets (Windows & macOS)", "Standard desktop targets for PC distribution channels");
            var desktopGrid = new VisualElement();
            desktopGrid.AddToClassList("bp-grid");

            AddQuickCard(desktopGrid, "Big Fish Games", "Windows 64-bit | Full | Splash", Publisher.BigFish, PlatformType.Windows64, false, false, false, false);
            AddQuickCard(desktopGrid, "Steam", "Windows 64-bit | Full Game", Publisher.Steam, PlatformType.Windows64, false, false, false, false);
            AddQuickCard(desktopGrid, "GameHouse", "Windows 64-bit | Full | Splash", Publisher.GameHouse, PlatformType.Windows64, false, false, false, false);
            AddQuickCard(desktopGrid, "Green Sauce Games", "Windows 64-bit | Direct Full", Publisher.GreenSauceGames, PlatformType.Windows64, false, false, false, false);
            AddQuickCard(desktopGrid, "Windows (Cheat ON)", "Windows 64-bit | QA / Cheats Enabled", Publisher.Default, PlatformType.Windows64, true, false, false, false);
            AddQuickCard(desktopGrid, "Mac App Store", "macOS Universal / Silicon", Publisher.MacAppStore, PlatformType.macOS, false, false, false, false);
            AddQuickCard(desktopGrid, "Mac Game Store", "macOS | Full | Splash", Publisher.MacGameStore, PlatformType.macOS, false, false, false, false);

            desktopCard.Add(desktopGrid);
            Root.Add(desktopCard);

            // Mobile Platform Section
            var mobileCard = BuildPipelineUIStyle.CreateCard("Mobile Targets (Android & iOS)", "Google Play, Amazon Appstore, and Apple iOS configurations");
            var mobileGrid = new VisualElement();
            mobileGrid.AddToClassList("bp-grid");

            AddQuickCard(mobileGrid, "📱 Android Free (Debug AutoRun)", "Installs & launches on device via ADB", Publisher.GoogleAndroidFree, PlatformType.Android, false, false, true, true, isHighlight: true);
            AddQuickCard(mobileGrid, "Google Play (Full .aab)", "Production App Bundle", Publisher.GoogleAndroidFull, PlatformType.Android, false, true, false, false);
            AddQuickCard(mobileGrid, "Google Play (Free .aab)", "Production App Bundle (Ads/IAP)", Publisher.GoogleAndroidFree, PlatformType.Android, false, true, false, false);
            AddQuickCard(mobileGrid, "Google Play (Debug .apk)", "Quick sideload APK", Publisher.GoogleAndroidFree, PlatformType.Android, false, false, false, true);
            AddQuickCard(mobileGrid, "Amazon Store (Full .apk)", "Standalone Amazon Full APK", Publisher.AmazonAndroidFull, PlatformType.Android, false, false, false, false);
            AddQuickCard(mobileGrid, "Amazon Store (Free .apk)", "Standalone Amazon Free APK", Publisher.AmazonAndroidFree, PlatformType.Android, false, false, false, false);
            AddQuickCard(mobileGrid, "iOS Full (Xcode Project)", "Production Full Release", Publisher.iOSFull, PlatformType.iOS, false, true, false, false);
            AddQuickCard(mobileGrid, "iOS Free (Xcode Project)", "Production Free Release", Publisher.iOSFree, PlatformType.iOS, false, false, false, false);

            mobileCard.Add(mobileGrid);
            Root.Add(mobileCard);
        }

        private void AddQuickCard(VisualElement grid, string title, string subtitle, Publisher pub, PlatformType plat, bool cheat, bool appBundle, bool autoRun, bool devBuild, bool isHighlight = false)
        {
            var btn = new Button(() => RunQuickBuild(pub, plat, cheat, appBundle, autoRun, devBuild));
            btn.AddToClassList("bp-quick-card");
            if (isHighlight)
            {
                btn.AddToClassList("bp-quick-card--highlight");
            }

            var t = new Label(title);
            t.AddToClassList("bp-quick-title");
            btn.Add(t);

            var s = new Label(subtitle);
            s.AddToClassList("bp-quick-subtitle");
            btn.Add(s);

            grid.Add(btn);
        }

        private void RunQuickBuild(Publisher pub, PlatformType plat, bool cheat, bool appBundle, bool autoRun, bool devBuild)
        {
            if (EditorPrefs.GetBool("BuildPipeline_AutoBumpOnBuild", false))
            {
                if (plat == PlatformType.Android)
                {
                    int next = (PlayerSettings.Android.bundleVersionCode > 0 ? PlayerSettings.Android.bundleVersionCode : 0) + 1;
                    PlayerSettings.Android.bundleVersionCode = next;
                    if (_config?.gameConfig != null)
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
                    if (_config?.gameConfig != null)
                    {
                        _config.gameConfig.iOSBuildNumber = nextIos;
                        EditorUtility.SetDirty(_config.gameConfig);
                    }
                }
                AssetDatabase.SaveAssets();
                _onRebuild?.Invoke();
            }

            var prof = _config?.publishers?.FirstOrDefault(p => p.publisher == pub);
            var ctx = new BuildContext
            {
                Config = _config,
                Publisher = pub,
                PublisherProfile = prof,
                Language = _config != null ? _config.defaultLanguage : GameLanguage.AutoDetect,
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
    }
}

