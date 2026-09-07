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

            if (context.Platform != PlatformType.Windows64 && context.Platform != PlatformType.macOS)
                return true;

            var splashFolder = context.Config != null ? context.Config.splashMasterFolder : "E:/Games/Unity/games/";
            var sourceSplash = Path.Combine(splashFolder, $"splash_{context.Publisher}.jpg");

            if (!File.Exists(sourceSplash))
            {
                context.LogWarning($"Publisher splash image not found at: {sourceSplash}");
                return true;
            }

            var streamingAssetsPath = "";
            if (context.Platform == PlatformType.Windows64)
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
    }
}
