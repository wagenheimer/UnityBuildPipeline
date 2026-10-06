using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Dedicated "build + upload to Steam" dashboard: generates the standalone builds, generates the
    /// app/depot .vdf scripts SteamCmd needs from <see cref="SteamUploadConfig"/>, runs SteamCmd with a
    /// live log, and keeps a history of past uploads — all driven from config that lives on this
    /// project's own <see cref="ProjectBuildConfig"/> asset, not the shared ContentBuilder folder.
    /// </summary>
    public class SteamUploadWindow : EditorWindow
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Steam Upload...", priority = 210)]
        public static void Open()
        {
            var win = GetWindow<SteamUploadWindow>("Steam Upload");
            win.minSize = new Vector2(560, 520);
        }

        private ProjectBuildConfig _config;
        private string _lastImportReport;
        private ScrollView _logView;
        private VisualElement _root;
        private Label _statusLabel;

        private void OnEnable()
        {
            _config = FindConfig();
            SteamUploadRunner.OnLogLine += AppendLog;
        }

        private void OnDisable() => SteamUploadRunner.OnLogLine -= AppendLog;

        private static ProjectBuildConfig FindConfig()
        {
            var guids = AssetDatabase.FindAssets("t:ProjectBuildConfig");
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<ProjectBuildConfig>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            BuildPipelineUIStyle.Apply(rootVisualElement);

            _root = new VisualElement { style = { paddingTop = 10, paddingBottom = 10, paddingLeft = 10, paddingRight = 10 } };
            rootVisualElement.Add(new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } }.Also(sv => sv.Add(_root)));

            Rebuild();
        }

        private void Rebuild()
        {
            _root.Clear();

            if (_config == null)
            {
                _root.Add(BuildPipelineUIStyle.CreateCallout(
                    "No ProjectBuildConfig asset found in this project. Create one first (Assets > Create > Build Pipeline > Project Build Config).",
                    "warning"));
                return;
            }

            var cfg = _config.steamUpload;

            _root.Add(BuildImportCard(cfg));
            _root.Add(BuildConfigCard(cfg));
            _root.Add(BuildDepotsCard(cfg));
            _root.Add(BuildActionsCard(cfg));
            _root.Add(BuildLogCard());
            _root.Add(BuildHistoryCard());
        }

        // ── Import ───────────────────────────────────────────────────────────────

        private VisualElement BuildImportCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("📥 Import From Existing SteamCmd Setup",
                "Already shipping via a run_build_<game>.bat + .vdf trio in the shared ContentBuilder folder? Import it instead of retyping App ID / Depot IDs.");

            var importBtn = new Button(() =>
            {
                var picked = EditorUtility.OpenFilePanel("Select this game's run_build_<name>.bat", @"E:\Games\steamworks_sdk_141\sdk\tools\ContentBuilder", "bat");
                if (string.IsNullOrEmpty(picked)) return;

                var error = SteamUploadRunner.ImportLegacyConfig(cfg, picked, out var report);
                _lastImportReport = (error == null ? "IMPORT OK\n\n" : "IMPORT FALHOU\n\n") + report;
                Debug.Log($"[BuildPipeline] Steam import report:\n{report}");
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssetIfDirty(_config);
                Rebuild();

                EditorUtility.DisplayDialog(
                    error == null ? "Import concluído" : "Import falhou",
                    (error == null
                        ? $"App ID {cfg.appId}, {new[] { cfg.HasWindowsDepot, cfg.HasMacDepot, cfg.HasLinuxDepot }.Count(b => b)} depot(s) importado(s). " +
                          "Os ContentRoots apontam para as pastas ANTIGAS por enquanto — use 'Build Now' para apontar para um build novo do Unity.\n\n"
                        : error + "\n\n") + report,
                    "OK");
            });
            BuildPipelineUIStyle.ApplyIconText(importBtn, "📥 Import from .bat / .vdf...");
            importBtn.AddToClassList("bp-btn");
            importBtn.AddToClassList("bp-btn--primary");
            card.Add(importBtn);

            if (!string.IsNullOrEmpty(_lastImportReport))
            {
                var reportLabel = new Label(_lastImportReport)
                {
                    selection = { isSelectable = true },
                    style = { fontSize = 10, whiteSpace = WhiteSpace.Normal, marginTop = 8, color = new Color(0.75f, 0.8f, 0.85f) }
                };
                card.Add(reportLabel);
            }
            return card;
        }

        // ── Config ───────────────────────────────────────────────────────────────

        private VisualElement BuildConfigCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("⚙️ Steam Configuration");

            card.Add(TextRow("App ID", cfg.appId, v => cfg.appId = v));
            card.Add(TextRow("SteamCmd Path", cfg.steamCmdPath, v => cfg.steamCmdPath = v, browseFile: true));
            card.Add(TextRow("Steam Username", cfg.steamUsername, v => cfg.steamUsername = v));
            card.Add(TextRow("Password Env Var", cfg.steamPasswordEnvVar, v => cfg.steamPasswordEnvVar = v));

            var pwd = cfg.GetEffectivePassword();
            card.Add(BuildPipelineUIStyle.CreateCallout(
                string.IsNullOrEmpty(pwd)
                    ? $"⚠ Env var '{cfg.steamPasswordEnvVar}' isn't set on this machine. Upload will try steamcmd's cached login for '{cfg.steamUsername}' instead (works after one manual interactive login + Steam Guard code)."
                    : $"✔ Password will be read from '{cfg.steamPasswordEnvVar}'. Never store the real password in this asset or in git.",
                string.IsNullOrEmpty(pwd) ? "warning" : "success"));

            card.Add(TextRow("Set Live Branch (optional)", cfg.setLiveBranch, v => cfg.setLiveBranch = v));
            return card;
        }

        // ── Depots ───────────────────────────────────────────────────────────────

        private VisualElement BuildDepotsCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("📦 Depots (Windows / Mac / Linux)",
                "Leave a Depot ID empty to skip that platform entirely.");

            card.Add(DepotRow("Windows", PlatformType.Windows64, cfg.depotIdWindows, v => cfg.depotIdWindows = v, cfg.lastBuiltPathWindows));
            card.Add(DepotRow("macOS", PlatformType.macOS, cfg.depotIdMac, v => cfg.depotIdMac = v, cfg.lastBuiltPathMac));
            card.Add(DepotRow("Linux", PlatformType.Linux64, cfg.depotIdLinux, v => cfg.depotIdLinux = v, cfg.lastBuiltPathLinux));
            return card;
        }

        private VisualElement DepotRow(string label, PlatformType platform, string depotId, Action<string> setDepotId, string lastBuiltPath)
        {
            var row = new VisualElement { style = { marginBottom = 8, paddingBottom = 6, borderBottomWidth = 1, borderBottomColor = new Color(1, 1, 1, 0.08f) } };
            row.Add(TextRow($"{label} Depot ID", depotId, v => { setDepotId(v); SaveDirty(); }));

            var statusRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
            var exists = !string.IsNullOrEmpty(lastBuiltPath) && Directory.Exists(lastBuiltPath);
            var statusLabel = new Label(string.IsNullOrEmpty(lastBuiltPath)
                ? "Not built yet."
                : (exists ? $"✔ Last build: {lastBuiltPath}" : $"⚠ Last build folder is missing: {lastBuiltPath}"));
            statusLabel.style.fontSize = 10;
            statusLabel.style.color = exists ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.9f, 0.7f, 0.4f);
            statusLabel.style.flexGrow = 1;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusRow.Add(statusLabel);

            var buildBtn = new Button(() => BuildOnePlatform(platform));
            BuildPipelineUIStyle.ApplyIconText(buildBtn, "🔨 Build Now");
            buildBtn.AddToClassList("bp-btn");
            statusRow.Add(buildBtn);

            row.Add(statusRow);
            return row;
        }

        private void BuildOnePlatform(PlatformType platform)
        {
            var steamProfile = _config.publishers?.FirstOrDefault(p => p.IsSteamProfile);
            if (steamProfile == null)
            {
                EditorUtility.DisplayDialog("No Steam Profile", "Add a Steam publisher profile first (Project Build Config > Publishers & Store Profiles).", "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("Building for Steam", $"Building {platform}...", 0.3f);
            try
            {
                var result = SteamUploadRunner.BuildPlatform(_config, steamProfile, platform);
                EditorUtility.ClearProgressBar();
                Rebuild();
                if (!result.Success)
                    EditorUtility.DisplayDialog("Build Failed", result.ErrorMessage ?? "Build failed — check the Console.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        private VisualElement BuildActionsCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("🚀 Upload");

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };

            var buildAllBtn = new Button(() =>
            {
                if (cfg.HasWindowsDepot) BuildOnePlatform(PlatformType.Windows64);
                if (cfg.HasMacDepot) BuildOnePlatform(PlatformType.macOS);
                if (cfg.HasLinuxDepot) BuildOnePlatform(PlatformType.Linux64);
            });
            BuildPipelineUIStyle.ApplyIconText(buildAllBtn, "🔨 Build All Configured Platforms");
            buildAllBtn.AddToClassList("bp-btn");
            buildAllBtn.style.marginRight = 6;
            row.Add(buildAllBtn);

            var canUpload = cfg.IsConfigured && !SteamUploadRunner.IsUploading &&
                             (!string.IsNullOrEmpty(cfg.lastBuiltPathWindows) || !string.IsNullOrEmpty(cfg.lastBuiltPathMac) || !string.IsNullOrEmpty(cfg.lastBuiltPathLinux));

            var uploadBtn = new Button(() =>
            {
                _logView.Clear();
                AppendLog($"=== Starting upload for App {cfg.appId} ===");
                SteamUploadRunner.Upload(_config, (success, summary) =>
                {
                    AppendLog(success ? $"✔ {summary}" : $"✕ {summary}");
                    Rebuild();
                });
                Rebuild();
            });
            BuildPipelineUIStyle.ApplyIconText(uploadBtn, SteamUploadRunner.IsUploading ? "⏳ Uploading..." : "🚀 Upload to Steam");
            uploadBtn.SetEnabled(canUpload);
            uploadBtn.AddToClassList("bp-btn");
            uploadBtn.AddToClassList("bp-btn--primary");
            if (!canUpload)
                uploadBtn.tooltip = "Set an App ID + at least one Depot ID, and build at least one platform first.";
            row.Add(uploadBtn);

            card.Add(row);
            return card;
        }

        // ── Log ──────────────────────────────────────────────────────────────────

        private VisualElement BuildLogCard()
        {
            var card = BuildPipelineUIStyle.CreateCard("📜 SteamCmd Log");
            _logView = new ScrollView { style = { height = 160, backgroundColor = new Color(0.05f, 0.05f, 0.06f) } };
            card.Add(_logView);
            return card;
        }

        private void AppendLog(string line)
        {
            if (_logView == null) return;
            var label = new Label(line) { style = { fontSize = 10, color = new Color(0.8f, 0.85f, 0.9f), whiteSpace = WhiteSpace.Normal } };
            _logView.Add(label);
            _logView.schedule.Execute(() => _logView.scrollOffset = new Vector2(0, float.MaxValue));
        }

        // ── History ──────────────────────────────────────────────────────────────

        private VisualElement BuildHistoryCard()
        {
            var card = BuildPipelineUIStyle.CreateCard("🕒 Upload History", "Saved with this project (ProjectSettings/SteamUploadHistory.json).");
            var entries = SteamUploadHistory.Load();

            if (entries.Count == 0)
            {
                card.Add(new Label("No uploads recorded yet.") { style = { fontSize = 11, color = new Color(0.6f, 0.6f, 0.65f) } });
                return card;
            }

            foreach (var e in entries.Take(15))
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingTop = 3, paddingBottom = 3, borderBottomWidth = 1, borderBottomColor = new Color(1, 1, 1, 0.06f) } };
                var icon = e.success ? "✔" : "✕";
                var color = e.success ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.9f, 0.4f, 0.4f);
                row.Add(new Label($"{icon} {e.Timestamp:yyyy-MM-dd HH:mm}") { style = { width = 150, color = color, fontSize = 11 } });
                row.Add(new Label($"App {e.appId} [{e.PlatformsLabel()}]" + (string.IsNullOrEmpty(e.branch) ? "" : $" → {e.branch}")) { style = { flexGrow = 1, fontSize = 11 } });
                card.Add(row);
            }
            return card;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void SaveDirty()
        {
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssetIfDirty(_config);
        }

        private VisualElement TextRow(string label, string value, Action<string> onChanged, bool browseFile = false)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            var field = new TextField(label) { value = value, style = { flexGrow = 1 } };
            field.RegisterValueChangedCallback(evt =>
            {
                onChanged(evt.newValue);
                SaveDirty();
            });
            row.Add(field);

            if (browseFile)
            {
                var browseBtn = new Button(() =>
                {
                    var picked = EditorUtility.OpenFilePanel("Locate steamcmd.exe", Path.GetDirectoryName(value) ?? "", "exe");
                    if (string.IsNullOrEmpty(picked)) return;
                    field.value = picked; // triggers the change callback above
                })
                { text = "..." };
                browseBtn.AddToClassList("bp-btn");
                browseBtn.style.marginLeft = 4;
                row.Add(browseBtn);
            }
            return row;
        }
    }

    internal static class VisualElementExtensions
    {
        public static T Also<T>(this T element, Action<T> action) where T : VisualElement
        {
            action(element);
            return element;
        }
    }
}
