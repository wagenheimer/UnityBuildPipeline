using System;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Per-project SteamPipe configuration: App ID, one Depot ID per standalone platform, the
    /// SteamCmd account/path, and the branch to set live. Lives on <see cref="ProjectBuildConfig"/>
    /// so every game's Steam config travels with its own project instead of a hand-edited
    /// .bat + 3 .vdf files sitting in the shared, unversioned SteamCmd ContentBuilder folder that
    /// every Green Sauce Games title's builds used to share.
    /// </summary>
    [Serializable]
    public class SteamUploadConfig
    {
        [Tooltip("Steam App ID for this game (from Steamworks > App Admin). Empty disables Steam upload.")]
        public string appId = "";

        [Tooltip("Absolute path to steamcmd.exe. Defaults to the shared SDK install; only needs to be set once per machine.")]
        public string steamCmdPath = @"E:\Games\steamworks_sdk_141\sdk\tools\ContentBuilder\builder\steamcmd.exe";

        [Tooltip("Steam builder account username. Never store the password here — see Password Env Var.")]
        public string steamUsername = "";

        [Tooltip("Name of an environment variable holding the Steam builder account password. Left unset, " +
                  "steamcmd falls back to its own cached login for this username (works after one manual " +
                  "interactive login + Steam Guard confirmation) instead of failing outright.")]
        public string steamPasswordEnvVar = "STEAM_PASSWORD";

        [Tooltip("Branch to set live after a successful upload (e.g. 'beta'). Empty uploads without setting any branch live — the default, safest choice.")]
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
            if (string.IsNullOrEmpty(steamPasswordEnvVar)) return null;
            return Environment.GetEnvironmentVariable(steamPasswordEnvVar);
        }
    }
}
