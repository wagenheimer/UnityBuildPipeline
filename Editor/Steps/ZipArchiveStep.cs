using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityEditor.Build.Reporting;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class ZipArchiveStep : IPostBuildStep
    {
        public int Order => 30;

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            var shouldZip = (context.PublisherProfile != null && context.PublisherProfile.zipAfterBuild)
                || (context.Config != null && context.Config.enableZipArchiving);

            if (!shouldZip)
                return true;

            var sourceDir = Path.GetDirectoryName(context.ResolvedOutputFilePath);
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
                return true;

            var folderName = Path.GetFileName(sourceDir);
            var zipTemplate = context.PublisherProfile != null && !string.IsNullOrEmpty(context.PublisherProfile.zipDestinationTemplate)
                ? context.PublisherProfile.zipDestinationTemplate
                : @"E:\Documentos\Dropbox\Builds\{ProjectName}\{Publisher}\";

            var zipDir = zipTemplate
                .Replace("{ProjectName}", context.ProjectName)
                .Replace("{Publisher}", context.Publisher.ToString());

            if (!Directory.Exists(zipDir))
                Directory.CreateDirectory(zipDir);

            var zipPath = Path.Combine(zipDir, $"{folderName}.zip");

            // Use 7-Zip if configured and available
            if (context.Config != null && context.Config.useExternalSevenZip && File.Exists(context.Config.sevenZipExecutable))
            {
                try
                {
                    var args = $@"a -tzip ""{zipPath}"" ""{sourceDir}"" {BuildZipFilter.SevenZipExcludeArgs}";
                    var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = context.Config.sevenZipExecutable,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    proc?.WaitForExit();
                    context.Log($"7-Zip Archive created: {zipPath}");
                    return true;
                }
                catch (Exception ex)
                {
                    context.LogWarning($"7-Zip execution failed, falling back to .NET ZipFile: {ex.Message}");
                }
            }

            // Fallback to built-in .NET ZipFile
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                var skipped = CreateFilteredZip(sourceDir, zipPath);
                context.Log(skipped > 0
                    ? $"ZIP Archive created: {zipPath} ({skipped} non-shipping file(s) left out, e.g. *_BackUpThisFolder_ButDontShipItWithYourGame)"
                    : $"ZIP Archive created: {zipPath}");
            }
            catch (Exception ex)
            {
                context.LogError($"Failed to create zip archive: {ex.Message}");
            }

            return true;
        }

        /// <summary>Same layout as ZipFile.CreateFromDirectory (no base folder), minus <see cref="BuildZipFilter"/> entries.</summary>
        /// <returns>How many files were left out.</returns>
        private static int CreateFilteredZip(string sourceDir, string zipPath)
        {
            var skipped = 0;
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var relative = BuildZipFilter.RelativePath(sourceDir, file);
                    if (BuildZipFilter.IsExcluded(relative)) { skipped++; continue; }
                    archive.CreateEntryFromFile(file, relative.Replace('\\', '/'), CompressionLevel.Optimal);
                }
            }
            return skipped;
        }
    }
}
