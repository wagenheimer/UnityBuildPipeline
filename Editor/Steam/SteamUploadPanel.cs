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

            _root.Add(BuildGuideCard());
            _root.Add(BuildDiagnosticsAndChecklistCard(cfg));
            _root.Add(BuildProfilesCard());
            _root.Add(BuildImportCard(cfg));
            _root.Add(BuildConfigCard(cfg));
            _root.Add(BuildDepotsCard(cfg));
            _root.Add(BuildAutoCloudCard(cfg));
            _root.Add(BuildActionsCard(cfg));
            _root.Add(BuildLogCard());
            _root.Add(BuildHistoryCard());
        }

        // ── Master Toggle Card ───────────────────────────────────────────────────

        private VisualElement BuildMasterToggleCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("🎮 Steam Integration",
                "Enables or disables the Steam ecosystem for this project. When disabled, missing SDK and depot warnings are muted.");

            var toggleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            var toggle = new Toggle("Steam Integration Enabled")
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

                AddLinkBtn("🌐 Game Dashboard", $"https://partner.steamgames.com/apps/landing/{cfg.appId}");
                AddLinkBtn("🚀 Builds (SteamPipe)", $"https://partner.steamgames.com/apps/builds/{cfg.appId}");
                AddLinkBtn("🏆 Achievements & Stats", $"https://partner.steamgames.com/apps/achievements/{cfg.appId}");
                card.Add(linksRow);
            }

            return card;
        }

        // ── Diagnostics & Checklist ──────────────────────────────────────────────

        private VisualElement BuildDiagnosticsAndChecklistCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("🩺 Steam Diagnostics & Checklist",
                "Essential requirements checklist to build, run, and test Steam features in the Editor and Standalone builds.");

            // 1. Steamworks.NET
            bool steamworksInstalled = DefineEnvironment.IsSteamworksPresent();
            var swRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            var swLabel = new Label(steamworksInstalled
                ? "✔ Steamworks.NET is installed in the project."
                : "⚠ Steamworks.NET not found. (Required for Steam achievements, overlay, stats, and cloud saves)")
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
                BuildPipelineUIStyle.ApplyIconText(installBtn, "📦 Add Steamworks.NET (UPM)");
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
                ? "⚠ Steam App ID has not been configured in the section below."
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
                BuildPipelineUIStyle.ApplyIconText(syncBtn, appIdFileExists ? "⚡ Synchronize steam_appid.txt" : "⚡ Create steam_appid.txt");
                syncBtn.AddToClassList("bp-btn");
                syncBtn.AddToClassList("bp-btn--primary");
                appIdRow.Add(syncBtn);
            }
            card.Add(appIdRow);

            // 3. SteamManager.cs
            string managerPath = FindSteamManagerScriptPath();
            bool hasManagerScript = !string.IsNullOrEmpty(managerPath);
            bool hasSafeEditorCheck = hasManagerScript && File.Exists(managerPath) && File.ReadAllText(managerPath).Contains("IsSteamRunning");

            var mgrRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            string mgrMsg = !hasManagerScript
                ? "⚠ SteamManager.cs not found in Assets/. (Essential script for SteamAPI.Init at runtime)"
                : hasSafeEditorCheck
                    ? $"✔ SteamManager.cs presente em '{managerPath}' (com proteção de Editor/Sentry)."
                    : $"✔ SteamManager.cs presente em '{managerPath}' (versão sem proteção de Editor).";

            var mgrLabel = new Label(mgrMsg)
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
                BuildPipelineUIStyle.ApplyIconText(createMgrBtn, "📄 Create SteamManager.cs");
                createMgrBtn.AddToClassList("bp-btn");
                createMgrBtn.AddToClassList("bp-btn--primary");
                mgrRow.Add(createMgrBtn);
            }
            else if (!hasSafeEditorCheck)
            {
                var upgradeMgrBtn = new Button(() =>
                {
                    CreateSteamManagerScript(managerPath);
                    Rebuild();
                });
                BuildPipelineUIStyle.ApplyIconText(upgradeMgrBtn, "⚡ Upgrade Editor Protection");
                upgradeMgrBtn.AddToClassList("bp-btn");
                upgradeMgrBtn.tooltip = "Atualiza o SteamManager para silenciar erros de inicialização no Editor quando a Steam estiver fechada.";
                mgrRow.Add(upgradeMgrBtn);
            }
            card.Add(mgrRow);

            // 4. Injeção na cena inicial
            var firstScene = EditorBuildSettings.scenes?.FirstOrDefault(s => s.enabled);
            var sceneRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            if (firstScene == null)
            {
                sceneRow.Add(new Label("⚠ No scenes enabled in File > Build Settings.")
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
                        ? $"✔ SteamManager has runtime auto-initialization ([RuntimeInitializeOnLoadMethod])."
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
                    BuildPipelineUIStyle.ApplyIconText(injectBtn, "⚡ Inject into Start Scene");
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
                    error == null ? "Import Successful" : "Import Failed",
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

        // ── Guide & Instructions ──────────────────────────────────────────────────

        private VisualElement BuildGuideCard()
        {
            var card = BuildPipelineUIStyle.CreateCard("📘 Steam Deployment & CI/CD Guide",
                "How automated builds and uploads work across Windows, macOS, Linux, and AppDeployHub / Forge.");

            var guideBox = new VisualElement();
            guideBox.AddToClassList("bp-subcard");
            guideBox.style.marginBottom = 6;

            void AddSection(string header, string body)
            {
                var h = new Label(header)
                {
                    style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.9f, 0.95f, 1f), marginBottom = 3 }
                };
                var b = new Label(body)
                {
                    style = { fontSize = 11, color = new Color(0.78f, 0.82f, 0.88f), whiteSpace = WhiteSpace.Normal, marginBottom = 8 }
                };
                b.selection.isSelectable = true;
                guideBox.Add(h);
                guideBox.Add(b);
            }

            AddSection("🌐 Cross-Platform Deploy (Windows uploads Windows, Mac and Linux!)",
                "• Windows can upload ALL 3 platforms (Windows, macOS .app, Linux) simultaneously directly to Valve's servers.\n" +
                "• You do NOT need a physical Mac to upload the Mac build: SteamCmd on Windows packages and uploads the .app folder to the macOS Depot directly.\n" +
                "• If your build server is a Mac runner (e.g. Mac Mini with Forge), SteamCmd runs natively on macOS via 'steamcmd.sh'.");

            AddSection("⬇️ Automatic SteamCmd Installation",
                "• If SteamCmd is missing, click '⬇️ Auto-Install SteamCmd' below. The pipeline downloads the official Valve package (zip on Windows, tar.gz on macOS), extracts it to a clean local programs directory, and configures the path automatically.\n" +
                "• In headless CI / Forge runs, if SteamCmd is missing on the runner, the pipeline automatically downloads it from Valve CDN before uploading.");

            AddSection("🔐 Authentication & Steam Guard",
                "• One-time interactive setup: Run SteamCmd once on the machine and enter your Steam Guard code. SteamCmd caches credentials locally, allowing future builds to upload without asking for 2FA.\n" +
                "• Headless CI / AppDeployHub: Valve recommends creating a dedicated builder account (e.g. greensauce_builder) with 'Upload Builds' permission in Steamworks, and injecting the password via the STEAM_PASSWORD environment variable.");

            AddSection("🤖 Headless CLI Commands for AppDeployHub / Forge",
                "AppDeployHub can trigger Steam builds and uploads using these Unity batchmode commands:\n" +
                "  • Build & Upload everything:\n" +
                "    Unity.exe -batchmode -quit -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildAndUploadSteam\n" +
                "  • Build all Steam targets only:\n" +
                "    Unity.exe -batchmode -quit -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildSteam\n" +
                "  • Upload pre-built depots to Steam:\n" +
                "    Unity.exe -batchmode -quit -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.UploadToSteam [-steamBranch <branch>]");

            card.Add(guideBox);
            return card;
        }

        // ── Config ───────────────────────────────────────────────────────────────

        private VisualElement BuildConfigCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("⚙️ Steam Configuration",
                "Steam App ID, credentials, and platform SteamCmd executables.");

            card.Add(TextRow("App ID", cfg.appId, v => cfg.appId = v));

            // SteamCmd section with auto-installer and status
            var steamCmdBox = new VisualElement();
            steamCmdBox.AddToClassList("bp-subcard");
            steamCmdBox.style.marginTop = 6;
            steamCmdBox.style.marginBottom = 8;

            var activePath = cfg.GetEffectiveSteamCmdPath();
            bool isInstalled = cfg.IsSteamCmdInstalled();

            var statusRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 6 } };
            var statusLeft = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            statusLeft.Add(new Label("SteamCmd Executable Status:") { style = { fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8 } });
            statusLeft.Add(BuildPipelineUIStyle.CreateIconBadge(
                isInstalled ? "✔ Installed" : "⚠ Not Found",
                isInstalled ? "ok" : "warn"));
            statusRow.Add(statusLeft);

            var installBtn = new Button();
            installBtn.clicked += async () =>
            {
                installBtn.SetEnabled(false);
                var (success, msg, path) = await SteamCmdInstaller.InstallAsync(cfg, (p, text) => { });
                EditorUtility.DisplayDialog(success ? "SteamCmd Installed" : "Installation Failed", msg, "OK");
                SaveDirty();
                Rebuild();
            };
            BuildPipelineUIStyle.ApplyIconText(installBtn, isInstalled ? "🔄 Reinstall SteamCmd" : "⬇️ Auto-Install SteamCmd");
            installBtn.AddToClassList("bp-btn");
            if (!isInstalled) installBtn.AddToClassList("bp-btn--primary");
            statusRow.Add(installBtn);
            steamCmdBox.Add(statusRow);

            steamCmdBox.Add(TextRow("SteamCmd Path (Windows)", cfg.steamCmdPath, v => cfg.steamCmdPath = v, browseFile: true));
            steamCmdBox.Add(TextRow("SteamCmd Path (macOS)", cfg.steamCmdPathMac, v => cfg.steamCmdPathMac = v, browseFile: true));

            var activeLabel = new Label($"Active on this OS ({Application.platform}): {activePath}")
            {
                style = { fontSize = 10, color = new Color(0.6f, 0.7f, 0.8f), marginTop = 2 }
            };
            steamCmdBox.Add(activeLabel);
            card.Add(steamCmdBox);

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
            var card = BuildPipelineUIStyle.CreateCard("📦 Depots (Windows / macOS / Linux)",
                "Configure Depot IDs and verify the target content roots for SteamCmd upload. Each platform has its own folder inside Publishers/Steam/{GameFolder}/.");

            // Auto-sync unconfigured depot paths silently
            AutoSyncDepotPathsSilently(cfg);

            var syncRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 8 } };
            var syncBtn = new Button(() => SyncDepotsWithProfiles(cfg));
            BuildPipelineUIStyle.ApplyIconText(syncBtn, "🔄 Sync Paths with Steam Profiles");
            syncBtn.AddToClassList("bp-btn");
            syncBtn.tooltip = "Auto-detect configured Steam profiles and update depot build folders to match their resolved output locations.";
            syncRow.Add(syncBtn);

            if (!string.IsNullOrEmpty(cfg.appId))
            {
                var webDepotsBtn = new Button(() => Application.OpenURL($"https://partner.steamgames.com/apps/depots/{cfg.appId}"));
                BuildPipelineUIStyle.ApplyIconText(webDepotsBtn, "🌐 Steamworks Depots Web");
                webDepotsBtn.AddToClassList("bp-btn");
                webDepotsBtn.style.marginLeft = 6;
                syncRow.Add(webDepotsBtn);
            }
            card.Add(syncRow);

            card.Add(DepotRow("Windows", PlatformType.Windows64, cfg.depotIdWindows, v => cfg.depotIdWindows = v, cfg.lastBuiltPathWindows));
            card.Add(DepotRow("macOS", PlatformType.macOS, cfg.depotIdMac, v => cfg.depotIdMac = v, cfg.lastBuiltPathMac));
            card.Add(DepotRow("Linux", PlatformType.Linux64, cfg.depotIdLinux, v => cfg.depotIdLinux = v, cfg.lastBuiltPathLinux));
            return card;
        }

        private void AutoSyncDepotPathsSilently(SteamUploadConfig cfg)
        {
            if (_config == null || cfg == null) return;
            bool changed = false;

            var winProfile = _config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.Windows64);
            if (winProfile != null && (string.IsNullOrEmpty(cfg.lastBuiltPathWindows) || !Directory.Exists(cfg.lastBuiltPathWindows)))
            {
                var resolved = ProfileOutputPaths.ResolveFolder(_config, winProfile, PlatformType.Windows64);
                if (Directory.Exists(resolved) && cfg.lastBuiltPathWindows != resolved)
                {
                    cfg.lastBuiltPathWindows = resolved;
                    changed = true;
                }
            }

            var macProfile = _config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.macOS);
            if (macProfile != null && (string.IsNullOrEmpty(cfg.lastBuiltPathMac) || !Directory.Exists(cfg.lastBuiltPathMac)))
            {
                var resolved = ProfileOutputPaths.ResolveFolder(_config, macProfile, PlatformType.macOS);
                if (Directory.Exists(resolved) && cfg.lastBuiltPathMac != resolved)
                {
                    cfg.lastBuiltPathMac = resolved;
                    changed = true;
                }
            }

            var linProfile = _config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.Linux64);
            if (linProfile != null && (string.IsNullOrEmpty(cfg.lastBuiltPathLinux) || !Directory.Exists(cfg.lastBuiltPathLinux)))
            {
                var resolved = ProfileOutputPaths.ResolveFolder(_config, linProfile, PlatformType.Linux64);
                if (Directory.Exists(resolved) && cfg.lastBuiltPathLinux != resolved)
                {
                    cfg.lastBuiltPathLinux = resolved;
                    changed = true;
                }
            }

            if (changed)
                SaveDirty();
        }

        private void SyncDepotsWithProfiles(SteamUploadConfig cfg)
        {
            if (_config == null) return;
            int count = 0;

            var winProfile = _config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.Windows64);
            if (winProfile != null)
            {
                var res = ProfileOutputPaths.ResolveFolder(_config, winProfile, PlatformType.Windows64);
                if (cfg.lastBuiltPathWindows != res) { cfg.lastBuiltPathWindows = res; count++; }
            }

            var macProfile = _config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.macOS);
            if (macProfile != null)
            {
                var res = ProfileOutputPaths.ResolveFolder(_config, macProfile, PlatformType.macOS);
                if (cfg.lastBuiltPathMac != res) { cfg.lastBuiltPathMac = res; count++; }
            }

            var linProfile = _config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == PlatformType.Linux64);
            if (linProfile != null)
            {
                var res = ProfileOutputPaths.ResolveFolder(_config, linProfile, PlatformType.Linux64);
                if (cfg.lastBuiltPathLinux != res) { cfg.lastBuiltPathLinux = res; count++; }
            }

            if (count > 0)
            {
                SaveDirty();
                Rebuild();
                EditorUtility.DisplayDialog("Steam Depots Synchronized", $"{count} depot content root path(s) updated to match Steam profile configurations.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Steam Depots Synchronized", "Depot content roots are already up to date with configured Steam profiles.", "OK");
            }
        }

        private VisualElement DepotRow(string label, PlatformType platform, string depotId, Action<string> setDepotId, string lastBuiltPath)
        {
            var row = new VisualElement { style = { marginBottom = 8, paddingBottom = 6, borderBottomWidth = 1, borderBottomColor = new Color(1, 1, 1, 0.08f) } };
            row.Add(TextRow($"{label} Depot ID", depotId, v => { setDepotId(v); SaveDirty(); }));

            var statusRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2, flexWrap = Wrap.Wrap } };
            var exists = !string.IsNullOrEmpty(lastBuiltPath) && Directory.Exists(lastBuiltPath);
            var statusLabel = new Label(string.IsNullOrEmpty(lastBuiltPath)
                ? "Not built yet."
                : (exists ? $"✔ Content root: {lastBuiltPath}" : $"⚠ Content folder does not exist: {lastBuiltPath}"));
            statusLabel.style.fontSize = 10;
            statusLabel.style.color = exists ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.9f, 0.7f, 0.4f);
            statusLabel.style.flexGrow = 1;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusRow.Add(statusLabel);

            if (!string.IsNullOrEmpty(lastBuiltPath))
            {
                var openBtn = new Button(() => BuildOutputFolderActions.OpenPath(lastBuiltPath, $"Open {label} Depot Folder"));
                BuildPipelineUIStyle.ApplyIconText(openBtn, "📂 Open Folder");
                openBtn.AddToClassList("bp-btn");
                openBtn.style.marginRight = 4;
                openBtn.tooltip = exists ? "Open this depot content root in file explorer" : "Folder does not exist yet (click to create or open)";
                statusRow.Add(openBtn);
            }

            var buildBtn = new Button(() => BuildOnePlatform(platform));
            BuildPipelineUIStyle.ApplyIconText(buildBtn, "🔨 Build Now");
            buildBtn.AddToClassList("bp-btn");
            statusRow.Add(buildBtn);

            row.Add(statusRow);
            return row;
        }

        private void BuildOnePlatform(PlatformType platform)
        {
            // Pick the specific Steam profile configured for this target platform, or fall back to active profile
            var steamProfile = _config.publishers?.FirstOrDefault(p => p != null && p.isSteam && p.platform == platform) 
                               ?? _steamProfile;

            if (steamProfile == null)
            {
                EditorUtility.DisplayDialog("No Steam Profile", "Configure at least one profile with 'Steam' checked in Profiles first.", "OK");
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
            var card = BuildPipelineUIStyle.CreateCard("🎮 Steam Profiles",
                "Perfis marcados como Steam (Perfis → Identidade → Steam). Um perfil Steam compila Windows, Mac e Linux; " +
                "os três usam o MESMO conjunto de defines (grupo Standalone do Unity).");

            var steamProfiles = (_config.publishers ?? new System.Collections.Generic.List<PublisherProfile>())
                .Where(p => p != null && p.isSteam).ToList();

            if (steamProfiles.Count == 0)
            {
                card.Add(BuildPipelineUIStyle.CreateCallout(
                    "No profile marked as Steam. Go to Profiles, select a profile and toggle 'Steam' on.", "warn"));
                return card;
            }

            if (_steamProfile == null || !steamProfiles.Contains(_steamProfile))
                _steamProfile = steamProfiles[0];

            var names = steamProfiles.Select(p => $"{p.displayName} ({p.EffectiveId})").ToList();
            var picker = new PopupField<string>("Active Profile for Build/Upload", names, steamProfiles.IndexOf(_steamProfile));
            picker.RegisterValueChangedCallback(evt =>
            {
                _steamProfile = steamProfiles[names.IndexOf(evt.newValue)];
                Rebuild();
            });
            card.Add(picker);

            var stack = DefineEnvironment.StackFor(_config, _steamProfile);
            bool hasSocialDefine = stack.Has(PublisherProfile.NativeSocialSteamDefine);
            string defineText = hasSocialDefine
                ? $"✔ '{PublisherProfile.NativeSocialSteamDefine}' is included in this build ({string.Join(", ", stack.For(PublisherProfile.NativeSocialSteamDefine).Select(e => e.source))})."
                : DefineEnvironment.IsSteamworksPresent()
                    ? $"⚠ '{PublisherProfile.NativeSocialSteamDefine}' is not included in this build (removed by profile defines)."
                    : $"ℹ Steamworks.NET is not installed: '{PublisherProfile.NativeSocialSteamDefine}' cannot be injected (Steam achievements will be disabled).";
            card.Add(BuildPipelineUIStyle.CreateCallout(defineText, hasSocialDefine ? "ok" : "warn"));

            card.Add(new Label("Final Standalone Defines: " + (stack.Final.Count == 0 ? "(nenhum)" : string.Join("; ", stack.Final)))
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
            AddButton("🧬 View Profile Defines", () => BuildPipelineWindow.ShowSection(BuildPipelineSections.Profiles, _steamProfile.EffectiveId));
            card.Add(row);
            return card;
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        private VisualElement BuildActionsCard(SteamUploadConfig cfg)
        {
            var card = BuildPipelineUIStyle.CreateCard("🚀 Upload to Steam", "Build standalone targets and upload them to SteamPipe via SteamCmd.");

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };

            var buildAndUploadBtn = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Confirm Full Steam Deploy",
                    $"This will sequentially compile all configured Steam platforms (Windows, macOS, Linux) and immediately upload them to Steam App {cfg.appId}.\n\nProceed?", "Build & Upload", "Cancel"))
                    return;

                _logView.Clear();
                AppendLog($"=== Starting Full Build & Upload for App {cfg.appId} ===");

                if (cfg.HasWindowsDepot) BuildOnePlatform(PlatformType.Windows64);
                if (cfg.HasMacDepot) BuildOnePlatform(PlatformType.macOS);
                if (cfg.HasLinuxDepot) BuildOnePlatform(PlatformType.Linux64);

                AppendLog("=== Local builds completed. Starting SteamCmd upload... ===");
                SteamUploadRunner.Upload(_config, (success, summary) =>
                {
                    AppendLog(success ? $"✔ {summary}" : $"✕ {summary}");
                    Rebuild();
                });
                Rebuild();
            });
            BuildPipelineUIStyle.ApplyIconText(buildAndUploadBtn, "⚡ Build All & Upload to Steam");
            buildAndUploadBtn.AddToClassList("bp-btn");
            buildAndUploadBtn.AddToClassList("bp-btn--success");
            buildAndUploadBtn.style.marginRight = 6;
            row.Add(buildAndUploadBtn);

            var buildAllBtn = new Button(() =>
            {
                if (cfg.HasWindowsDepot) BuildOnePlatform(PlatformType.Windows64);
                if (cfg.HasMacDepot) BuildOnePlatform(PlatformType.macOS);
                if (cfg.HasLinuxDepot) BuildOnePlatform(PlatformType.Linux64);
            });
            BuildPipelineUIStyle.ApplyIconText(buildAllBtn, "🔨 Build All Platforms (No Upload)");
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

        public const string SteamworksGitPackageUrl = "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#20.0.0";

        private static void AddSteamworksPackage()
        {
            try
            {
                string manifestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "manifest.json");
                if (File.Exists(manifestPath))
                {
                    string text = File.ReadAllText(manifestPath);
                    if (text.Contains("com.rlabrecque.steamworks.net"))
                    {
                        var regex = new System.Text.RegularExpressions.Regex("\"com\\.rlabrecque\\.steamworks\\.net\"\\s*:\\s*\"[^\"]*\"");
                        text = regex.Replace(text, $"\"com.rlabrecque.steamworks.net\": \"{SteamworksGitPackageUrl}\"");
                        File.WriteAllText(manifestPath, text);
                    }
                    else
                    {
                        int depIdx = text.IndexOf("\"dependencies\"");
                        if (depIdx >= 0)
                        {
                            int openBrace = text.IndexOf('{', depIdx);
                            if (openBrace >= 0)
                            {
                                string insertion = $"\n    \"com.rlabrecque.steamworks.net\": \"{SteamworksGitPackageUrl}\",";
                                text = text.Insert(openBrace + 1, insertion);
                                File.WriteAllText(manifestPath, text);
                            }
                        }
                    }

                    try
                    {
                        UnityEditor.PackageManager.Client.Resolve();
                    }
                    catch { }

                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog("Steamworks.NET", $"Pacote configurado no manifest.json:\n\n{SteamworksGitPackageUrl}\n\nO Unity agora fará o download e importação dos arquivos do Steamworks.", "OK");
                    return;
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

        private static void CreateSteamManagerScript(string existingPath = null)
        {
            try
            {
                string filePath;
                if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
                {
                    if (!EditorUtility.DisplayDialog("Update SteamManager.cs",
                            $"Deseja atualizar '{existingPath}' para a versão com proteção de Editor/Sentry (não emite erro se o cliente Steam estiver fechado)?",
                            "Upgrade", "Cancel"))
                        return;

                    filePath = existingPath;
                }
                else
                {
                    string targetDir = Path.Combine(Application.dataPath, "Scripts", "Steam");
                    if (!Directory.Exists(targetDir))
                        Directory.CreateDirectory(targetDir);
                    filePath = Path.Combine(targetDir, "SteamManager.cs");
                }

                File.WriteAllText(filePath, SteamManagerTemplate);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("SteamManager Configured",
                    $"SteamManager.cs written successfully to '{filePath}'.\n\nIncludes runtime auto-initialization before first scene and safe-mode protection for Editor/Sentry when Steam is closed.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Erro", "Falha ao gravar SteamManager.cs: " + ex.Message, "OK");
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
                EditorUtility.DisplayDialog("Injection Error", ex.Message, "OK");
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
#if UNITY_EDITOR
        try {
            if (!SteamAPI.IsSteamRunning()) return;
        } catch { return; }
#endif
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
#if UNITY_EDITOR
            Debug.LogWarning(""[Steamworks.NET] SteamAPI_Init() não pôde inicializar no Editor (verifique se o Steam está aberto e com licença do App ID). O jogo continuará normalmente sem Steam."", this);
#else
            Debug.LogError(""[Steamworks.NET] SteamAPI_Init() failed. Refer to Valve's documentation or the comment above this line for more information."", this);
#endif
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

        // ── Auto-Cloud: cross-platform saves ─────────────────────────────────────

        /// <summary>
        /// Declares the save folder per OS and renders, for this game, the exact Steamworks table (one Root Path +
        /// one Root Override per other OS with "Replace path") plus the checks that catch a save that would split.
        /// </summary>
        private VisualElement BuildAutoCloudCard(SteamUploadConfig cfg)
        {
            if (_config.autoCloud == null) _config.autoCloud = new SteamAutoCloudConfig();
            var ac = _config.autoCloud;

            var card = BuildPipelineUIStyle.CreateCard("☁️ Steam Auto-Cloud (cross-platform saves)",
                "Emits the Steamworks configuration that keeps the player's save as the same file on Windows, macOS and Linux.");

            var toggle = new Toggle("Declare Auto-Cloud for this game")
            {
                value = ac.enabled,
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                ac.enabled = evt.newValue;
                SaveDirty();
                Rebuild();
            });
            card.Add(toggle);

            if (!ac.enabled)
            {
                card.Add(BuildPipelineUIStyle.CreateCallout(
                    "Ligue para gerar a tabela do Steamworks e checar as pastas. O jogo precisa gravar a MESMA pasta relativa nos três SOs — " +
                    "no macOS não use Application.persistentDataPath (o Unity alterna entre 'unity.<empresa>.<jogo>' e '<empresa>/<jogo>').",
                    "info"));
                return card;
            }

            var derived = new VisualElement();
            void RefreshDerived()
            {
                derived.Clear();
                derived.Add(BuildAutoCloudRows(ac, cfg));
            }

            card.Add(TextRow("Windows folder (relative to %APPDATA%)", ac.windowsSubdirectory,
                v => { ac.windowsSubdirectory = v; RefreshDerived(); }));
            card.Add(TextRow("macOS folder (relative to ~/Library/Application Support)", ac.macSubdirectory,
                v => { ac.macSubdirectory = v; RefreshDerived(); }));
            card.Add(TextRow("Linux folder (relative to ~)", ac.linuxSubdirectory,
                v => { ac.linuxSubdirectory = v; RefreshDerived(); }));
            card.Add(TextRow("File pattern", ac.filePattern,
                v => { ac.filePattern = v; RefreshDerived(); }));

            var recursive = new Toggle("Recursive (include subfolders)") { value = ac.recursive };
            recursive.RegisterValueChangedCallback(evt =>
            {
                ac.recursive = evt.newValue;
                SaveDirty();
                RefreshDerived();
            });
            card.Add(recursive);

            RefreshDerived();
            card.Add(derived);

            var buttons = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 8 }
            };

            var copyButton = new Button(() =>
            {
                GUIUtility.systemCopyBuffer = SteamAutoCloudPlanner.SteamworksTable(ac);
                Debug.Log("[BuildPipeline] Configuração do Steam Auto-Cloud copiada para a área de transferência.");
            });
            BuildPipelineUIStyle.ApplyIconText(copyButton, "📋 Copy Steamworks configuration");
            copyButton.AddToClassList("bp-btn");
            copyButton.AddToClassList("bp-btn--primary");
            copyButton.style.marginRight = 6;
            buttons.Add(copyButton);

            var probeButton = new Button();
            var probeLabel = new Label
            {
                style = { fontSize = 10, whiteSpace = WhiteSpace.Normal, flexGrow = 1, color = new Color(0.65f, 0.75f, 0.85f) }
            };
            probeButton.clicked += () => probeLabel.text = ProbeAutoCloudFolder(ac);
            BuildPipelineUIStyle.ApplyIconText(probeButton, "🔎 Check this machine");
            probeButton.AddToClassList("bp-btn");
            buttons.Add(probeButton);

            card.Add(buttons);
            card.Add(probeLabel);

            card.Add(BuildPipelineUIStyle.CreateCallout(
                "O jogo deve gravar a mesma pasta relativa nos três SOs (montada no código, não via Application.persistentDataPath no macOS). " +
                "Teste em Steamworks ▸ console (steam://open/console ▸ testappcloudpaths <AppID> ▸ set_spew_level 4 4). " +
                "Não sincronize configuração de máquina (resolução, qualidade de vídeo).",
                "info"));

            return card;
        }

        /// <summary>Checks + the Steamworks table rendered as cards (Root Path first, then the Root Overrides).</summary>
        private VisualElement BuildAutoCloudRows(SteamAutoCloudConfig ac, SteamUploadConfig cfg)
        {
            var container = new VisualElement();

            foreach (var issue in SteamAutoCloudPlanner.Check(ac, cfg != null && cfg.steamEnabled))
                container.Add(BuildPipelineUIStyle.CreateCallout(issue.Message, issue.IsWarning ? "warning" : "info"));

            var rows = SteamAutoCloudPlanner.Rows(ac);
            var root = rows[0];
            container.Add(AutoCloudRowElement(
                root.Label,
                $"Root: {root.Root}",
                $"Subdirectory: {root.Subdirectory}",
                $"Pattern: {root.FilePattern}   |   Recursive: {(root.Recursive ? "yes" : "no")}",
                "OS restriction: Cross-Platform (Multiplataforma)   |   Cross-platform sync: Sim"));

            foreach (var row in rows.Where(r => r.IsOverride))
            {
                container.Add(AutoCloudRowElement(
                    row.Label,
                    $"Original root: {row.OriginalRoot}   →   New root: {row.Root}",
                    $"Add/Replace path: {row.Subdirectory}",
                    "Replace path: yes (sim)"));
            }

            container.Add(new Label(
                $"Windows: {SteamAutoCloudPlanner.PhysicalPath(ac, SteamAutoCloudPlatform.Windows)}\n" +
                $"macOS:   {SteamAutoCloudPlanner.PhysicalPath(ac, SteamAutoCloudPlatform.Mac)}\n" +
                $"Linux:   {SteamAutoCloudPlanner.PhysicalPath(ac, SteamAutoCloudPlatform.Linux)}")
            {
                style =
                {
                    fontSize = 10,
                    color = new Color(0.62f, 0.72f, 0.82f),
                    whiteSpace = WhiteSpace.Normal,
                    marginTop = 6
                }
            });

            return container;
        }

        private static VisualElement AutoCloudRowElement(string title, params string[] lines)
        {
            var box = new VisualElement();
            box.AddToClassList("bp-subcard");
            box.style.marginTop = 6;

            box.Add(new Label(title)
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 }
            });
            foreach (var line in lines)
            {
                box.Add(new Label(line)
                {
                    style = { fontSize = 10, color = new Color(0.75f, 0.82f, 0.9f), whiteSpace = WhiteSpace.Normal }
                });
            }

            return box;
        }

        /// <summary>Looks for save files in the folder declared for the OS the Editor is running on.</summary>
        private static string ProbeAutoCloudFolder(SteamAutoCloudConfig ac)
        {
            SteamAutoCloudPlatform platform;
            string configured;
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    platform = SteamAutoCloudPlatform.Mac;
                    configured = SteamAutoCloudPlanner.Normalize(ac.macSubdirectory);
                    break;
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer:
                    platform = SteamAutoCloudPlatform.Linux;
                    configured = SteamAutoCloudPlanner.Normalize(ac.linuxSubdirectory);
                    break;
                default:
                    platform = SteamAutoCloudPlatform.Windows;
                    configured = SteamAutoCloudPlanner.Normalize(ac.windowsSubdirectory);
                    break;
            }

            if (configured.Length == 0)
                return "Folder for this OS is not declared.";

            try
            {
                var folder = SteamAutoCloudPlanner.LocalFolder(ac, platform);
                if (!Directory.Exists(folder))
                    return $"⚠ Not found on this machine: {folder}";

                var pattern = string.IsNullOrWhiteSpace(ac.filePattern) ? "*.sav" : ac.filePattern.Trim();
                var option = ac.recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(folder, pattern, option);
                if (files.Length == 0)
                    return $"⚠ Folder exists but has no '{pattern}' file: {folder}";

                var names = string.Join(", ", files.Take(6).Select(Path.GetFileName));
                return $"✔ {files.Length} file(s) in {folder}: {names}{(files.Length > 6 ? ", …" : "")}";
            }
            catch (Exception ex)
            {
                return "⚠ " + ex.Message;
            }
        }

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
