using System.IO;
using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class CleanupObsoleteFilesStep : IPostBuildStep
    {
        public int Order => 20;

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            if (context.Platform != PlatformType.Windows64)
                return true;

            var dir = Path.GetDirectoryName(context.ResolvedOutputFilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return true;

            // Delete crash handlers
            var crash32 = Path.Combine(dir, "UnityCrashHandler32.exe");
            if (File.Exists(crash32)) File.Delete(crash32);

            var crash64 = Path.Combine(dir, "UnityCrashHandler64.exe");
            if (File.Exists(crash64)) File.Delete(crash64);

            // Delete obsolete backup folders if any
            var parentDir = Path.GetDirectoryName(dir);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                var obsoleteDirs = Directory.GetDirectories(parentDir, "*_BackUpThisFolder_ButDontShipItWithYourGame");
                foreach (var od in obsoleteDirs)
                {
                    try { Directory.Delete(od, true); } catch { }
                }
            }

            return true;
        }
    }
}
