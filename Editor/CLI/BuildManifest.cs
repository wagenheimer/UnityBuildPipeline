using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Machine-readable result of one headless build, written to the path given by <c>-manifest</c>.
    /// Consumed by external CI (AppDeployHub.Forge) instead of scraping the Unity log.
    /// </summary>
    [Serializable]
    public class BuildManifestEntry
    {
        public bool success;
        public string result;          // BuildResult enum name
        public string platform;        // PlatformType enum name
        public string profileId;
        public string defines;         // ';'-joined extra defines applied
        public string version;
        public string buildNumber;
        public string outputDirectory;
        public string artifactPath;    // .aab/.apk/.exe/.app or the WebGL/Xcode folder
        public string xcodeProjectPath; // set only for iOS (== artifactPath folder)
        public string symbolsZipPath;   // Android: native symbols (.symbols.zip) beside the artifact
        public string mappingPath;      // Android: R8/ProGuard mapping.txt (only when Minify is on)
        public bool symbolsEmbedded;    // Android .aab: native symbols embedded in the bundle itself
        public long sizeBytes;
        public int errors;
        public int warnings;
        public double durationSeconds;
        public string errorMessage;

        public static BuildManifestEntry From(BuildContext context, BuildResultSummary summary)
        {
            var entry = new BuildManifestEntry
            {
                success = summary.Success,
                result = summary.Result.ToString(),
                platform = context.Platform.ToString(),
                profileId = string.IsNullOrEmpty(context.ProfileId) ? context.Publisher.ToString() : context.ProfileId,
                defines = context.ScriptingDefines != null ? string.Join(";", context.ScriptingDefines) : "",
                version = ResolveVersion(context),
                buildNumber = ResolveBuildNumber(context),
                outputDirectory = context.ResolvedOutputDirectory,
                artifactPath = context.ResolvedOutputFilePath,
                symbolsZipPath = GetExtraString(context, ExportAndroidSymbolsStep.SymbolsZipKey),
                mappingPath = GetExtraString(context, ExportAndroidSymbolsStep.MappingFileKey),
                symbolsEmbedded = GetExtraBool(context, ExportAndroidSymbolsStep.SymbolsEmbeddedKey),
                sizeBytes = (long)summary.TotalSize,
                errors = summary.TotalErrors,
                warnings = summary.TotalWarnings,
                durationSeconds = summary.Duration.TotalSeconds,
                errorMessage = summary.ErrorMessage ?? ""
            };

            if (context.Platform == PlatformType.iOS)
                entry.xcodeProjectPath = context.ResolvedOutputFilePath;

            return entry;
        }

        private static string GetExtraString(BuildContext context, string key)
        {
            return context.ExtraData.TryGetValue(key, out var value) && value is string s ? s : "";
        }

        private static bool GetExtraBool(BuildContext context, string key)
        {
            return context.ExtraData.TryGetValue(key, out var value) && value is bool b && b;
        }

        private static string ResolveVersion(BuildContext context)
        {
            if (context.Config != null && context.Config.gameConfig != null)
                return context.Config.gameConfig.GameVersion.GameVersionAsText;
            return PlayerSettings.bundleVersion;
        }

        private static string ResolveBuildNumber(BuildContext context)
        {
            switch (context.Platform)
            {
                case PlatformType.Android: return PlayerSettings.Android.bundleVersionCode.ToString();
                case PlatformType.iOS: return PlayerSettings.iOS.buildNumber;
                case PlatformType.macOS: return PlayerSettings.macOS.buildNumber;
                default: return "";
            }
        }
    }

    [Serializable]
    public class BuildManifest
    {
        public string schema = "unity-build-pipeline/manifest@1";
        public string generatedAtUtc = DateTime.UtcNow.ToString("o");
        public bool allSuccess = true;
        public List<BuildManifestEntry> builds = new List<BuildManifestEntry>();

        public void Add(BuildManifestEntry entry)
        {
            builds.Add(entry);
            if (!entry.success) allSuccess = false;
        }

        /// <summary>Writes the manifest to <paramref name="path"/> (pretty JSON). Never throws.</summary>
        public void WriteTo(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var full = Path.GetFullPath(path);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(full, JsonUtility.ToJson(this, true));
                Debug.Log($"[BuildCLI] Wrote build manifest: {full} ({builds.Count} build(s), allSuccess={allSuccess})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildCLI] Failed to write build manifest to '{path}': {ex.Message}");
            }
        }
    }
}
