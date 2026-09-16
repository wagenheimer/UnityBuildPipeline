using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    [Serializable]
    public class BuildHistoryEntry
    {
        public string timeStamp = "";
        public string projectName = "";
        public string platform = "";
        public string publisher = "";
        public string language = "";
        public string bundleId = "";
        public bool cheatMode = false;
        public bool developmentBuild = false;
        public string outputPath = "";
        public bool success = false;
        public double durationSeconds = 0;
        public long totalSize = 0;
        public int totalErrors = 0;

        public DateTime Time
        {
            get
            {
                DateTime.TryParse(timeStamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt);
                return dt;
            }
        }

        public bool IsAab => !string.IsNullOrEmpty(outputPath) && outputPath.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);
        public bool IsApk => !string.IsNullOrEmpty(outputPath) && outputPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public class BuildHistoryData
    {
        public List<BuildHistoryEntry> entries = new List<BuildHistoryEntry>();
    }

    public static class BuildHistory
    {
        private const int MaxEntries = 30;
        private static BuildHistoryData _data;
        private static string HistoryPath => Path.Combine("Library", "BuildPipelineHistory.json");

        public static List<BuildHistoryEntry> Entries
        {
            get
            {
                if (_data == null) Load();
                return _data.entries;
            }
        }

        public static BuildHistoryEntry LastSuccessful
        {
            get
            {
                foreach (var e in Entries)
                    if (e.success) return e;
                return null;
            }
        }

        private static void Load()
        {
            _data = new BuildHistoryData();
            try
            {
                if (File.Exists(HistoryPath))
                    _data = JsonUtility.FromJson<BuildHistoryData>(File.ReadAllText(HistoryPath)) ?? new BuildHistoryData();
                if (_data.entries == null) _data.entries = new List<BuildHistoryEntry>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Failed to load build history: {ex.Message}");
                _data = new BuildHistoryData();
            }
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(HistoryPath, JsonUtility.ToJson(_data, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Failed to save build history: {ex.Message}");
            }
        }

        public static void Record(BuildContext context, BuildResultSummary result, string bundleId)
        {
            if (_data == null) Load();

            var entry = new BuildHistoryEntry
            {
                timeStamp = DateTime.Now.ToString("o"),
                projectName = context.ProjectName,
                platform = context.Platform.ToString(),
                publisher = context.Publisher.ToString(),
                language = context.Language.ToString(),
                bundleId = bundleId ?? "",
                cheatMode = context.CheatMode,
                developmentBuild = context.DevelopmentBuild,
                outputPath = context.ResolvedOutputFilePath,
                success = result.Success,
                durationSeconds = result.Duration.TotalSeconds,
                totalSize = result.TotalSize,
                totalErrors = result.TotalErrors
            };

            _data.entries.Insert(0, entry);
            if (_data.entries.Count > MaxEntries)
                _data.entries.RemoveRange(MaxEntries, _data.entries.Count - MaxEntries);
            Save();
        }

        public static void Remove(BuildHistoryEntry entry)
        {
            if (_data == null) Load();
            _data.entries.Remove(entry);
            Save();
        }

        public static void Clear()
        {
            if (_data == null) Load();
            _data.entries.Clear();
            Save();
        }

        public static void OpenFolder(BuildHistoryEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.outputPath)) return;

            var path = entry.outputPath;
            if (File.Exists(path))
                EditorUtility.RevealInFinder(path);
            else if (Directory.Exists(path))
                EditorUtility.RevealInFinder(path);
            else
                EditorUtility.DisplayDialog("Build History", $"Path not found:\n{path}", "OK");
        }

        public static bool CanRun(BuildHistoryEntry entry)
        {
            if (entry == null || !entry.success || string.IsNullOrEmpty(entry.outputPath)) return false;

            switch (entry.platform)
            {
                case "Windows64":
                case "Linux64":
                    return File.Exists(entry.outputPath);
                case "macOS":
                    return Directory.Exists(entry.outputPath);
                case "Android":
                    return entry.IsApk;
                default:
                    return false;
            }
        }

        public static void Run(BuildHistoryEntry entry)
        {
            if (!CanRun(entry)) return;

            try
            {
                switch (entry.platform)
                {
                    case "Windows64":
                    case "Linux64":
                        Process.Start(entry.outputPath);
                        break;

                    case "macOS":
                        Process.Start("open", entry.outputPath);
                        break;

                    case "Android":
                        if (entry.IsApk)
                            InstallAndLaunchApk(entry);
                        break;
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Run Build", $"Failed to run build:\n{ex.Message}", "OK");
            }
        }

        private static string FindAdb()
        {
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(EditorApplication.applicationPath, "../PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe")),
                Path.Combine(System.Environment.GetEnvironmentVariable("ANDROID_HOME") ?? "", "platform-tools", "adb.exe"),
                Path.Combine(System.Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? "", "platform-tools", "adb.exe"),
                "adb"
            };

            foreach (var c in candidates)
            {
                if (c == "adb" || File.Exists(c)) return c;
            }
            return "adb";
        }

        private static void InstallAndLaunchApk(BuildHistoryEntry entry)
        {
            var adb = FindAdb();

            EditorUtility.DisplayProgressBar("Run Build", $"Installing APK on device ({adb})...", 0.5f);
            try
            {
                var output = RunCommand(adb, $"install -r \"{entry.outputPath}\"");
                if (output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Run Build (APK)",
                        $"APK install did not report success.\n\nCommand: adb install -r\nOutput:\n{output}\n\n" +
                        "Check that a device is connected and USB debugging is enabled.", "OK");
                    return;
                }

                var launchOut = "";
                if (!string.IsNullOrEmpty(entry.bundleId))
                    launchOut = RunCommand(adb, $"shell monkey -p {entry.bundleId} 1");

                EditorUtility.ClearProgressBar();
                Debug.Log($"[BuildPipeline] APK installed & launched. bundleId: {entry.bundleId}\n{output}\n{launchOut}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string RunCommand(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                return $"{stdout}\n{stderr}".Trim();
            }
        }
    }
}
