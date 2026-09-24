using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal static class BuildPipelineUIStyle
    {
        public const string PackageStylePath = "Packages/com.wagenheimer.buildpipeline/Editor/UI/BuildPipelineCommon.uss";

        public static void Apply(VisualElement element)
        {
            if (element == null) return;

            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackageStylePath);
            if (sheet == null)
            {
                var guids = AssetDatabase.FindAssets("BuildPipelineCommon t:StyleSheet");
                if (guids != null && guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                }
            }

            if (sheet != null && !element.styleSheets.Contains(sheet))
            {
                element.styleSheets.Add(sheet);
            }
        }

        public static VisualElement CreateCard(string title, string subtitle = null, VisualElement headerRight = null)
        {
            var card = new VisualElement();
            card.AddToClassList("bp-card");

            var header = new VisualElement();
            header.AddToClassList("bp-card-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("bp-card-title");
            header.Add(titleLabel);

            if (headerRight != null)
            {
                header.Add(headerRight);
            }

            card.Add(header);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = new Label(subtitle);
                sub.AddToClassList("bp-card-subtitle");
                card.Add(sub);
            }

            return card;
        }

        public static VisualElement CreateCallout(string text, string severity = "info")
        {
            var box = new VisualElement();
            box.AddToClassList("bp-callout");
            box.AddToClassList("bp-callout--" + severity);

            var label = new Label(text);
            label.AddToClassList("bp-callout-text");
            box.Add(label);

            return box;
        }

        public static Label CreateBadge(string text, string type = "ok")
        {
            var badge = new Label(text);
            badge.AddToClassList("bp-badge");
            badge.AddToClassList("bp-badge--" + type);
            return badge;
        }

        public static void SetBadge(Label badge, string text, string type)
        {
            if (badge == null) return;
            badge.text = text;
            badge.RemoveFromClassList("bp-badge--ok");
            badge.RemoveFromClassList("bp-badge--warn");
            badge.RemoveFromClassList("bp-badge--fail");
            badge.RemoveFromClassList("bp-badge--info");
            badge.AddToClassList("bp-badge--" + type);
        }

        public static VisualElement CreateSaveStatusBar(UnityEngine.Object asset, Action onSave = null)
        {
            var bar = new VisualElement();
            bar.AddToClassList("bp-save-status");

            bool isDirty = asset != null && EditorUtility.IsDirty(asset);
            if (isDirty)
            {
                bar.AddToClassList("bp-save-status--dirty");
            }

            var label = new Label(isDirty
                ? "⚠ Changes in memory — NOT yet written to disk (won't show up in git)."
                : "✔ Everything saved to disk.");
            label.AddToClassList("bp-save-status-text");
            bar.Add(label);

            if (isDirty)
            {
                var saveBtn = new Button(() =>
                {
                    if (asset != null)
                    {
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                    }
                    AssetDatabase.SaveAssets();
                    onSave?.Invoke();
                })
                { text = "💾 Save to Disk" };
                saveBtn.AddToClassList("bp-btn");
                saveBtn.AddToClassList("bp-btn--primary");
                saveBtn.style.height = 22;
                saveBtn.style.marginRight = 0;
                saveBtn.style.marginBottom = 0;
                bar.Add(saveBtn);
            }

            return bar;
        }
    }
}
