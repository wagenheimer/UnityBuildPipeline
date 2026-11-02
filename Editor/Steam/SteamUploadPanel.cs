using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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
    internal sealed class SteamUploadPanel
    {
        private readonly ProjectBuildConfig _config;
        private readonly Action _onConfigChanged;
        private string _lastImportReport;
        private ScrollView _logView;
        private readonly VisualElement _root = new VisualElement();
        private PublisherProfile _steamProfile;

        public VisualElement Root => _root;

        public SteamUploadPanel(ProjectBuildConfig config, Action onConfigChanged = null)
        {
            _config = config;
            _onConfigChanged = onConfigChanged;
            BuildPipelineUIStyle.Apply(_root);
            _root.RegisterCallback<AttachToPanelEvent>(_ => SteamUploadRunner.OnLogLine += AppendLog);
            _root.RegisterCallback<DetachFromPanelEvent>(_ => SteamUploadRunner.OnLogLine -= AppendLog);
            _steamProfile = _config?.publishers?.FirstOrDefault(p => p != null && p.isSteam);
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

            _root.Add(BuildMasterToggleCard(cfg));

            if (!cfg.steamEnabled)
            {
                _root.Add(BuildPipelineUIStyle.CreateCallout(
                    "A integração e diagnósticos da Steam estão DESATIVADOS para este projeto. " +
                    "Marque 'Steam Ativado' acima caso este jogo seja publicado na Steam para verificar Steamworks.NET, SteamManager, steam_appid.txt e automação de builds/upload.",
                    "info"));
                return;
            }

            _root.Add(BuildDiagnosticsAndChecklistCard(cfg));
            _root.Add(BuildProfilesCard());
            _root.Add(BuildImportCard(cfg));
            _root.Add(BuildConfigCard(cfg));
            _root.Add(BuildDepotsCard(cfg));
            _root.Add(BuildActionsCard(cfg));
            _root.Add(BuildLogCard());
            _root.Add(BuildHistoryCard());
        }

        // ── Master Toggle Card ───────────────────────────────────────────────────

        private VisualElement BuildMasterToggleCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("🎮 Integração Steam",
                "Ativa ou desativa o ecossistema Steam para este projeto. Quando desativado, o painel silencia alertas e erros de SDKs ausentes.");

            var toggleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            var toggle = new Toggle("Steam Ativado (S/N)")
            {
                value = cfg.steamEnabled,
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                cfg.steamEnabled = evt.newValue;
                SaveDirty();
                Rebuild();
            });
            toggleRow.Add(toggle);
            card.Add(toggleRow);

            if (cfg.steamEnabled && !string.IsNullOrEmpty(cfg.appId))
            {
                var linksRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };
                void AddLinkBtn(string text, string url)
                {
                    var btn = new Button(() => Application.OpenURL(url));
                    BuildPipelineUIStyle.ApplyIconText(btn, text);
                    btn.AddToClassList("bp-btn");
                    btn.style.marginRight = 6;
                    linksRow.Add(btn);
                }

                AddLinkBtn("🌐 Painel do Jogo", $"https://partner.steamgames.com/apps/landing/{cfg.appId}");
                AddLinkBtn("🚀 Builds (SteamPipe)", $"https://partner.steamgames.com/apps/builds/{cfg.appId}");
                AddLinkBtn("🏆 Conquistas & Stats", $"https://partner.steamgames.com/apps/achievements/{cfg.appId}");
                card.Add(linksRow);
            }

            return card;
        }

        // ── Diagnostics & Checklist ──────────────────────────────────────────────

        private VisualElement BuildDiagnosticsAndChecklistCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("🩺 Diagnóstico & Checklist do Steam",
                "Verificação dos requisitos essenciais para compilar, executar e testar o jogo com a Steam no editor e standalone.");

            // 1. Steamworks.NET
            bool steamworksInstalled = DefineEnvironment.IsSteamworksPresent();
            var swRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            var swLabel = new Label(steamworksInstalled
                ? "✔ Steamworks.NET instalado no projeto."
                : "⚠ Steamworks.NET não encontrado. (Necessário para a API Steam, conquistas, overlay e nuvem)")
            {
                style = { flexGrow = 1, color = steamworksInstalled ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.95f, 0.75f, 0.35f), fontSize = 11, whiteSpace = WhiteSpace.Normal }
            };
            swRow.Add(swLabel);
            if (!steamworksInstalled)
            {
                var installBtn = new Button(() =>
                {
                    AddSteamworksPackage();
                    Rebuild();
                });
                BuildPipelineUIStyle.ApplyIconText(installBtn, "📦 Adicionar Steamworks.NET (UPM)");
                installBtn.AddToClassList("bp-btn");
                installBtn.AddToClassList("bp-btn--primary");
                swRow.Add(installBtn);
            }
            card.Add(swRow);

            // 2. steam_appid.txt
            string appIdPath = GetSteamAppIdFilePath();
            bool appIdFileExists = File.Exists(appIdPath);
            string fileContent = appIdFileExists ? File.ReadAllText(appIdPath).Trim() : "";
            bool idSynced = appIdFileExists && !string.IsNullOrEmpty(cfg.appId) && fileContent == cfg.appId.Trim();

            var appIdRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            string appIdMsg = string.IsNullOrWhiteSpace(cfg.appId)
                ? "⚠ App ID da Steam ainda não foi configurado na seção abaixo."
                : !appIdFileExists
                    ? $"⚠ steam_appid.txt ausente na raiz do projeto ({Path.GetFileName(Path.GetDirectoryName(Application.dataPath))}/steam_appid.txt)."
                    : idSynced
                        ? $"✔ steam_appid.txt presente e sincronizado (App ID: {cfg.appId})."
                        : $"⚠ steam_appid.txt desatualizado (arquivo: '{fileContent}' vs config: '{cfg.appId}').";

            var appIdLabel = new Label(appIdMsg)
            {
                style = { flexGrow = 1, color = idSynced ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.95f, 0.75f, 0.35f), fontSize = 11, whiteSpace = WhiteSpace.Normal }
            };
            appIdRow.Add(appIdLabel);

            if (!string.IsNullOrWhiteSpace(cfg.appId) && !idSynced)
            {
                var syncBtn = new Button(() =>
                {
                    try
                    {
                        File.WriteAllText(appIdPath, cfg.appId.Trim());
                        AssetDatabase.Refresh();
                        Rebuild();
                        EditorUtility.DisplayDialog("steam_appid.txt", $"Arquivo gravado com App ID {cfg.appId} na raiz do projeto.", "OK");
                    }
                    catch (Exception ex)
                    {
                        EditorUtility.DisplayDialog("Erro", ex.Message, "OK");
                    }
                });
                BuildPipelineUIStyle.ApplyIconText(syncBtn, appIdFileExists ? "⚡ Sincronizar steam_appid.txt" : "⚡ Criar steam_appid.txt");
                syncBtn.AddToClassList("bp-btn");
                syncBtn.AddToClassList("bp-btn--primary");
                appIdRow.Add(syncBtn);
            }
            card.Add(appIdRow);

            // 3. SteamManager.cs
            string managerPath = FindSteamManagerScriptPath();
            bool hasManagerScript = !string.IsNullOrEmpty(managerPath);
            var mgrRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            var mgrLabel = new Label(hasManagerScript
                ? $"✔ SteamManager.cs encontrado em '{managerPath}'."
                : "⚠ SteamManager.cs não encontrado em Assets/. (Script essencial para SteamAPI.Init no runtime)")
            {
                style = { flexGrow = 1, color = hasManagerScript ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.95f, 0.75f, 0.35f), fontSize = 11, whiteSpace = WhiteSpace.Normal }
            };
            mgrRow.Add(mgrLabel);
            if (!hasManagerScript)
            {
                var createMgrBtn = new Button(() =>
                {
                    CreateSteamManagerScript();
                    Rebuild();
                });
                BuildPipelineUIStyle.ApplyIconText(createMgrBtn, "📄 Criar SteamManager.cs");
                createMgrBtn.AddToClassList("bp-btn");
                createMgrBtn.AddToClassList("bp-btn--primary");
                mgrRow.Add(createMgrBtn);
            }
            card.Add(mgrRow);

            // 4. Injeção na cena inicial
            var firstScene = EditorBuildSettings.scenes?.FirstOrDefault(s => s.enabled);
            var sceneRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            if (firstScene == null)
            {
                sceneRow.Add(new Label("⚠ Nenhuma cena habilitada em File > Build Settings.")
                {
                    style = { flexGrow = 1, color = new Color(0.95f, 0.75f, 0.35f), fontSize = 11 }
                });
            }
            else
            {
                bool sceneHasManager = false;
                string sceneFullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, firstScene.path);
                if (File.Exists(sceneFullPath))
                {
                    string sceneText = File.ReadAllText(sceneFullPath);
                    if (sceneText.Contains("SteamManager"))
                        sceneHasManager = true;
                }
                bool autoInit = hasManagerScript && File.Exists(managerPath) && File.ReadAllText(managerPath).Contains("RuntimeInitializeOnLoadMethod");

                string sceneMsg = sceneHasManager
                    ? $"✔ SteamManager presente na cena inicial ({Path.GetFileName(firstScene.path)})."
                    : autoInit
                        ? $"✔ SteamManager possui auto-inicialização em runtime ([RuntimeInitializeOnLoadMethod])."
                        : $"⚠ SteamManager não detectado na primeira cena ({Path.GetFileName(firstScene.path)}).";

                bool ok = sceneHasManager || autoInit;
                var sceneLabel = new Label(sceneMsg)
                {
                    style = { flexGrow = 1, color = ok ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.95f, 0.75f, 0.35f), fontSize = 11, whiteSpace = WhiteSpace.Normal }
                };
                sceneRow.Add(sceneLabel);

                if (!ok)
                {
                    var injectBtn = new Button(() =>
                    {
                        InjectSteamManagerIntoFirstScene(firstScene.path);
                        Rebuild();
                    });
                    BuildPipelineUIStyle.ApplyIconText(injectBtn, "⚡ Injetar na Cena Inicial");
                    injectBtn.AddToClassList("bp-btn");
                    injectBtn.AddToClassList("bp-btn--primary");
                    sceneRow.Add(injectBtn);
                }
            }
            card.Add(sceneRow);

            return card;
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
            var steamProfile = _steamProfile;
            if (steamProfile == null)
            {
                EditorUtility.DisplayDialog("Sem perfil Steam", "Marque um perfil como Steam em Perfis (campo 'Steam') primeiro.", "OK");
                return;
            }

            var context = BuildContextFactory.Create(_config, steamProfile, new BuildContextFactory.Options { Platform = platform });
            if (!BuildConfirmation.Confirm(context, _config, $"Confirm Steam Build ({platform})"))
                return;

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

        // ── Steam profiles ───────────────────────────────────────────────────────

        private VisualElement BuildProfilesCard()
        {
            var card = BuildPipelineUIStyle.CreateCard("🎮 Perfis Steam",
                "Perfis marcados como Steam (Perfis → Identidade → Steam). Um perfil Steam compila Windows, Mac e Linux; " +
                "os três usam o MESMO conjunto de defines (grupo Standalone do Unity).");

            var steamProfiles = (_config.publishers ?? new System.Collections.Generic.List<PublisherProfile>())
                .Where(p => p != null && p.isSteam).ToList();

            if (steamProfiles.Count == 0)
            {
                card.Add(BuildPipelineUIStyle.CreateCallout(
                    "Nenhum perfil marcado como Steam. Abra Perfis, escolha o perfil e ligue 'Steam'.", "warn"));
                return card;
            }

            if (_steamProfile == null || !steamProfiles.Contains(_steamProfile))
                _steamProfile = steamProfiles[0];

            var names = steamProfiles.Select(p => $"{p.displayName} ({p.EffectiveId})").ToList();
            var picker = new PopupField<string>("Perfil para build/upload", names, steamProfiles.IndexOf(_steamProfile));
            picker.RegisterValueChangedCallback(evt =>
            {
                _steamProfile = steamProfiles[names.IndexOf(evt.newValue)];
                Rebuild();
            });
            card.Add(picker);

            var stack = DefineEnvironment.StackFor(_config, _steamProfile);
            bool hasSocialDefine = stack.Has(PublisherProfile.NativeSocialSteamDefine);
            string defineText = hasSocialDefine
                ? $"✔ '{PublisherProfile.NativeSocialSteamDefine}' entra neste build ({string.Join(", ", stack.For(PublisherProfile.NativeSocialSteamDefine).Select(e => e.source))})."
                : DefineEnvironment.IsSteamworksPresent()
                    ? $"⚠ '{PublisherProfile.NativeSocialSteamDefine}' não entra neste build: o perfil o remove."
                    : $"ℹ Steamworks.NET não está instalado: '{PublisherProfile.NativeSocialSteamDefine}' não é injetado (achievements Steam ficam desligados).";
            card.Add(BuildPipelineUIStyle.CreateCallout(defineText, hasSocialDefine ? "ok" : "warn"));

            card.Add(new Label("Defines finais (Standalone): " + (stack.Final.Count == 0 ? "(nenhum)" : string.Join("; ", stack.Final)))
            {
                style = { fontSize = 10, whiteSpace = WhiteSpace.Normal, color = new Color(0.7f, 0.75f, 0.8f), marginTop = 4 }
            });

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };
            void AddButton(string text, Action action, bool primary = false)
            {
                var button = new Button(action);
                BuildPipelineUIStyle.ApplyIconText(button, text);
                button.AddToClassList("bp-btn");
                if (primary) button.AddToClassList("bp-btn--primary");
                button.style.marginRight = 6;
                row.Add(button);
            }

            AddButton("🪟 Build Windows", () => BuildOnePlatform(PlatformType.Windows64));
            AddButton("🍎 Build Mac", () => BuildOnePlatform(PlatformType.macOS));
            AddButton("🐧 Build Linux", () => BuildOnePlatform(PlatformType.Linux64));
            AddButton("🧬 Ver defines do perfil", () => BuildPipelineWindow.ShowSection(BuildPipelineSections.Profiles, _steamProfile.EffectiveId));
            card.Add(row);
            return card;
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

        // ── Steam Diagnostic Helpers ─────────────────────────────────────────────

        private static string GetSteamAppIdFilePath() =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");

        private static bool HasSteamworksPackage() => DefineEnvironment.IsSteamworksPresent();

        private static void AddSteamworksPackage()
        {
            try
            {
                string manifestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "manifest.json");
                if (File.Exists(manifestPath))
                {
                    string text = File.ReadAllText(manifestPath);
                    if (!text.Contains("com.rlabrecque.steamworks.net"))
                    {
                        int depIdx = text.IndexOf("\"dependencies\"");
                        if (depIdx >= 0)
                        {
                            int openBrace = text.IndexOf('{', depIdx);
                            if (openBrace >= 0)
                            {
                                string insertion = "\n    \"com.rlabrecque.steamworks.net\": \"https://github.com/rlabrecque/Steamworks.NET.git\",";
                                text = text.Insert(openBrace + 1, insertion);
                                File.WriteAllText(manifestPath, text);
                                AssetDatabase.Refresh();
                                EditorUtility.DisplayDialog("Steamworks.NET", "Pacote com.rlabrecque.steamworks.net adicionado ao manifest.json. O Unity começará o download.", "OK");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Erro", "Falha ao editar manifest.json: " + ex.Message, "OK");
            }
        }

        private static string FindSteamManagerScriptPath()
        {
            string[] guids = AssetDatabase.FindAssets("SteamManager t:MonoScript");
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileName(p).Equals("SteamManager.cs", StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private static void CreateSteamManagerScript()
        {
            try
            {
                string targetDir = Path.Combine(Application.dataPath, "Scripts", "Steam");
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                string filePath = Path.Combine(targetDir, "SteamManager.cs");
                File.WriteAllText(filePath, SteamManagerTemplate);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("SteamManager Criado", $"Script SteamManager.cs gerado com sucesso em '{filePath}'. Ele possui auto-inicialização em runtime antes da primeira cena.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Erro", "Falha ao criar SteamManager.cs: " + ex.Message, "OK");
            }
        }

        private static void InjectSteamManagerIntoFirstScene(string scenePath)
        {
            try
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var existing = GameObject.Find("SteamManager") ?? GameObject.Find("[SteamManager]");
                    if (existing == null)
                    {
                        var go = new GameObject("SteamManager");
                        var managerType = AppDomain.CurrentDomain.GetAssemblies()
                            .Select(a => a.GetType("SteamManager"))
                            .FirstOrDefault(t => t != null);
                        if (managerType != null)
                            go.AddComponent(managerType);
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                        EditorUtility.DisplayDialog("Sucesso", $"Objeto SteamManager adicionado e salvo na cena '{scenePath}'.", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Aviso", $"A cena '{scenePath}' já possui o objeto '{existing.name}'.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Erro ao injetar", ex.Message, "OK");
            }
        }

        private const string SteamManagerTemplate = @"// The SteamManager is designed to work with Steamworks.NET
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using System.Collections;
using Steamworks;
#endif

[DisallowMultipleComponent]
public class SteamManager : MonoBehaviour {
#if !DISABLESTEAMWORKS
    protected static SteamManager s_instance;
    protected static bool s_EverInitialized;

    protected bool m_bInitialized;
    public static bool Initialized => s_instance != null && s_instance.m_bInitialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize() {
        if (s_instance == null && !s_EverInitialized) {
            var go = new GameObject(""[SteamManager]"");
            DontDestroyOnLoad(go);
            go.AddComponent<SteamManager>();
        }
    }

    protected virtual void Awake() {
        if (s_instance != null) {
            Destroy(gameObject);
            return;
        }
        s_instance = this;

        if (s_EverInitialized) {
            throw new System.Exception(""Tried to Initialize the SteamAPI twice in one session!"");
        }
        DontDestroyOnLoad(gameObject);

        if (!Packsize.Test()) {
            Debug.LogError(""[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform."", this);
        }

        if (!DllCheck.Test()) {
            Debug.LogError(""[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version."", this);
        }

        try {
            if (SteamAPI.RestartAppIfNecessary((AppId_t)0)) {
                Application.Quit();
                return;
            }
        }
        catch (System.DllNotFoundException e) {
            Debug.LogError(""[Steamworks.NET] Could not load [lib]steam_api.dll/so/dylib. It's likely not in the correct location. Refer to the README for more details.\n"" + e, this);
            Application.Quit();
            return;
        }

        m_bInitialized = SteamAPI.Init();
        if (!m_bInitialized) {
            Debug.LogError(""[Steamworks.NET] SteamAPI_Init() failed. Refer to Valve's documentation or the comment above this line for more information."", this);
            return;
        }
        s_EverInitialized = true;
    }

    protected virtual void OnEnable() {
        if (s_instance == null) s_instance = this;
    }

    protected virtual void Update() {
        if (!m_bInitialized) return;
        SteamAPI.RunCallbacks();
    }

    protected virtual void OnDestroy() {
        if (s_instance != this) return;
        s_instance = null;
        if (!m_bInitialized) return;
        SteamAPI.Shutdown();
    }
#endif
}
";

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void SaveDirty()
        {
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssetIfDirty(_config);
            _onConfigChanged?.Invoke();
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
                    field.value = picked;
                })
                { text = "..." };
                browseBtn.AddToClassList("bp-btn");
                browseBtn.style.marginLeft = 4;
                row.Add(browseBtn);
            }
            return row;
        }
    }

    /// <summary>Kept for existing callers and the menu: Steam now lives in the Build Pipeline window.</summary>
    public static class SteamUploadWindow
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Steam Upload...", priority = 30)]
        public static void Open() => BuildPipelineWindow.ShowSection(BuildPipelineSections.Steam);
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
