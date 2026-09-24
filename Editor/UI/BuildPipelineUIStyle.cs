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
            })
            { text = "↺ Discard Changes" };
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
            })
            { text = "💾 Save to Disk" };
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
    }
}

