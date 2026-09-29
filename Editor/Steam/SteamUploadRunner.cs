using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Builds each standalone platform, generates the app/depot .vdf scripts SteamCmd needs, and runs
    /// SteamCmd itself — all driven from <see cref="SteamUploadConfig"/> on the project's own
    /// <see cref="ProjectBuildConfig"/>, instead of the hand-maintained per-game .bat + .vdf trio that used
    /// to live in the shared, unversioned SteamCmd ContentBuilder folder.
    /// </summary>
    public static class SteamUploadRunner
    {
        /// <summary>Raised for every line SteamCmd prints, so a UI can stream it live.</summary>
        public static event Action<string> OnLogLine;

        public static bool IsUploading { get; private set; }

        private static string GeneratedScriptsDir =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "SteamUpload");

        /// <summary>Runs a normal pipeline build for one standalone platform using the Steam publisher
        /// profile, then records the resolved output folder as that platform's depot content root.</summary>
        public static BuildResultSummary BuildPlatform(ProjectBuildConfig config, PublisherProfile steamProfile, PlatformType platform)
        {
            var ctx = new BuildContext
            {
                Config = config,
                Publisher = steamProfile.publisher,
                PublisherProfile = steamProfile,
                Language = config.defaultLanguage,
                Platform = platform,
                Demo = steamProfile.isDemo
            };

            var result = BuildPipelineRunner.Execute(ctx);
            if (!result.Success || string.IsNullOrEmpty(result.OutputPath))
                return result;

            // Windows/Linux: OutputPath is the executable file, so its directory is the folder to upload.
            // macOS: OutputPath is the .app bundle itself (a directory) — Steam wants the folder *containing*
            // it, not its contents, so this is also Path.GetDirectoryName either way.
            var contentRoot = Path.GetDirectoryName(result.OutputPath);

            switch (platform)
            {
                case PlatformType.Windows64: config.steamUpload.lastBuiltPathWindows = contentRoot; break;
                case PlatformType.macOS: config.steamUpload.lastBuiltPathMac = contentRoot; break;
                case PlatformType.Linux64: config.steamUpload.lastBuiltPathLinux = contentRoot; break;
            }
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            return result;
        }

        /// <summary>Writes app_build.vdf + one depot_*.vdf per configured, already-built platform under
        /// Library/SteamUpload/ (project-local, gitignored — regenerated every run). Returns the app vdf path.</summary>
        public static string GenerateVdfFiles(SteamUploadConfig cfg)
        {
            Directory.CreateDirectory(GeneratedScriptsDir);

            var depotsBlock = "";
            void AddDepot(string depotId, string contentRoot)
            {
                if (string.IsNullOrEmpty(depotId) || string.IsNullOrEmpty(contentRoot)) return;

                var depotVdfPath = Path.Combine(GeneratedScriptsDir, $"depot_{depotId}.vdf");
                File.WriteAllText(depotVdfPath,
$@"""DepotBuildConfig""
{{
	""DepotID"" ""{depotId}""
	""contentroot"" ""{contentRoot}""
	""FileMapping""
	{{
		""LocalPath"" ""*""
		""DepotPath"" "".""
		""recursive"" ""1""
	}}
	""FileExclusion"" ""*.pdb""
}}");
                depotsBlock += $"\t\t\"{depotId}\"\t\"{depotVdfPath}\"\n";
            }

            AddDepot(cfg.depotIdWindows, cfg.lastBuiltPathWindows);
            AddDepot(cfg.depotIdMac, cfg.lastBuiltPathMac);
            AddDepot(cfg.depotIdLinux, cfg.lastBuiltPathLinux);

            var appVdfPath = Path.Combine(GeneratedScriptsDir, $"app_build_{cfg.appId}.vdf");
            var setLive = string.IsNullOrEmpty(cfg.setLiveBranch) ? "" : cfg.setLiveBranch;
            File.WriteAllText(appVdfPath,
$@"""appbuild""
{{
	""appid"" ""{cfg.appId}""
	""desc"" ""Automated upload via UnityBuildPipeline""
	""buildoutput"" ""{Path.Combine(GeneratedScriptsDir, "output")}""
	""contentroot"" """"
	""setlive"" ""{setLive}""
	""preview"" ""0""
	""local""	""""
	""depots""
	{{
{depotsBlock}	}}
}}");
            return appVdfPath;
        }

        /// <summary>
        /// Runs SteamCmd against the generated app vdf. Logs stream via <see cref="OnLogLine"/> as they
        /// arrive; the callback fires on the main thread via <see cref="EditorApplication.update"/> so UI
        /// code can touch VisualElements safely.
        /// </summary>
        public static void Upload(ProjectBuildConfig config, Action<bool, string> onComplete)
        {
            var cfg = config.steamUpload;
            if (!cfg.IsConfigured)
            {
                onComplete?.Invoke(false, "Steam Upload isn't configured yet: set an App ID and at least one Depot ID.");
                return;
            }
            if (!File.Exists(cfg.steamCmdPath))
            {
                onComplete?.Invoke(false, $"steamcmd.exe not found at '{cfg.steamCmdPath}'.");
                return;
            }
            if (IsUploading)
            {
                onComplete?.Invoke(false, "An upload is already in progress.");
                return;
            }

            var appVdfPath = GenerateVdfFiles(cfg);
            var password = cfg.GetEffectivePassword();
            var loginArgs = string.IsNullOrEmpty(password)
                ? $"+login {cfg.steamUsername}"
                : $"+login {cfg.steamUsername} {password}";

            var psi = new ProcessStartInfo
            {
                FileName = cfg.steamCmdPath,
                Arguments = $"{loginArgs} +run_app_build_http \"{appVdfPath}\" +quit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            IsUploading = true;
            var windowsIncluded = cfg.HasWindowsDepot && !string.IsNullOrEmpty(cfg.lastBuiltPathWindows);
            var macIncluded = cfg.HasMacDepot && !string.IsNullOrEmpty(cfg.lastBuiltPathMac);
            var linuxIncluded = cfg.HasLinuxDepot && !string.IsNullOrEmpty(cfg.lastBuiltPathLinux);
            var lastLine = "";

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { lastLine = e.Data; QueueLogLine(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lastLine = e.Data; QueueLogLine("[stderr] " + e.Data); } };

            void Finish(bool success, string summary)
            {
                IsUploading = false;
                SteamUploadHistory.Append(new SteamUploadHistoryEntry
                {
                    timestampUtc = DateTime.UtcNow.ToString("o"),
                    appId = cfg.appId,
                    branch = cfg.setLiveBranch,
                    windowsIncluded = windowsIncluded,
                    macIncluded = macIncluded,
                    linuxIncluded = linuxIncluded,
                    success = success,
                    summary = summary
                });
                QueueMainThread(() => onComplete?.Invoke(success, summary));
            }

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Finish(false, $"Failed to start steamcmd: {ex.Message}");
                return;
            }

            // Poll for exit on a background thread so the Editor stays responsive; steamcmd uploads can
            // take minutes for a full multi-platform depot set.
            new Thread(() =>
            {
                process.WaitForExit();
                var exitCode = process.ExitCode;
                var success = exitCode == 0 && !LooksLikeFailure(lastLine);
                Finish(success, success
                    ? "Upload finished successfully."
                    : $"steamcmd exited with code {exitCode}. Last line: {lastLine}");
            })
            { IsBackground = true }.Start();
        }

        private static bool LooksLikeFailure(string lastLine) =>
            !string.IsNullOrEmpty(lastLine) &&
            (lastLine.IndexOf("FAILED", StringComparison.OrdinalIgnoreCase) >= 0 ||
             lastLine.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 &&
             lastLine.IndexOf("0 errors", StringComparison.OrdinalIgnoreCase) < 0);

        private static void QueueLogLine(string line) => QueueMainThread(() => OnLogLine?.Invoke(line));

        private static void QueueMainThread(Action action)
        {
            void Handler()
            {
                EditorApplication.update -= Handler;
                action();
            }
            EditorApplication.update += Handler;
        }

        // ── Legacy import ────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses an existing <c>run_build_&lt;game&gt;.bat</c> from the shared SteamCmd ContentBuilder
        /// folder (plus the app/depot .vdf files it references) and fills <see cref="SteamUploadConfig"/>
        /// from it — so migrating a game already shipping on Steam via the old per-game .bat/.vdf trio takes
        /// one click instead of retyping App ID/Depot IDs by hand.
        /// </summary>
        public static string ImportLegacyConfig(SteamUploadConfig cfg, string batPath)
        {
            if (!File.Exists(batPath)) return "Batch file not found.";
            var batDir = Path.GetDirectoryName(batPath);
            var batText = File.ReadAllText(batPath);

            var loginMatch = Regex.Match(batText, @"\+login\s+(\S+)\s+(\S+)");
            if (!loginMatch.Success) loginMatch = Regex.Match(batText, @"\+login\s+(\S+)");
            var steamCmdMatch = Regex.Match(batText, @"^(.*?steamcmd\.exe)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var appVdfMatch = Regex.Match(batText, @"\+run_app_build(?:_http)?\s+([^\s+]+\.vdf)");

            if (!appVdfMatch.Success)
                return "Could not find '+run_app_build ... .vdf' in that .bat file.";

            if (loginMatch.Success) cfg.steamUsername = loginMatch.Groups[1].Value;
            if (steamCmdMatch.Success)
                cfg.steamCmdPath = Path.GetFullPath(Path.Combine(batDir, steamCmdMatch.Groups[1].Value.Trim()));

            // steamcmd resolves a relative +run_app_build path against ITS OWN folder (e.g.
            // ContentBuilder\builder\), not the .bat's folder — so "..\scripts\x.vdf" from a .bat in
            // ContentBuilder\ really means ContentBuilder\scripts\x.vdf. Try steamcmd's folder first, then
            // fall back to the .bat's folder for setups that launch steamcmd from the .bat's own directory.
            var vdfRef = appVdfMatch.Groups[1].Value.Trim('"');
            var steamCmdDir = Path.GetDirectoryName(cfg.steamCmdPath);
            var candidates = Path.IsPathRooted(vdfRef)
                ? new[] { vdfRef }
                : new[] { Path.Combine(steamCmdDir ?? batDir, vdfRef), Path.Combine(batDir, vdfRef) };

            var appVdfPath = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
            if (appVdfPath == null)
                return "App build script referenced by the .bat wasn't found. Looked in:\n  " +
                       string.Join("\n  ", candidates.Select(Path.GetFullPath));

            var appVdfText = File.ReadAllText(appVdfPath);
            var appIdMatch = Regex.Match(appVdfText, @"""appid""\s*""(\d+)""", RegexOptions.IgnoreCase);
            if (appIdMatch.Success) cfg.appId = appIdMatch.Groups[1].Value;

            var setLiveMatch = Regex.Match(appVdfText, @"""setlive""\s*""([^""]*)""", RegexOptions.IgnoreCase);
            if (setLiveMatch.Success) cfg.setLiveBranch = setLiveMatch.Groups[1].Value;

            var depotDir = Path.GetDirectoryName(appVdfPath);
            foreach (Match depotRef in Regex.Matches(appVdfText, @"""(\d+)""\s*""([^""]+\.vdf)"""))
            {
                var depotId = depotRef.Groups[1].Value;
                var depotVdfPath = depotRef.Groups[2].Value.Trim();
                if (!Path.IsPathRooted(depotVdfPath)) depotVdfPath = Path.Combine(depotDir, depotVdfPath);
                if (!File.Exists(depotVdfPath)) continue;

                var depotText = File.ReadAllText(depotVdfPath);
                var contentRootMatch = Regex.Match(depotText, @"""contentroot""\s*""([^""]*)""", RegexOptions.IgnoreCase);
                var contentRoot = contentRootMatch.Success ? contentRootMatch.Groups[1].Value : null;

                // No reliable per-platform field in a legacy depot vdf — infer from the folder name, which
                // every existing ContentBuilder script in this SDK install names Windows/Mac/Linux (or macOS).
                var lower = (contentRoot ?? "") + " " + depotVdfPath;
                if (Regex.IsMatch(lower, "mac", RegexOptions.IgnoreCase))
                {
                    cfg.depotIdMac = depotId;
                    cfg.lastBuiltPathMac = contentRoot;
                }
                else if (Regex.IsMatch(lower, "linux", RegexOptions.IgnoreCase))
                {
                    cfg.depotIdLinux = depotId;
                    cfg.lastBuiltPathLinux = contentRoot;
                }
                else
                {
                    cfg.depotIdWindows = depotId;
                    cfg.lastBuiltPathWindows = contentRoot;
                }
            }

            return cfg.IsConfigured
                ? null // null = success
                : "Parsed the .bat/.vdf files but couldn't resolve an App ID and at least one Depot ID from them.";
        }
    }
}
