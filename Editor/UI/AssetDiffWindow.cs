using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Shows what "Changes in memory — NOT written to disk" actually means: a real line-level diff between
    /// the asset's file on disk and what would be written if you hit Save now, so the warning is inspectable
    /// instead of just a name list.
    /// </summary>
    public class AssetDiffWindow : EditorWindow
    {
        public static void Show(UnityEngine.Object asset)
        {
            if (asset == null) return;

            var win = GetWindow<AssetDiffWindow>(true, $"Changes — {asset.name}");
            win.minSize = new Vector2(640, 420);
            win._asset = asset;
            win.Rebuild();
        }

        private UnityEngine.Object _asset;

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            BuildPipelineUIStyle.Apply(rootVisualElement);
            Rebuild();
        }

        private void Rebuild()
        {
            rootVisualElement.Clear();
            if (_asset == null)
            {
                rootVisualElement.Add(new Label("No asset."));
                return;
            }

            var path = AssetDatabase.GetAssetPath(_asset);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                rootVisualElement.Add(BuildPipelineUIStyle.CreateCallout("Asset has no file on disk yet (never saved).", "info"));
                return;
            }

            List<DiffLine> diff;
            try
            {
                diff = ComputeDiff(_asset, path);
            }
            catch (Exception ex)
            {
                rootVisualElement.Add(BuildPipelineUIStyle.CreateCallout($"Could not compute diff: {ex.Message}", "warning"));
                return;
            }

            var changed = diff.Count(d => d.Kind != DiffKind.Unchanged);
            var header = new Label(changed == 0
                ? "No field-level differences found (the asset may be marked dirty from a no-op edit)."
                : $"{changed} changed line(s) between disk and the current in-memory state. Not yet saved.");
            header.style.marginBottom = 6;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            rootVisualElement.Add(header);

            var scroll = new ScrollView { style = { flexGrow = 1 } };
            foreach (var line in diff.Where(d => d.Kind != DiffKind.Unchanged))
            {
                var lbl = new Label((line.Kind == DiffKind.Added ? "+ " : "- ") + line.Text)
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Normal,
                        color = line.Kind == DiffKind.Added ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.9f, 0.5f, 0.5f),
                        whiteSpace = WhiteSpace.Normal,
                        fontSize = 11
                    }
                };
                scroll.Add(lbl);
            }
            rootVisualElement.Add(scroll);

            var actionsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            var saveBtn = new Button(() =>
            {
                EditorUtility.SetDirty(_asset);
                AssetDatabase.SaveAssetIfDirty(_asset);
                Close();
            });
            BuildPipelineUIStyle.ApplyIconText(saveBtn, "💾 Save to Disk");
            saveBtn.AddToClassList("bp-btn");
            saveBtn.AddToClassList("bp-btn--primary");
            actionsRow.Add(saveBtn);

            var closeBtn = new Button(Close) { text = "Close" };
            closeBtn.AddToClassList("bp-btn");
            closeBtn.style.marginLeft = 6;
            actionsRow.Add(closeBtn);

            rootVisualElement.Add(actionsRow);
        }

        private enum DiffKind { Unchanged, Added, Removed }
        private struct DiffLine { public DiffKind Kind; public string Text; }

        /// <summary>
        /// Writes the asset's CURRENT in-memory field values to a throwaway scratch copy via Unity's own
        /// asset serializer (so this is a byte-real diff, not an approximation), reads both YAML texts, diffs
        /// them line-by-line with a small LCS, then deletes the scratch copy.
        /// </summary>
        private static List<DiffLine> ComputeDiff(UnityEngine.Object asset, string assetPath)
        {
            var diskText = File.ReadAllText(assetPath);

            const string scratchPath = "Assets/_BuildPipeline_DiffScratch_DELETE_ME.asset";
            var scratch = UnityEngine.Object.Instantiate(asset);
            string currentText;
            try
            {
                AssetDatabase.DeleteAsset(scratchPath); // in case a previous run left one behind
                AssetDatabase.CreateAsset(scratch, scratchPath);
                // SaveAssetIfDirty(scratch) only — NOT AssetDatabase.SaveAssets(), which would flush every
                // OTHER dirty asset in the project too, silently saving the very changes this window exists
                // to let you preview before committing to disk.
                AssetDatabase.SaveAssetIfDirty(scratch);
                currentText = File.ReadAllText(scratchPath);
            }
            finally
            {
                AssetDatabase.DeleteAsset(scratchPath);
            }

            return DiffLines(
                diskText.Split('\n').Select(l => l.TrimEnd('\r')).ToArray(),
                currentText.Split('\n').Select(l => l.TrimEnd('\r')).ToArray());
        }

        /// <summary>Minimal LCS-based line diff — good enough for a config-sized YAML file, not meant for huge files.</summary>
        private static List<DiffLine> DiffLines(string[] a, string[] b)
        {
            int n = a.Length, m = b.Length;
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var result = new List<DiffLine>();
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[x] == b[y]) { result.Add(new DiffLine { Kind = DiffKind.Unchanged, Text = a[x] }); x++; y++; }
                else if (lcs[x + 1, y] >= lcs[x, y + 1]) { result.Add(new DiffLine { Kind = DiffKind.Removed, Text = a[x] }); x++; }
                else { result.Add(new DiffLine { Kind = DiffKind.Added, Text = b[y] }); y++; }
            }
            while (x < n) { result.Add(new DiffLine { Kind = DiffKind.Removed, Text = a[x] }); x++; }
            while (y < m) { result.Add(new DiffLine { Kind = DiffKind.Added, Text = b[y] }); y++; }
            return result;
        }
    }
}
