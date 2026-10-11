using System.IO;
using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class CleanupObsoleteFilesStep : IPostBuildStep
    {
        public int Order => 20;

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            var isSteam = context.PublisherProfile != null && context.PublisherProfile.IsSteamProfile;
            if (!isSteam)
            {
                CleanupNonSteamBinaries(context);
            }

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

        private static void CleanupNonSteamBinaries(BuildContext context)
        {
            try
            {
                var outputPath = context.ResolvedOutputFilePath;
                if (string.IsNullOrEmpty(outputPath)) return;

                if (context.Platform == PlatformType.macOS)
                {
                    string appBundle = outputPath.EndsWith(".app", System.StringComparison.OrdinalIgnoreCase) && Directory.Exists(outputPath)
                        ? outputPath
                        : null;

                    if (appBundle == null)
                    {
                        var dir = Directory.Exists(outputPath) ? outputPath : Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            var apps = Directory.GetDirectories(dir, "*.app");
                            if (apps.Length > 0) appBundle = apps[0];
                        }
                    }

                    if (!string.IsNullOrEmpty(appBundle) && Directory.Exists(appBundle))
                    {
                        var pluginsDir = Path.Combine(appBundle, "Contents", "PlugIns");
                        if (Directory.Exists(pluginsDir))
                        {
                            var steamBundle = Path.Combine(pluginsDir, "steam_api.bundle");
                            if (Directory.Exists(steamBundle))
                            {
                                Directory.Delete(steamBundle, true);
                                context.Log("🧹 Removed steam_api.bundle from non-Steam macOS build.");
                            }
                            var dylib = Path.Combine(pluginsDir, "libsteam_api.dylib");
                            if (File.Exists(dylib))
                            {
                                File.Delete(dylib);
                                context.Log("🧹 Removed libsteam_api.dylib from non-Steam macOS build.");
                            }
                        }
                    }
                }
                else if (context.Platform == PlatformType.Windows64)
                {
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        foreach (var dataDir in Directory.GetDirectories(dir, "*_Data"))
                        {
                            var pluginsDir = Path.Combine(dataDir, "Plugins");
                            if (!Directory.Exists(pluginsDir)) continue;

                            foreach (var dllName in new[] { "steam_api64.dll", "steam_api.dll" })
                            {
                                var direct = Path.Combine(pluginsDir, dllName);
                                if (File.Exists(direct)) { File.Delete(direct); context.Log($"🧹 Removed {dllName} from non-Steam Windows build."); }
                                var x64 = Path.Combine(pluginsDir, "x86_64", dllName);
                                if (File.Exists(x64)) { File.Delete(x64); context.Log($"🧹 Removed {dllName} from non-Steam Windows build."); }
                                var x86 = Path.Combine(pluginsDir, "x86", dllName);
                                if (File.Exists(x86)) { File.Delete(x86); context.Log($"🧹 Removed {dllName} from non-Steam Windows build."); }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                context.LogWarning($"⚠️ Non-Steam binary cleanup warning: {ex.Message}");
            }
        }
    }
}
