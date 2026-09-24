using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    [CustomEditor(typeof(GameConfig))]
    public class GameConfigCustomEditor : UnityEditor.Editor
    {
        private ProjectBuildConfig _linkedBuildConfig;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            BuildPipelineUIStyle.Apply(root);

            var config = (GameConfig)target;

            if (_linkedBuildConfig == null)
                _linkedBuildConfig = LegacyGameConfigMigrator.FindOrCreateProjectBuildConfig();

            EnsureSubObjects(config);

            // 1. Hero Banner
            var headerCard = BuildPipelineUIStyle.CreateCard("🎮 Game Configuration (Runtime Source of Truth)", "Version, build numbers, active store, and assets for runtime execution");

            var actionRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };

            var openWinBtn = new Button(BuildPipelineWindow.ShowWindow) { text = "🚀 Open Build Pipeline Window" };
            openWinBtn.AddToClassList("bp-btn");
            openWinBtn.AddToClassList("bp-btn--primary");
            openWinBtn.style.flexGrow = 1;
            openWinBtn.style.height = 30;
            openWinBtn.style.fontSize = 12;
            actionRow.Add(openWinBtn);

            if (_linkedBuildConfig != null)
            {
                var pingCfgBtn = new Button(() =>
                {
                    Selection.activeObject = _linkedBuildConfig;
                    EditorGUIUtility.PingObject(_linkedBuildConfig);
                })
                { text = "📁 ProjectBuildConfig" };
                pingCfgBtn.AddToClassList("bp-btn");
                pingCfgBtn.style.height = 30;
                pingCfgBtn.style.fontSize = 11;
                actionRow.Add(pingCfgBtn);
            }

            var guideBtn = new Button(BuildPipelineGuideWindow.Open) { text = "📖 Guide" };
            guideBtn.AddToClassList("bp-btn");
            guideBtn.style.height = 30;
            guideBtn.style.fontSize = 11;
            actionRow.Add(guideBtn);

            headerCard.Add(actionRow);
            root.Add(headerCard);

            // 2. Live Save Status Bar
            root.Add(BuildPipelineUIStyle.CreateSaveStatusBar(config));

            // 3. Digital Version & Build Numbers Card
            root.Add(CreateVersionCard(config));

            // 4. Core Runtime Settings Card
            var coreCard = BuildPipelineUIStyle.CreateCard("Core Runtime Settings", "Active store and language flags applied during build and playback");
            coreCard.Add(new PropertyField(serializedObject.FindProperty("Publisher"), "Target Publisher"));
            coreCard.Add(new PropertyField(serializedObject.FindProperty("GameLanguage"), "Default Game Language"));

            var flagsBox = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };
            flagsBox.Add(new PropertyField(serializedObject.FindProperty("CheatMode"), "Cheat Mode") { style = { marginRight = 12 } });
            flagsBox.Add(new PropertyField(serializedObject.FindProperty("FullGame"), "Full Game") { style = { marginRight = 12 } });
            flagsBox.Add(new PropertyField(serializedObject.FindProperty("Demo"), "Demo Build") { style = { marginRight = 12 } });
            flagsBox.Add(new PropertyField(serializedObject.FindProperty("UseAchievements"), "Achievements"));
            coreCard.Add(flagsBox);
            root.Add(coreCard);

            // 5. Store Identifiers & Package IDs
            var storesCard = BuildPipelineUIStyle.CreateCard("Store Identifiers & Package IDs");
            storesCard.Add(new PropertyField(serializedObject.FindProperty("DefaultBundleIdentifier"), "Default Bundle ID"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("AndroidFull"), "Google Play (Full Package)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("AndroidFree"), "Google Play (Free Package)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("IOSFull"), "iOS (Full Bundle ID)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("IOSFree"), "iOS (Free Bundle ID)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("iOSAppIDFull"), "Apple App ID (Full)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("iOSAppIDFree"), "Apple App ID (Free)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("AmazonFull"), "Amazon (Full Package)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("AmazonFree"), "Amazon (Free Package)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("SamsungFull"), "Samsung Galaxy (Full Package)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("SamsungFree"), "Samsung Galaxy (Free Package)"));
            storesCard.Add(new PropertyField(serializedObject.FindProperty("MacAppStoreID"), "Mac App Store ID"));
            root.Add(storesCard);

            // 6. Visual Assets & Sprite Atlases
            var assetsCard = BuildPipelineUIStyle.CreateCard("Visual Assets & Sprite Atlases");
            assetsCard.Add(new PropertyField(serializedObject.FindProperty("HudSpriteAtlas"), "HUD Sprite Atlas"));
            assetsCard.Add(new PropertyField(serializedObject.FindProperty("LevelHudSpriteAtlas"), "Level HUD Sprite Atlas"));
            assetsCard.Add(new PropertyField(serializedObject.FindProperty("DefaultSpriteAtlas"), "Default Sprite Atlas"));
            assetsCard.Add(new PropertyField(serializedObject.FindProperty("cursorTexture"), "Cursor Texture"));
            assetsCard.Add(new PropertyField(serializedObject.FindProperty("IconFull"), "Application Icon (Full)"));
            assetsCard.Add(new PropertyField(serializedObject.FindProperty("IconFree"), "Application Icon (Free)"));
            root.Add(assetsCard);

            // 7. Extra Configuration Flags
            var extraCard = BuildPipelineUIStyle.CreateCard("Extra Settings & Options");
            extraCard.Add(new PropertyField(serializedObject.FindProperty("FreeToPlay"), "Free To Play Mode"));
            extraCard.Add(new PropertyField(serializedObject.FindProperty("NoCustomCursor"), "Disable Custom Cursor"));
            extraCard.Add(new PropertyField(serializedObject.FindProperty("LogLevelsInfo"), "Log Levels Info"));
            extraCard.Add(new PropertyField(serializedObject.FindProperty("LevelEditor"), "Enable Level Editor"));
            extraCard.Add(new PropertyField(serializedObject.FindProperty("ExternalTranslation"), "External Translation"));
            extraCard.Add(new PropertyField(serializedObject.FindProperty("UseOnlyEditorPlayer"), "Use Only Editor Player"));
            extraCard.Add(new PropertyField(serializedObject.FindProperty("CanChangeLanguage"), "Can Change Language In-Game"));
            root.Add(extraCard);

            return root;
        }

        private VisualElement CreateVersionCard(GameConfig config)
        {
            var card = BuildPipelineUIStyle.CreateCard("📦 Version & Build Numbers (Single Source of Truth)");

            var verText = config.GameVersion != null ? $"v{config.GameVersion.GameVersionAsTextWithBetaLabel}" : "v1.0.0";
            var dateText = config.VersionDate != null ? config.VersionDate.AsText : DateTime.Now.ToString("MMM dd, yyyy");

            var statusRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 8 } };
            var badge = BuildPipelineUIStyle.CreateBadge($"{verText}  •  {dateText}  •  🤖 #{config.AndroidBundleVersionCode}  •  🍎 #{config.iOSBuildNumber}", "ok");
            badge.style.fontSize = 11;
            statusRow.Add(badge);
            card.Add(statusRow);

            var stepperRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };

            AddStepperBtn(stepperRow, "+ Major", () =>
            {
                if (config.GameVersion == null) config.GameVersion = new GameVersion();
                config.GameVersion.Major++;
                config.GameVersion.Minor = 0;
                config.GameVersion.Build = 0;
                EditorUtility.SetDirty(config);
            });

            AddStepperBtn(stepperRow, "+ Minor", () =>
            {
                if (config.GameVersion == null) config.GameVersion = new GameVersion();
                config.GameVersion.Minor++;
                config.GameVersion.Build = 0;
                EditorUtility.SetDirty(config);
            });

            AddStepperBtn(stepperRow, "+ Build", () =>
            {
                if (config.GameVersion == null) config.GameVersion = new GameVersion();
                config.GameVersion.Build++;
                EditorUtility.SetDirty(config);
            });

            AddStepperBtn(stepperRow, "📅 Today", () =>
            {
                config.VersionDate = new GameBuildDate(DateTime.Now);
                EditorUtility.SetDirty(config);
            });

            AddStepperBtn(stepperRow, "🤖 Code +1", () =>
            {
                config.AndroidBundleVersionCode++;
                PlayerSettings.Android.bundleVersionCode = config.AndroidBundleVersionCode;
                EditorUtility.SetDirty(config);
            });

            AddStepperBtn(stepperRow, "🍎 iOS +1", () =>
            {
                int.TryParse(config.iOSBuildNumber, out int cur);
                config.iOSBuildNumber = (cur + 1).ToString();
                PlayerSettings.iOS.buildNumber = config.iOSBuildNumber;
                EditorUtility.SetDirty(config);
            });

            card.Add(stepperRow);
            return card;
        }

        private static void AddStepperBtn(VisualElement parent, string text, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.AddToClassList("bp-btn");
            btn.style.height = 24;
            btn.style.fontSize = 11;
            btn.style.marginRight = 4;
            btn.style.marginBottom = 4;
            parent.Add(btn);
        }

        private void EnsureSubObjects(GameConfig config)
        {
            if (config.GameVersion == null)
            {
                config.GameVersion = new GameVersion(1, 0, 0);
                EditorUtility.SetDirty(config);
            }
            if (config.VersionDate == null)
            {
                config.VersionDate = new GameBuildDate(DateTime.Now);
                EditorUtility.SetDirty(config);
            }
            if (config.AndroidBundleVersionCode <= 0)
            {
                config.AndroidBundleVersionCode = PlayerSettings.Android.bundleVersionCode > 0
                    ? PlayerSettings.Android.bundleVersionCode
                    : 1;
                EditorUtility.SetDirty(config);
            }
            if (string.IsNullOrEmpty(config.iOSBuildNumber))
            {
                config.iOSBuildNumber = !string.IsNullOrEmpty(PlayerSettings.iOS.buildNumber)
                    ? PlayerSettings.iOS.buildNumber
                    : "1";
                EditorUtility.SetDirty(config);
            }
        }
    }
}
