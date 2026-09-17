using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Post-build: gathers the debug artifacts the Play Store expects alongside the App Bundle and
    /// leaves them next to the .aab with deterministic names, so CI (AppDeployHub.Forge) can upload
    /// them without having to guess paths:
    ///  - <c>&lt;aab&gt;.symbols.zip</c>  → native symbols (avoids the "contains native code and you
    ///    haven't uploaded debug symbols" warning). Unity generates it as
    ///    <c>&lt;aabbase&gt;-&lt;version&gt;-v&lt;bundleVersionCode&gt;-IL2CPP.symbols.zip</c>.
    ///  - <c>&lt;aab&gt;_mapping.txt</c>   → R8/ProGuard deobfuscation file, when release Minify is
    ///    on (avoids the "no deobfuscation file associated" warning). Without minify there is no
    ///    mapping.txt and nothing is copied.
    /// The paths end up in <see cref="BuildContext.ExtraData"/> and are exposed in unity-manifest.json.
    /// </summary>
    public class ExportAndroidSymbolsStep : IPostBuildStep
    {
        public const string SymbolsZipKey = "androidSymbolsZip";
        public const string MappingFileKey = "androidMappingFile";
        public const string SymbolsEmbeddedKey = "androidSymbolsEmbedded";

        public int Order => 25;

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            if (context.Platform != PlatformType.Android)
                return true;

            var artifact = context.ResolvedOutputFilePath;
            if (string.IsNullOrEmpty(artifact) || !File.Exists(artifact))
                return true;

            var aabDir = Path.GetDirectoryName(artifact);
            if (string.IsNullOrEmpty(aabDir) || !Directory.Exists(aabDir))
                return true;

            var baseName = Path.GetFileNameWithoutExtension(artifact);
            if (string.IsNullOrEmpty(baseName))
                return true;

            var embedded = artifact.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);
            context.ExtraData[SymbolsEmbeddedKey] = embedded;

            var symbolsZip = ExportSymbolsZip(context, aabDir, baseName);
            if (!string.IsNullOrEmpty(symbolsZip))
                context.ExtraData[SymbolsZipKey] = symbolsZip;

            var mapping = ExportMappingFile(context, aabDir, baseName);
            if (!string.IsNullOrEmpty(mapping))
                context.ExtraData[MappingFileKey] = mapping;

            return true;
        }

        private static string ExportSymbolsZip(BuildContext context, string aabDir, string baseName)
        {
            try
            {
                var source = FindNewestSymbolsZip(aabDir, baseName);
                if (string.IsNullOrEmpty(source))
                {
                    context.LogWarning("Native symbols: no *.symbols.zip found next to the artifact. " +
                                       "Check Debug Symbols (Public/Debugging) in the Android build profile.");
                    return null;
                }

                var dest = Path.Combine(aabDir, baseName + ".symbols.zip");
                if (!string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(source, dest, true);
                    context.Log($"Native symbols exported: {dest}");
                }
                else
                {
                    context.Log($"Native symbols already in place: {dest}");
                }

                return dest;
            }
            catch (Exception ex)
            {
                context.LogWarning($"Failed to export native symbols: {ex.Message}");
                return null;
            }
        }

        private static string FindNewestSymbolsZip(string aabDir, string baseName)
        {
            var roots = new List<string>
            {
                aabDir,
                Path.Combine(aabDir, baseName + "_BackUpThisFolder_ButDontShipItWithYourGame")
            };

            var candidates = new List<string>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    // Prefer the zip named after the artifact itself; then fall back to any *.symbols.zip.
                    candidates.AddRange(Directory.GetFiles(root, baseName + "*.symbols.zip", SearchOption.TopDirectoryOnly));
                    candidates.AddRange(Directory.GetFiles(root, "*.symbols.zip", SearchOption.TopDirectoryOnly));
                    candidates.AddRange(Directory.GetFiles(root, "*.symbols.zip", SearchOption.AllDirectories));
                }
                catch
                {
                    // Backup folders can have long paths/permission issues — proceed with what was found.
                }
            }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static string ExportMappingFile(BuildContext context, string aabDir, string baseName)
        {
            try
            {
                var source = FindNewestMappingFile(aabDir);
                if (string.IsNullOrEmpty(source))
                    return null;

                var dest = Path.Combine(aabDir, baseName + "_mapping.txt");
                if (!string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(source, dest, true);
                    context.Log($"Deobfuscation file (mapping.txt) exported: {dest}");
                }

                return dest;
            }
            catch (Exception ex)
            {
                context.LogWarning($"Failed to export mapping.txt: {ex.Message}");
                return null;
            }
        }

        private static string FindNewestMappingFile(string aabDir)
        {
            var candidates = new List<string>();

            // Gradle project generated by Unity during the build (R8/ProGuard writes here).
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                var gradleOut = Path.Combine(projectRoot, "Temp", "gradleOut");
                if (Directory.Exists(gradleOut))
                {
                    try { candidates.AddRange(Directory.GetFiles(gradleOut, "mapping.txt", SearchOption.AllDirectories)); }
                    catch { /* ignore unreadable folders */ }
                }
            }

            try { candidates.AddRange(Directory.GetFiles(aabDir, "mapping.txt", SearchOption.AllDirectories)); }
            catch { }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
    }
}
