using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal sealed class BuildPipelineHeaderView
    {
        public VisualElement Root { get; }

        public BuildPipelineHeaderView(ProjectBuildConfig config, Action onRebuild)
        {
            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            // 1. Header Row: Title & Action Buttons
            var header = new VisualElement();
            header.AddToClassList("bp-header");

            var row = new VisualElement();
            row.AddToClassList("bp-header-row");

            var left = new VisualElement();
            left.AddToClassList("bp-header-left");

            var titleIcon = new Label("🚀") { style = { fontSize = 18, marginRight = 6 } };
            var title = new Label("Unity Build Pipeline");
            title.AddToClassList("bp-header-title");
            left.Add(titleIcon);
            left.Add(title);

            var verBadge = new Label("v" + GetPackageVersion());
            verBadge.AddToClassList("bp-header-version");
            left.Add(verBadge);

            row.Add(left);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("bp-toolbar-actions");

            var guideBtn = new Button(BuildPipelineGuideWindow.Open) { text = "📖 Guide" };
            guideBtn.AddToClassList("bp-toolbar-btn");
            toolbar.Add(guideBtn);

            var updateBtn = new Button(() => UpdateChecker.CheckForUpdate(force: true)) { text = "🔄 Check Updates" };
            updateBtn.AddToClassList("bp-toolbar-btn");
            toolbar.Add(updateBtn);

            row.Add(toolbar);
            header.Add(row);

            // Project Identity Subtitle
            var projName = config != null && !string.IsNullOrEmpty(config.projectName)
                ? config.projectName
                : (!string.IsNullOrEmpty(Application.productName) ? Application.productName : "Untitled Project");

            string gameConfigName = "None linked";
            if (config != null && config.gameConfig != null)
            {
                gameConfigName = config.gameConfig.name;
            }

            var meta = new Label($"📁 Project: {projName}    •    🎮 GameConfig: {gameConfigName}");
            meta.AddToClassList("bp-header-meta");
            header.Add(meta);

            Root.Add(header);

            // 2. Save Status Bar
            if (config != null && config.gameConfig != null)
            {
                Root.Add(BuildPipelineUIStyle.CreateSaveStatusBar(config.gameConfig, onRebuild));
            }

            // 3. Version & Build Numbers Bar
            Root.Add(CreateVersionBar(config, onRebuild));
        }

        private VisualElement CreateVersionBar(ProjectBuildConfig config, Action onRebuild)
        {
            var bar = new VisualElement();
            bar.AddToClassList("bp-version-bar");

            var versionText = "v1.0.0";
            var dateText = DateTime.Now.ToString("MMM dd, yyyy");
            int andCode = PlayerSettings.Android.bundleVersionCode > 0 ? PlayerSettings.Android.bundleVersionCode : 1;
            string iosNum = !string.IsNullOrEmpty(PlayerSettings.iOS.buildNumber) ? PlayerSettings.iOS.buildNumber : "1";

            if (config != null && config.gameConfig != null)
            {
                try
                {
                    if (config.gameConfig.GameVersion != null)
                        versionText = $"v{config.gameConfig.GameVersion.GameVersionAsTextWithBetaLabel}";
                    if (config.gameConfig.VersionDate != null)
                        dateText = config.gameConfig.VersionDate.AsText;
                    if (config.gameConfig.AndroidBundleVersionCode > 0)
                        andCode = config.gameConfig.AndroidBundleVersionCode;
                    if (!string.IsNullOrEmpty(config.gameConfig.iOSBuildNumber))
                        iosNum = config.gameConfig.iOSBuildNumber;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BuildPipeline] Version read warning: {ex.Message}");
                }
            }

            // Left Side: Live Summary
            var leftGroup = new VisualElement();
            leftGroup.AddToClassList("bp-version-group");

            var icon = new Label("📦") { style = { marginRight = 5 } };
            leftGroup.Add(icon);

            var summary = new Label($"{versionText}  •  {dateText}  •  🤖 #{andCode}  •  🍎 #{iosNum}");
            summary.AddToClassList("bp-version-val");
            leftGroup.Add(summary);

            bar.Add(leftGroup);

            // Right Side: Steppers
            var steppers = new VisualElement();
            steppers.style.flexDirection = FlexDirection.Row;
            steppers.style.alignItems = Align.Center;

            // Major / Minor / Build Stepper
            AddStepperBtn(steppers, "+ Maj", "Increment Major version (X.0.0)", () =>
            {
                if (config?.gameConfig?.GameVersion == null) return;
                config.gameConfig.GameVersion.Major++;
                config.gameConfig.GameVersion.Minor = 0;
                config.gameConfig.GameVersion.Build = 0;
                EditorUtility.SetDirty(config.gameConfig);
                onRebuild?.Invoke();
            });

            AddStepperBtn(steppers, "+ Min", "Increment Minor version (x.X.0)", () =>
            {
                if (config?.gameConfig?.GameVersion == null) return;
                config.gameConfig.GameVersion.Minor++;
                config.gameConfig.GameVersion.Build = 0;
                EditorUtility.SetDirty(config.gameConfig);
                onRebuild?.Invoke();
            });

            AddStepperBtn(steppers, "+ Bld", "Increment Build version (x.x.X)", () =>
            {
                if (config?.gameConfig?.GameVersion == null) return;
                config.gameConfig.GameVersion.Build++;
                EditorUtility.SetDirty(config.gameConfig);
                onRebuild?.Invoke();
            });

            // Date Today Button
            AddStepperBtn(steppers, "📅 Today", "Update VersionDate to today", () =>
            {
                if (config?.gameConfig == null) return;
                config.gameConfig.VersionDate = new GameBuildDate(DateTime.Now);
                EditorUtility.SetDirty(config.gameConfig);
                onRebuild?.Invoke();
            });

            // Android Code Stepper
            AddStepperBtn(steppers, "🤖 +1", "Increment Android bundleVersionCode", () =>
            {
                if (config?.gameConfig == null) return;
                config.gameConfig.AndroidBundleVersionCode = Mathf.Max(1, config.gameConfig.AndroidBundleVersionCode + 1);
                PlayerSettings.Android.bundleVersionCode = config.gameConfig.AndroidBundleVersionCode;
                EditorUtility.SetDirty(config.gameConfig);
                onRebuild?.Invoke();
            });

            // iOS Number Stepper
            AddStepperBtn(steppers, "🍎 +1", "Increment iOS build number", () =>
            {
                if (config?.gameConfig == null) return;
                int.TryParse(config.gameConfig.iOSBuildNumber, out int cur);
                config.gameConfig.iOSBuildNumber = Mathf.Max(1, cur + 1).ToString();
                PlayerSettings.iOS.buildNumber = config.gameConfig.iOSBuildNumber;
                EditorUtility.SetDirty(config.gameConfig);
                onRebuild?.Invoke();
            });

            bar.Add(steppers);
            return bar;
        }

        private static void AddStepperBtn(VisualElement parent, string text, string tooltip, Action onClick)
        {
            var btn = new Button(onClick) { text = text, tooltip = tooltip };
            btn.AddToClassList("bp-btn");
            btn.style.height = 20;
            btn.style.paddingLeft = 5;
            btn.style.paddingRight = 5;
            btn.style.fontSize = 10;
            btn.style.marginRight = 3;
            btn.style.marginBottom = 0;
            parent.Add(btn);
        }

        private static string GetPackageVersion()
        {
            try
            {
                var packageJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.wagenheimer.buildpipeline/package.json");
                if (packageJson != null)
                {
                    var data = JsonUtility.FromJson<PackageJsonMinimal>(packageJson.text);
                    if (data != null && !string.IsNullOrEmpty(data.version))
                        return data.version;
                }
            }
            catch { }
            return "1.12.0";
        }

        [Serializable]
        private class PackageJsonMinimal
        {
            public string version;
        }
    }
}
