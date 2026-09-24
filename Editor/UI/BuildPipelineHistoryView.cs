using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    internal sealed class BuildPipelineHistoryView
    {
        public VisualElement Root { get; }

        private readonly Action _onRefresh;

        public BuildPipelineHistoryView(Action onRefresh)
        {
            _onRefresh = onRefresh;

            Root = new VisualElement();
            BuildPipelineUIStyle.Apply(Root);

            BuildUI();
        }

        private void BuildUI()
        {
            Root.Clear();

            var headerCard = BuildPipelineUIStyle.CreateCard("Recent Builds History",
                "Last 30 builds recorded on this machine. Open output directories or install APK/AAB packages directly to connected devices via ADB / bundletool.");

            var toolchainRow = new VisualElement();
            toolchainRow.style.flexDirection = FlexDirection.Row;
            toolchainRow.style.alignItems = Align.Center;
            toolchainRow.style.justifyContent = Justify.SpaceBetween;

            var btPath = BuildHistory.GetBundletoolPath();
            var btLabel = new Label($"bundletool.jar: {(string.IsNullOrEmpty(btPath) ? "NOT FOUND (will offer download)" : btPath)}");
            btLabel.style.fontSize = 11;
            btLabel.style.color = string.IsNullOrEmpty(btPath) ? new Color(0.95f, 0.65f, 0.2f) : new Color(0.4f, 0.85f, 0.5f);
            toolchainRow.Add(btLabel);

            var clearBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Clear Build History", "Remove all entries from the local build history?", "CLEAR", "CANCEL"))
                {
                    BuildHistory.Clear();
                    BuildUI();
                }
            })
            { text = "🗑 Clear History" };
            clearBtn.AddToClassList("bp-btn");
            clearBtn.AddToClassList("bp-btn--danger");
            clearBtn.style.height = 22;
            clearBtn.style.fontSize = 10;
            toolchainRow.Add(clearBtn);

            headerCard.Add(toolchainRow);
            Root.Add(headerCard);

            var entries = BuildHistory.Entries;
            if (entries == null || entries.Count == 0)
            {
                var emptyCard = BuildPipelineUIStyle.CreateCallout("No builds recorded yet. Run a build from the Quick Build or Matrix tabs to populate this list.", "info");
                Root.Add(emptyCard);
                return;
            }

            var listCard = BuildPipelineUIStyle.CreateCard($"Recorded Builds ({entries.Count})");

            foreach (var entry in entries)
            {
                listCard.Add(CreateEntryRow(entry));
            }

            Root.Add(listCard);
        }

        private VisualElement CreateEntryRow(BuildHistoryEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("bp-table-row");
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;
            topRow.style.justifyContent = Justify.SpaceBetween;

            var statusBadge = BuildPipelineUIStyle.CreateBadge(entry.success ? "SUCCESS" : "FAILED", entry.success ? "ok" : "fail");
            statusBadge.style.marginRight = 8;
            topRow.Add(statusBadge);

            var timeLbl = new Label($"{entry.Time:dd/MM/yyyy HH:mm}");
            timeLbl.style.fontSize = 11;
            timeLbl.style.color = new Color(0.7f, 0.7f, 0.7f);
            timeLbl.style.marginRight = 8;
            topRow.Add(timeLbl);

            var platLbl = new Label(entry.platform);
            platLbl.style.fontSize = 11;
            platLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            platLbl.style.color = Color.white;
            platLbl.style.marginRight = 8;
            topRow.Add(platLbl);

            var pubLbl = new Label(entry.publisher);
            pubLbl.style.fontSize = 11;
            pubLbl.style.color = new Color(0.85f, 0.85f, 0.85f);
            pubLbl.style.marginRight = 8;
            topRow.Add(pubLbl);

            var langLbl = new Label(entry.language);
            langLbl.style.fontSize = 11;
            langLbl.style.color = new Color(0.75f, 0.75f, 0.75f);
            topRow.Add(langLbl);

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            topRow.Add(spacer);

            if (entry.cheatMode)
            {
                var cheatBadge = BuildPipelineUIStyle.CreateBadge("CHEAT", "warn");
                cheatBadge.style.marginRight = 4;
                topRow.Add(cheatBadge);
            }

            if (entry.developmentBuild)
            {
                var devBadge = BuildPipelineUIStyle.CreateBadge("DEV", "info");
                devBadge.style.marginRight = 4;
                topRow.Add(devBadge);
            }

            var durLbl = new Label($"{entry.durationSeconds:F0}s" + (entry.totalSize > 0 ? $"  •  {entry.totalSize / (1024.0 * 1024.0):F1} MB" : ""));
            durLbl.style.fontSize = 11;
            durLbl.style.color = new Color(0.6f, 0.6f, 0.6f);
            topRow.Add(durLbl);

            row.Add(topRow);

            // Path & Action Row
            var bottomRow = new VisualElement();
            bottomRow.style.flexDirection = FlexDirection.Row;
            bottomRow.style.alignItems = Align.Center;
            bottomRow.style.marginTop = 4;

            var pathLbl = new Label(entry.outputPath);
            pathLbl.style.fontSize = 10;
            pathLbl.style.color = new Color(0.55f, 0.6f, 0.65f);
            pathLbl.style.flexGrow = 1;
            pathLbl.style.whiteSpace = WhiteSpace.Normal;
            bottomRow.Add(pathLbl);

            var folderBtn = new Button(() => BuildHistory.OpenFolder(entry)) { text = "📂 Open Folder" };
            folderBtn.AddToClassList("bp-btn");
            folderBtn.style.height = 20;
            folderBtn.style.fontSize = 10;
            folderBtn.style.paddingLeft = 8;
            folderBtn.style.paddingRight = 8;
            bottomRow.Add(folderBtn);

            string runLabel = entry.platform == "Android"
                ? (entry.IsAab ? "▶️ Install & Run (bundletool)" : "▶️ Install & Run (adb)")
                : "▶️ Run";

            var runBtn = new Button(() => BuildHistory.Run(entry)) { text = runLabel };
            runBtn.AddToClassList("bp-btn");
            runBtn.AddToClassList("bp-btn--success");
            runBtn.style.height = 20;
            runBtn.style.fontSize = 10;
            runBtn.style.paddingLeft = 8;
            runBtn.style.paddingRight = 8;
            runBtn.SetEnabled(BuildHistory.CanRun(entry));
            bottomRow.Add(runBtn);

            var delBtn = new Button(() =>
            {
                BuildHistory.Remove(entry);
                BuildUI();
            }) { text = "✕" };
            delBtn.AddToClassList("bp-btn");
            delBtn.style.height = 20;
            delBtn.style.width = 22;
            delBtn.style.fontSize = 10;
            delBtn.style.paddingLeft = 0;
            delBtn.style.paddingRight = 0;
            delBtn.style.marginRight = 0;
            bottomRow.Add(delBtn);

            row.Add(bottomRow);

            return row;
        }
    }
}
