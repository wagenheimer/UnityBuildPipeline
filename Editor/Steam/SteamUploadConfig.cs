using System;
using System.IO;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Per-project SteamPipe configuration: App ID, one Depot ID per standalone platform, the
    /// SteamCmd account/path, and the branch to set live. Lives on <see cref="ProjectBuildConfig"/>
    /// so every game's Steam config travels with its own project instead of a hand-edited
    /// .bat + 3 .vdf files sitting in the shared, unversioned SteamCmd ContentBuilder folder.
    /// </summary>
    [Serializable]
    public class SteamUploadConfig
    {
        [Tooltip("Enables Steam integration and diagnostics for this project. When disabled, missing SDK alerts are silenced.")]
        public bool steamEnabled = false;

        [Tooltip("Steam App ID for this game (from Steamworks > App Admin). Empty disables Steam upload.")]
        public string appId = "";

        [Tooltip("Absolute path to steamcmd.exe on Windows. Defaults to standard locations or ContentBuilder.")]
        public string steamCmdPath = @"E:\Games\steamworks_sdk_141\sdk\tools\ContentBuilder\builder\steamcmd.exe";

        [Tooltip("Absolute path to steamcmd.sh on macOS. Defaults to ~/steamcmd/steamcmd.sh if empty.")]
        public string steamCmdPathMac = "";

        [Tooltip("Steam builder account username. Can also be provided via STEAM_USERNAME environment variable.")]
        public string steamUsername = "";

        [Tooltip("Name of an environment variable holding the Steam builder account password. Left unset, " +
                  "steamcmd falls back to its own cached login for this username (works after one manual " +
                  "interactive login + Steam Guard confirmation) instead of failing outright.")]
        public string steamPasswordEnvVar = "STEAM_PASSWORD";

        [Tooltip("Branch to set live after a successful upload (e.g. 'beta'). Can also be overridden via STEAM_BRANCH env var. Empty uploads without setting any branch live.")]
        public string setLiveBranch = "";

        [Header("Depots (one per standalone platform — leave a Depot ID empty to skip that platform)")]
        public string depotIdWindows = "";
        public string depotIdMac = "";
        public string depotIdLinux = "";

        [Header("Last successful build per platform (auto-filled by 'Build Now' — not hand-edited)")]
        public string lastBuiltPathWindows = "";
        public string lastBuiltPathMac = "";
        public string lastBuiltPathLinux = "";

        public bool HasWindowsDepot => !string.IsNullOrEmpty(depotIdWindows);
        public bool HasMacDepot => !string.IsNullOrEmpty(depotIdMac);
        public bool HasLinuxDepot => !string.IsNullOrEmpty(depotIdLinux);
        public bool HasAnyDepot => HasWindowsDepot || HasMacDepot || HasLinuxDepot;
        public bool IsConfigured => !string.IsNullOrEmpty(appId) && HasAnyDepot;

        public string GetEffectivePassword()
        {
            if (!string.IsNullOrEmpty(steamPasswordEnvVar))
            {
                var env = Environment.GetEnvironmentVariable(steamPasswordEnvVar);
                if (!string.IsNullOrEmpty(env)) return env;
            }
            return Environment.GetEnvironmentVariable("STEAM_PASSWORD");
        }

        public string GetEffectiveUsername()
        {
            var env = Environment.GetEnvironmentVariable("STEAM_USERNAME");
            if (!string.IsNullOrEmpty(env)) return env;
            return steamUsername;
        }

        public string GetEffectiveBranch()
        {
            var env = Environment.GetEnvironmentVariable("STEAM_BRANCH");
            if (!string.IsNullOrEmpty(env)) return env;
            return setLiveBranch;
        }

        /// <summary>
        /// Returns the platform-appropriate steamcmd executable path, falling back to standard install locations.
        /// </summary>
        public string GetEffectiveSteamCmdPath()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                if (!string.IsNullOrEmpty(steamCmdPathMac) && File.Exists(steamCmdPathMac))
                    return steamCmdPathMac;

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var candidates = new[]
                {
                    Path.Combine(home, "steamcmd", "steamcmd.sh"),
                    Path.Combine(home, "Library", "Application Support", "SteamCmd", "steamcmd.sh"),
                    "/usr/local/bin/steamcmd",
                    "/opt/homebrew/bin/steamcmd"
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return c;
                }

                return !string.IsNullOrEmpty(steamCmdPathMac) ? steamCmdPathMac : Path.Combine(home, "steamcmd", "steamcmd.sh");
            }

            // Windows
            if (!string.IsNullOrEmpty(steamCmdPath) && File.Exists(steamCmdPath))
                return steamCmdPath;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winCandidates = new[]
            {
                Path.Combine(localAppData, "Programs", "SteamCmd", "steamcmd.exe"),
                @"C:\steamcmd\steamcmd.exe"
            };

            foreach (var c in winCandidates)
            {
                if (File.Exists(c)) return c;
            }

            return steamCmdPath;
        }

        public bool IsSteamCmdInstalled()
        {
            var path = GetEffectiveSteamCmdPath();
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }
    }
}
