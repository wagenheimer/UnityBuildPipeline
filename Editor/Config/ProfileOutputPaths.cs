using System;
using System.IO;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Single place that turns a profile's <see cref="PublisherProfile.outputSubfolder"/> into a real folder.
    /// Supports tokens: {Publisher}, {GameFolder}, {CleanGameName}, {ProjectName}, {Platform}.
    /// Steam desktop profiles automatically resolve to:
    /// [Root]/Publishers/Steam/{GameFolder}/{Platform} (Windows, Linux, Mac),
    /// placing files directly into the platform folder without extra dated subfolders.
    /// </summary>
    public static class ProfileOutputPaths
    {
        public const string WindowsFolder = "Windows";
        public const string LinuxFolder = "Linux";
        public const string MacFolder = "Mac";

        /// <summary>True when this profile gets an automatic platform subfolder (Steam desktop builds).</summary>
        public static bool UsesPlatformSubfolder(PublisherProfile profile, PlatformType platform) =>
            profile != null &&
            profile.IsSteamProfile &&
            platform is PlatformType.Windows64 or PlatformType.Linux64 or PlatformType.macOS;

        public static string PlatformFolder(PlatformType platform) => platform switch
        {
            PlatformType.Windows64 => WindowsFolder,
            PlatformType.Linux64 => LinuxFolder,
            PlatformType.macOS => MacFolder,
            _ => ""
        };

        /// <summary>
        /// Effective relative subfolder under the build output root: replaces tokens,
        /// strips redundant leading 'Builds/' when the root already points to a Builds folder,
        /// and appends Windows/Linux/Mac for Steam desktop profiles.
        /// </summary>
        public static string EffectiveSubfolder(PublisherProfile profile, PlatformType platform, ProjectBuildConfig config = null)
        {
            string declared = profile != null ? profile.outputSubfolder : "Publishers/";
            string publisher = (profile != null ? profile.publisher : Publisher.Default).ToString();
            string gameFolder = config != null ? config.GetEffectiveGameFolderName() : "";
            string projectName = config != null ? config.projectName : "";

            string subfolder = (declared ?? "").Replace("{Publisher}", publisher);
            if (!string.IsNullOrEmpty(gameFolder))
            {
                subfolder = subfolder.Replace("{GameFolder}", gameFolder)
                                     .Replace("{CleanGameName}", gameFolder);
            }
            if (!string.IsNullOrEmpty(projectName))
            {
                subfolder = subfolder.Replace("{ProjectName}", projectName);
            }

            subfolder = NormalizeSubfolder(subfolder, config);

            if (!UsesPlatformSubfolder(profile, platform))
                return subfolder;

            // For Steam desktop profiles, ensure the game folder is inserted if not present
            if (!string.IsNullOrEmpty(gameFolder) &&
                !subfolder.Contains(gameFolder, StringComparison.OrdinalIgnoreCase) &&
                (subfolder.EndsWith("Steam", StringComparison.OrdinalIgnoreCase) ||
                 subfolder.EndsWith("Steam/", StringComparison.OrdinalIgnoreCase) ||
                 subfolder.EndsWith("Steam\\", StringComparison.OrdinalIgnoreCase)))
            {
                subfolder = AppendFolder(subfolder, gameFolder);
            }

            string platformFolder = PlatformFolder(platform);
            if (string.IsNullOrEmpty(platformFolder))
                return subfolder;

            if (subfolder.IndexOf("{Platform}", StringComparison.OrdinalIgnoreCase) >= 0)
                return subfolder.Replace("{Platform}", platformFolder, StringComparison.OrdinalIgnoreCase);

            return EndsWithFolder(subfolder, platformFolder)
                ? subfolder
                : AppendFolder(subfolder, platformFolder);
        }

        public static string NormalizeSubfolder(string subfolder, ProjectBuildConfig config = null)
        {
            if (string.IsNullOrEmpty(subfolder)) return "";
            var normalized = subfolder.Replace('\\', '/').Trim();

            // If the root already ends with 'Builds' or 'Builds/', strip leading 'Builds/' to prevent 'Builds/Builds'
            if (config != null && !string.IsNullOrEmpty(config.buildOutputRoot))
            {
                var cleanRoot = config.buildOutputRoot.Trim().TrimEnd('/', '\\');
                if (cleanRoot.EndsWith("Builds", StringComparison.OrdinalIgnoreCase) &&
                    (normalized.StartsWith("Builds/", StringComparison.OrdinalIgnoreCase) ||
                     normalized.Equals("Builds", StringComparison.OrdinalIgnoreCase)))
                {
                    normalized = normalized.Length > 7 ? normalized.Substring(7) : "";
                }
            }
            return normalized;
        }

        /// <summary>Absolute base folder this profile writes to.</summary>
        public static string ResolveFolder(ProjectBuildConfig config, PublisherProfile profile, PlatformType platform)
        {
            string root = config != null ? config.GetEffectiveBuildOutputRoot(platform) : "";
            string subfolder = EffectiveSubfolder(profile, platform, config);
            try
            {
                return Path.GetFullPath(Path.Combine(root, subfolder));
            }
            catch
            {
                return Path.Combine(root, subfolder);
            }
        }

        /// <summary>Example of where the next build will land, using the current version/date/language.</summary>
        public static BuildOutputPreview ResolveNextBuild(
            ProjectBuildConfig config, PublisherProfile profile, PlatformType platform)
        {
            var preview = new BuildOutputPreview();
            if (config == null || profile == null)
                return preview;

            try
            {
                var context = BuildContextFactory.Create(config, profile, new BuildContextFactory.Options { Platform = platform });
                context.ResolveOutputPaths();
                preview.Folder = context.ResolvedOutputDirectory ?? "";
                preview.Artifact = context.ResolvedOutputFilePath ?? "";
            }
            catch
            {
                // The preview is best-effort: a malformed path must not break the profile editor.
            }

            return preview;
        }

        public sealed class BuildOutputPreview
        {
            public string Folder = "";
            public string Artifact = "";

            public bool HasArtifact =>
                !string.IsNullOrEmpty(Artifact) &&
                !string.Equals(Folder, Artifact, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EndsWithFolder(string path, string folder)
        {
            string value = (path ?? "").TrimEnd('/', '\\');
            int slash = value.LastIndexOfAny(new[] { '/', '\\' });
            string leaf = slash >= 0 ? value.Substring(slash + 1) : value;
            return string.Equals(leaf, folder, StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendFolder(string path, string folder)
        {
            string value = (path ?? "").TrimEnd('/', '\\');
            return value.Length == 0 ? folder : value + "/" + folder;
        }
    }
}
