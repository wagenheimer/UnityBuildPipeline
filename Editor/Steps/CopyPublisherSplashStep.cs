using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class CopyPublisherSplashStep : IPostBuildStep
    {
        public int Order => 10;

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            if (context.PublisherProfile == null || !context.PublisherProfile.requiresSplash)
                return true;

            if (!context.Platform.IsWindows() && context.Platform != PlatformType.macOS)
                return true;

            // CW_SPLASH_FOLDER: splash library synced by the AppDeployHub Forge agent (Forge → Splashes). Falls back to the
            // local master folder from the config when building on the developer machine.
            var forgeFolder = System.Environment.GetEnvironmentVariable("CW_SPLASH_FOLDER");
            var splashFolder = !string.IsNullOrWhiteSpace(forgeFolder) && Directory.Exists(forgeFolder)
                ? forgeFolder
                : (context.Config != null ? context.Config.splashMasterFolder : "E:/Games/Unity/games/");
            var sourceSplash = FindSplash(splashFolder, context.Publisher.ToString());

            if (sourceSplash == null)
            {
                context.LogWarning($"Publisher splash image not found: splash_{context.Publisher}.jpg/.png in {splashFolder}" +
                                   (string.IsNullOrWhiteSpace(forgeFolder) ? "" : " (upload it in AppDeployHub Forge → Splashes)"));
                return true;
            }

            var streamingAssetsPath = "";
            if (context.Platform.IsWindows())
            {
                var exeDir = Path.GetDirectoryName(context.ResolvedOutputFilePath);
                var exeName = Path.GetFileNameWithoutExtension(context.ResolvedOutputFilePath);
                streamingAssetsPath = Path.Combine(exeDir, $"{exeName}_Data", "StreamingAssets");
            }
            else if (context.Platform == PlatformType.macOS)
            {
                streamingAssetsPath = Path.Combine(context.ResolvedOutputFilePath, "Contents", "Resources", "Data", "StreamingAssets");
            }

            if (!string.IsNullOrEmpty(streamingAssetsPath))
            {
                if (!Directory.Exists(streamingAssetsPath))
                    Directory.CreateDirectory(streamingAssetsPath);

                var targetFile = Path.Combine(streamingAssetsPath, "splash1.jpg");
                FileUtil.CopyFileOrDirectory(sourceSplash, targetFile);
                context.Log($"Copied Publisher Splash -> {targetFile}");
            }

            return true;
        }

        /// <summary>splash_{publisher}.jpg (or .png), ignoring case — the macOS/Linux agents may run on case-sensitive volumes.</summary>
        private static string FindSplash(string folder, string publisher)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;

            foreach (var extension in new[] { ".jpg", ".jpeg", ".png" })
            {
                var wanted = $"splash_{publisher}{extension}";
                foreach (var file in Directory.GetFiles(folder))
                {
                    if (string.Equals(Path.GetFileName(file), wanted, System.StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }
            return null;
        }
    }
}
