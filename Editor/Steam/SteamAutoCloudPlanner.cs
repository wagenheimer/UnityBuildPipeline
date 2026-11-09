using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Wagenheimer.BuildPipeline.Editor
{
    public enum SteamAutoCloudPlatform
    {
        Windows = 0,
        Mac = 1,
        Linux = 2
    }

    /// <summary>One row of the Steamworks Auto-Cloud table: the Windows Root Path or a Root Override.</summary>
    public sealed class SteamAutoCloudRow
    {
        public bool IsOverride;
        public string Root;
        public string OriginalRoot;
        public string Os;
        public string Subdirectory;
        public string FilePattern;
        public bool Recursive;

        public string Label => (IsOverride ? "Root Override" : "Root Path") + " · " + Os;
    }

    /// <summary>A finding from <see cref="SteamAutoCloudPlanner.Check"/>: warning (actionable) or info.</summary>
    public sealed class SteamAutoCloudIssue
    {
        public string Code;
        public bool IsWarning;
        public string Message;

        public override string ToString() => (IsWarning ? "[WARN] " : "[INFO] ") + Message;
    }

    /// <summary>
    /// Builds the Steamworks Auto-Cloud configuration for a cross-platform save and validates that the declared
    /// folders are deterministic.
    /// <para>
    /// Steamworks model (see https://partner.steamgames.com/doc/features/cloud, "Example: Configuring Auto-Cloud for
    /// Unity Applications"): <b>one</b> Root Path (Windows) with OS restriction [Cross-Platform], plus one Root
    /// Override per other OS pointing at the Windows root with "Replace Path" enabled — the override's
    /// Add/Replace Path is the physical folder used on that OS.
    /// </para>
    /// <para>
    /// Extra roots for macOS/Linux must NOT be declared: each root is its own cloud namespace, so two roots split
    /// the same save in the cloud instead of sharing it.
    /// </para>
    /// </summary>
    public static class SteamAutoCloudPlanner
    {
        public const string WindowsRoot = "WinAppDataRoaming";
        public const string MacRoot = "MacAppSupport";
        public const string LinuxRoot = "LinuxHome";

        public const string WindowsRootLabel = "%APPDATA%";
        public const string MacRootLabel = "~/Library/Application Support";
        public const string LinuxRootLabel = "~";

        /// <summary>Folder written as "a/b/c": trims, converts '\' to '/' and drops leading/trailing separators.</summary>
        public static string Normalize(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return "";

            var normalized = folder.Trim().Replace('\\', '/');
            while (normalized.Contains("//"))
                normalized = normalized.Replace("//", "/");

            return normalized.Trim('/');
        }

        /// <summary>Last segment of the folder ("Green Sauce Games/My Game" → "My Game").</summary>
        public static string Leaf(string folder)
        {
            var normalized = Normalize(folder);
            if (normalized.Length == 0)
                return "";

            var slash = normalized.LastIndexOf('/');
            return slash < 0 ? normalized : normalized.Substring(slash + 1);
        }

        /// <summary>The one Root Path + the Root Overrides for macOS and Linux, in table order.</summary>
        public static List<SteamAutoCloudRow> Rows(SteamAutoCloudConfig cfg)
        {
            var pattern = string.IsNullOrWhiteSpace(cfg?.filePattern) ? "*.sav" : cfg.filePattern.Trim();
            var recursive = cfg == null || cfg.recursive;

            return new List<SteamAutoCloudRow>
            {
                new SteamAutoCloudRow
                {
                    Root = WindowsRoot,
                    Os = "Windows",
                    Subdirectory = Normalize(cfg?.windowsSubdirectory),
                    FilePattern = pattern,
                    Recursive = recursive
                },
                new SteamAutoCloudRow
                {
                    IsOverride = true,
                    OriginalRoot = WindowsRoot,
                    Root = MacRoot,
                    Os = "macOS",
                    Subdirectory = Normalize(cfg?.macSubdirectory),
                    FilePattern = pattern,
                    Recursive = recursive
                },
                new SteamAutoCloudRow
                {
                    IsOverride = true,
                    OriginalRoot = WindowsRoot,
                    Root = LinuxRoot,
                    Os = "Linux + SteamOS",
                    Subdirectory = Normalize(cfg?.linuxSubdirectory),
                    FilePattern = pattern,
                    Recursive = recursive
                }
            };
        }

        /// <summary>
        /// Problems that make the save split (or never sync). Only warnings: a game may legitimately keep saves per
        /// platform — the point is to tell the developer what will happen.
        /// </summary>
        public static List<SteamAutoCloudIssue> Check(SteamAutoCloudConfig cfg, bool steamEnabled)
        {
            var issues = new List<SteamAutoCloudIssue>();
            if (cfg == null || !cfg.enabled)
                return issues;

            void Warn(string code, string message) => issues.Add(new SteamAutoCloudIssue { Code = code, IsWarning = true, Message = message });
            void Info(string code, string message) => issues.Add(new SteamAutoCloudIssue { Code = code, IsWarning = false, Message = message });

            if (!steamEnabled)
                Warn("autocloud-steam-off",
                    "Auto-Cloud is declared but the Steam integration is disabled: the Steamworks table and the checks only matter for a Steam build.");

            var windows = Normalize(cfg.windowsSubdirectory);
            var mac = Normalize(cfg.macSubdirectory);
            var linux = Normalize(cfg.linuxSubdirectory);

            if (windows.Length == 0)
                Warn("autocloud-windows-empty", "Declare the Windows save folder (relative to %APPDATA%). It is the Root Path and therefore the cloud namespace.");
            if (mac.Length == 0)
                Warn("autocloud-mac-empty", "Declare the macOS save folder (relative to ~/Library/Application Support).");
            if (linux.Length == 0)
                Warn("autocloud-linux-empty", "Declare the Linux save folder (relative to ~).");

            var pattern = (cfg.filePattern ?? "").Trim();
            if (pattern.Length == 0)
                Warn("autocloud-pattern-empty", "The file pattern is empty: Steam would sync nothing.");
            else if (pattern == "*" || pattern == "*.*")
                Warn("autocloud-pattern-broad", $"Pattern '{pattern}' syncs the whole folder (settings and caches included). Use a narrow mask such as *.sav.");

            if (windows.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0)
                Warn("autocloud-windows-invalid",
                    $"The Windows folder '{windows}' has characters Windows cannot create (: < > \" | ? *). It is the cloud namespace, so it must be valid on every OS.");

            var leaves = new[] { Leaf(windows), Leaf(mac), Leaf(linux) }
                .Where(leaf => leaf.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (leaves.Length > 1)
                Info("autocloud-leaf-mismatch",
                    $"The folder name differs per OS ({string.Join(", ", leaves)}). It works with 'Replace path', but the same leaf on all three keeps the game code and the Steamworks table in sync.");

            // Unity's macOS Player path flips between "unity.<company>.<game>" and "<company>/<game>" depending on
            // whether that folder already exists, so the same player can read/write two different folders.
            if (mac.StartsWith("unity.", StringComparison.OrdinalIgnoreCase))
                Warn("autocloud-mac-persistent-data",
                    $"'{mac}' is Unity's default macOS persistentDataPath. Unity alternates it with '<company>/<game>' on machines where that folder exists, which splits the save in the cloud. Build the macOS path explicitly (README ▸ Steam Auto-Cloud).");
            else if (mac.Length > 0 && !mac.Contains("/") && mac.Count(c => c == '.') >= 2)
                Warn("autocloud-mac-bundle-id",
                    $"'{mac}' looks like a bundle identifier (what Application.persistentDataPath returns in some builds). Prefer a fixed '<company>/<game>' folder so the path does not depend on the build or Unity version.");

            if (mac.StartsWith("Caches", StringComparison.OrdinalIgnoreCase) || mac.Contains("/Caches/"))
                Warn("autocloud-mac-caches", "The macOS folder is inside ~/Library/Caches, which macOS can purge at any moment. Use Application Support.");

            if (linux.Length > 0 && !linux.StartsWith(".config/unity3d/", StringComparison.OrdinalIgnoreCase))
                Info("autocloud-linux-path",
                    $"'{linux}' is not Unity's default Linux path (.config/unity3d/<company>/<game>). Make sure the game really writes there.");

            return issues;
        }

        /// <summary>Preview of the physical folder (with the Steamworks root shown as %APPDATA% / ~).</summary>
        public static string PhysicalPath(SteamAutoCloudConfig cfg, SteamAutoCloudPlatform platform)
        {
            switch (platform)
            {
                case SteamAutoCloudPlatform.Mac:
                    return Join(MacRootLabel, Normalize(cfg?.macSubdirectory));
                case SteamAutoCloudPlatform.Linux:
                    return Join(LinuxRootLabel, Normalize(cfg?.linuxSubdirectory));
                default:
                    return Join(WindowsRootLabel, Normalize(cfg?.windowsSubdirectory));
            }
        }

        /// <summary>Real folder on this machine (used by the panel to probe the save files).</summary>
        public static string LocalFolder(
            SteamAutoCloudConfig cfg, SteamAutoCloudPlatform platform,
            string userProfile = null, string appData = null)
        {
            var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roaming = appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            switch (platform)
            {
                case SteamAutoCloudPlatform.Mac:
                    return Path.Combine(home, "Library", "Application Support", ToPlatformPath(cfg?.macSubdirectory));
                case SteamAutoCloudPlatform.Linux:
                    return Path.Combine(home, ToPlatformPath(cfg?.linuxSubdirectory));
                default:
                    return Path.Combine(roaming, ToPlatformPath(cfg?.windowsSubdirectory));
            }
        }

        /// <summary>Copy-paste text of the whole Steamworks configuration (Root Path + Root Overrides + test steps).</summary>
        public static string SteamworksTable(SteamAutoCloudConfig cfg)
        {
            var rows = Rows(cfg);
            var root = rows[0];

            var builder = new StringBuilder();
            builder.AppendLine("Steamworks ▸ App Admin ▸ Steam Cloud ▸ Auto-Cloud");
            builder.AppendLine();
            builder.AppendLine("ROOT PATH (one row)");
            builder.AppendLine($"  Root: {root.Root}");
            builder.AppendLine($"  Subdirectory: {root.Subdirectory}");
            builder.AppendLine($"  Pattern: {root.FilePattern}   |   Recursive: {(root.Recursive ? "yes" : "no")}");
            builder.AppendLine("  OS restriction: Cross-Platform (Multiplataforma)");
            builder.AppendLine("  Cross-platform sync: Yes (Sim)");
            builder.AppendLine();
            builder.AppendLine("ROOT OVERRIDES (one per other OS)");
            foreach (var row in rows.Where(r => r.IsOverride))
            {
                builder.AppendLine($"  Original root: {row.OriginalRoot}   |   OS: {row.Os}   |   New root: {row.Root}");
                builder.AppendLine($"  Add/Replace path: {row.Subdirectory}");
                builder.AppendLine("  Replace path: yes (sim)");
            }

            builder.AppendLine();
            builder.AppendLine("PHYSICAL FOLDERS");
            builder.AppendLine($"  Windows: {PhysicalPath(cfg, SteamAutoCloudPlatform.Windows)}");
            builder.AppendLine($"  macOS:   {PhysicalPath(cfg, SteamAutoCloudPlatform.Mac)}");
            builder.AppendLine($"  Linux:   {PhysicalPath(cfg, SteamAutoCloudPlatform.Linux)}");
            builder.AppendLine();
            builder.AppendLine("TEST: steam://open/console → testappcloudpaths <AppID> → set_spew_level 4 4");
            return builder.ToString();
        }

        private static string Join(string root, string sub) =>
            string.IsNullOrEmpty(sub) ? root + "/<not set>" : root + "/" + sub;

        private static string ToPlatformPath(string folder) =>
            Normalize(folder).Replace('/', Path.DirectorySeparatorChar);
    }
}
