using System;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Steam Auto-Cloud declaration for a cross-platform save: the folder RELATIVE to each OS root where the game
    /// writes its saves. Nothing here changes the build — <see cref="SteamAutoCloudPlanner"/> turns it into the exact
    /// Steamworks rows (one Root Path + one Root Override per other OS, with "Replace Path") and into checks, so the
    /// player's save is the same file on Windows, macOS and Linux.
    /// </summary>
    [Serializable]
    public class SteamAutoCloudConfig
    {
        [Tooltip("Turns on the Auto-Cloud card and its checks for this game.")]
        public bool enabled = false;

        [Tooltip("File mask synced by Steam (e.g. *.sav). Keep it narrow: never sync the whole folder.")]
        public string filePattern = "*.sav";

        [Tooltip("Include subfolders. Recommended: on.")]
        public bool recursive = true;

        [Header("Folder below each OS root (same leaf name on all three is recommended)")]
        [Tooltip("Relative to %APPDATA% (Steamworks root 'WinAppDataRoaming'). Example: Green Sauce Games/My Game (Steam)")]
        public string windowsSubdirectory = "";

        [Tooltip("Relative to ~/Library/Application Support (Steamworks root 'MacAppSupport'). Example: Green Sauce Games/My Game (Steam)")]
        public string macSubdirectory = "";

        [Tooltip("Relative to ~ (Steamworks root 'LinuxHome'). Example: .config/unity3d/Green Sauce Games/My Game (Steam)")]
        public string linuxSubdirectory = "";

        /// <summary>False when at least one of the three folders was never declared (panel and validator warn about it).</summary>
        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(windowsSubdirectory) &&
            !string.IsNullOrWhiteSpace(macSubdirectory) &&
            !string.IsNullOrWhiteSpace(linuxSubdirectory);
    }
}
