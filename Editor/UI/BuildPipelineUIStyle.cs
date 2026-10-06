using System;
using System.Collections.Generic;
using System.Linq;
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

            var titleLabel = CreateIconLabel(title);
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
            return CreateSaveStatusBar(asset != null ? new[] { asset } : Array.Empty<UnityEngine.Object>(), onSave);
        }

        public static VisualElement CreateSaveStatusBar(IEnumerable<UnityEngine.Object> assets, Action onSave = null)
        {
            var assetList = assets != null ? assets.Where(a => a != null).Distinct().ToList() : new List<UnityEngine.Object>();
            var bar = new VisualElement();
            bar.AddToClassList("bp-save-status");

            var label = new Label();
            label.AddToClassList("bp-save-status-text");
            label.style.flexGrow = 1;
            bar.Add(label);

            var btnContainer = new VisualElement();
            btnContainer.style.flexDirection = FlexDirection.Row;
            btnContainer.style.alignItems = Align.Center;

            var viewBtn = new Button(() =>
            {
                foreach (var a in assetList.Where(a => a != null && EditorUtility.IsDirty(a)))
                    AssetDiffWindow.Show(a);
            });
            BuildPipelineUIStyle.ApplyIconText(viewBtn, "👁 View Changes");
            viewBtn.AddToClassList("bp-btn");
            viewBtn.style.height = 22;
            viewBtn.style.marginRight = 6;
            viewBtn.style.marginBottom = 0;
            btnContainer.Add(viewBtn);

            var discardBtn = new Button(() =>
            {
                var dirtyList = assetList.Where(a => a != null && EditorUtility.IsDirty(a)).ToList();
                if (dirtyList.Count == 0) return;

                string listText = string.Join("\n", dirtyList.Select(x => $"• {x.name} ({x.GetType().Name})"));
                if (EditorUtility.DisplayDialog(
                    "Discard Changes",
                    $"Discard all unsaved in-memory changes for:\n\n{listText}\n\nAny unsaved edits will be lost and reverted from disk.",
                    "Discard Changes",
                    "Cancel"))
                {
                    foreach (var a in dirtyList)
                    {
                        if (a != null)
                        {
                            EditorUtility.ClearDirty(a);
                            string path = AssetDatabase.GetAssetPath(a);
                            if (!string.IsNullOrEmpty(path))
                            {
                                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                            }
                        }
                    }
                    AssetDatabase.Refresh();
                    Debug.Log("[BuildPipeline] Unsaved in-memory changes discarded and reloaded from disk.");
                    onSave?.Invoke();
                }
            });
            BuildPipelineUIStyle.ApplyIconText(discardBtn, "↺ Discard Changes");
            discardBtn.AddToClassList("bp-btn");
            discardBtn.style.height = 22;
            discardBtn.style.marginRight = 6;
            discardBtn.style.marginBottom = 0;
            btnContainer.Add(discardBtn);

            var saveBtn = new Button(() =>
            {
                var dirtyList = assetList.Where(a => a != null && EditorUtility.IsDirty(a)).ToList();
                foreach (var a in dirtyList)
                {
                    if (a != null)
                    {
                        EditorUtility.SetDirty(a);
                        AssetDatabase.SaveAssetIfDirty(a);
                    }
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[BuildPipeline] Changes saved to disk successfully.");
                onSave?.Invoke();
            });
            BuildPipelineUIStyle.ApplyIconText(saveBtn, "💾 Save to Disk");
            saveBtn.AddToClassList("bp-btn");
            saveBtn.AddToClassList("bp-btn--primary");
            saveBtn.style.height = 22;
            saveBtn.style.marginRight = 0;
            saveBtn.style.marginBottom = 0;
            btnContainer.Add(saveBtn);

            bar.Add(btnContainer);

            void RefreshState()
            {
                var dirtyList = assetList.Where(a => a != null && EditorUtility.IsDirty(a)).ToList();
                bool isDirty = dirtyList.Count > 0;

                if (isDirty)
                {
                    if (!bar.ClassListContains("bp-save-status--dirty"))
                        bar.AddToClassList("bp-save-status--dirty");

                    string dirtyNames = string.Join(", ", dirtyList.Select(x => x.name));
                    label.text = $"⚠ Changes in memory ({dirtyNames}) — NOT written to disk.";
                    btnContainer.style.display = DisplayStyle.Flex;
                }
                else
                {
                    if (bar.ClassListContains("bp-save-status--dirty"))
                        bar.RemoveFromClassList("bp-save-status--dirty");

                    label.text = "✔ Everything saved to disk.";
                    btnContainer.style.display = DisplayStyle.None;
                }
            }

            RefreshState();
            bar.RegisterCallback<AttachToPanelEvent>(e => RefreshState());
            bar.schedule.Execute(RefreshState).Every(400);

            return bar;
        }

        /// <summary>
        /// Renders <paramref name="text"/> on the button, splitting a leading icon (emoji/symbol) into its own
        /// element with a reserved width. Inline, a fallback emoji glyph draws wider than it measures on Windows,
        /// so the following text runs over it; a separate, min-width element keeps them apart.
        /// </summary>
        public static void ApplyIconText(Button button, string text)
        {
            // Idempotent: drop icon/text children from a previous call so live label updates can re-apply cleanly.
            for (int i = button.childCount - 1; i >= 0; i--)
            {
                var child = button[i];
                if (child.ClassListContains("wui-btn-icon") || child.ClassListContains("wui-btn-text"))
                    child.RemoveFromHierarchy();
            }

            SplitLeadingIcon(text, out var icon, out var label);

            if (string.IsNullOrEmpty(icon))
            {
                button.text = text;
                return;
            }

            button.text = string.Empty;
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;

            var iconElement = new Label(icon);
            iconElement.AddToClassList("wui-btn-icon");
            iconElement.style.minWidth = 14;
            iconElement.style.marginRight = string.IsNullOrEmpty(label) ? 0 : 6;
            iconElement.style.flexShrink = 0;
            iconElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconElement.pickingMode = PickingMode.Ignore;
            button.Add(iconElement);

            if (!string.IsNullOrEmpty(label))
            {
                var textLabel = new Label(label);
                textLabel.AddToClassList("wui-btn-text");
                textLabel.style.flexShrink = 0;
                textLabel.pickingMode = PickingMode.Ignore;
                button.Add(textLabel);
            }
        }

        /// <summary>
        /// A label whose leading icon is a separate element (same overlap fix as buttons).
        /// </summary>
        public static VisualElement CreateIconLabel(string text)
        {
            SplitLeadingIcon(text, out var icon, out var rest);
            if (string.IsNullOrEmpty(icon))
                return new Label(text);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var iconElement = new Label(icon);
            iconElement.AddToClassList("wui-btn-icon");
            iconElement.style.minWidth = 14;
            iconElement.style.marginRight = string.IsNullOrEmpty(rest) ? 0 : 6;
            iconElement.style.flexShrink = 0;
            iconElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconElement.pickingMode = PickingMode.Ignore;
            row.Add(iconElement);

            var label = new Label(rest);
            label.pickingMode = PickingMode.Ignore;
            row.Add(label);
            return row;
        }

        internal static void SplitLeadingIcon(string text, out string icon, out string label)
        {
            icon = null;
            label = text;
            if (string.IsNullOrEmpty(text)) return;

            int i = 0;
            while (i < text.Length)
            {
                int codePoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length
                    ? char.ConvertToUtf32(text[i], text[i + 1])
                    : text[i];

                if (!IsIconCodePoint(codePoint)) break;
                i += char.IsHighSurrogate(text[i]) ? 2 : 1;
            }

            if (i == 0) return;

            icon = text.Substring(0, i).TrimEnd();
            label = text.Substring(i).TrimStart();
        }

        private static bool IsIconCodePoint(int codePoint) =>
            (codePoint >= 0x2190 && codePoint <= 0x2BFF)
            || (codePoint >= 0x1F000 && codePoint <= 0x1FAFF)
            || codePoint == 0xFE0F
            || codePoint == 0x20E3;
    }
}

