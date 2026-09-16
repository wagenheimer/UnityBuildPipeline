using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

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
                totalSize = (long) result.TotalSize,
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
                    return entry.IsApk || entry.IsAab;
                default:
                    return false;
            }
        }

        public static void Run(BuildHistoryEntry entry)
        {
            if (!CanRun(entry)) return;

            var action = entry.platform switch
            {
                "Windows64" or "Linux64" => $"Run executable:\n{entry.outputPath}",
                "macOS" => $"Run app:\n{entry.outputPath}",
                "Android" when entry.IsApk =>
                    $"Install APK on connected device (adb install -r) and launch:\n{entry.outputPath}",
                "Android" when entry.IsAab =>
                    $"Install AAB on connected device (bundletool: generate device APKs + install) and launch:\n{entry.outputPath}",
                _ => entry.outputPath
            };

            var info = $"{action}\n\n" +
                       $"Publisher: {entry.publisher}  |  Language: {entry.language}" +
                       $"{(entry.cheatMode ? "  |  CHEAT" : "")}{(entry.developmentBuild ? "  |  DEV" : "")}";
            if (!EditorUtility.DisplayDialog("Run Build", info, "▶ RUN", "CANCEL")) return;

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
                        else if (entry.IsAab)
                            InstallAndLaunchAab(entry);
                        break;
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Run Build", $"Failed to run build:\n{ex.Message}", "OK");
            }
        }

        private static string FindJava()
        {
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(EditorApplication.applicationPath, "Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe")),
                Path.Combine(System.Environment.GetEnvironmentVariable("JAVA_HOME") ?? "", "bin", "java.exe"),
                "java"
            };

            foreach (var c in candidates)
            {
                if (c == "java" || File.Exists(c)) return c;
            }
            return "java";
        }

        /// <summary>
        /// Locates a usable bundletool.jar (one with an embedded aapt2, i.e. the official
        /// "bundletool-all" build): EditorPrefs override -> env var -> project folders ->
        /// Unity's bundled Android player -> .NET Android SDK packs -> Visual Studio Xamarin.
        /// Slim SDK-bundled jars (no aapt2 inside) are rejected with a warning. Returns null
        /// when nothing usable is found (caller can then download or prompt).
        /// </summary>
        private static string FindBundletool()
        {
            var explicitCandidates = new[]
            {
                EditorPrefs.GetString("BuildPipeline_BundletoolJar", ""),
                System.Environment.GetEnvironmentVariable("BUNDLETOOL_JAR") ?? "",
                Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? "", "Library", "bundletool.jar"),
                Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? "", "bundletool.jar")
            };

            foreach (var c in explicitCandidates)
            {
                if (!string.IsNullOrEmpty(c) && File.Exists(c) && JarHasAapt2(c, "explicit"))
                    return c;
            }

            // Common locations where bundletool.jar ships with other toolchains.
            var programFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
            var searchRoots = new[]
            {
                // Unity's Android player tools (some editor installs ship bundletool here)
                Path.GetFullPath(Path.Combine(EditorApplication.applicationPath, "Data/PlaybackEngines/AndroidPlayer")),
                // .NET Android SDK workloads (MAUI / Xamarin) each carry a copy under <version>/tools
                Path.Combine(programFiles, "dotnet", "packs", "Microsoft.Android.Sdk.Windows")
            };

            foreach (var root in searchRoots)
            {
                var found = GlobBundletool(root);
                if (found != null) return found;
            }

            // Visual Studio keeps one copy per edition under <edition>/MSBuild/Xamarin/Android -
            // scan only that subtree instead of the whole VS install (which is huge).
            var vsRoot = Path.Combine(programFiles, "Microsoft Visual Studio");
            if (Directory.Exists(vsRoot))
            {
                try
                {
                    foreach (var edition in Directory.GetDirectories(vsRoot))
                    {
                        var found = GlobBundletool(Path.Combine(edition, "MSBuild", "Xamarin", "Android"));
                        if (found != null) return found;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BuildPipeline] bundletool search failed in {vsRoot}: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>True when the jar embeds aapt2 (only the official bundletool-all build does).</summary>
        private static bool JarHasAapt2(string jarPath, string origin)
        {
            try
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(jarPath))
                {
                    foreach (var e in zip.Entries)
                    {
                        if (e.FullName.StartsWith("aapt2", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Could not inspect bundletool jar {jarPath}: {ex.Message}");
                return false;
            }

            Debug.LogWarning($"[BuildPipeline] {origin} bundletool.jar at {jarPath} is a slim build " +
                             "without embedded aapt2 - skipping it. bundletool-all.jar is required to build APKs.");
            return false;
        }

        private static string GlobBundletool(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;

            try
            {
                var jars = Directory.GetFiles(root, "bundletool*.jar", SearchOption.AllDirectories);
                if (jars.Length == 0) return null;

                // Prefer the newest copy; slim SDK builds are rejected by JarHasAapt2.
                var best = jars
                    .Select(j => new { Path = j, Modified = File.GetLastWriteTimeUtc(j) })
                    .OrderByDescending(j => j.Modified);
                foreach (var j in best)
                {
                    if (JarHasAapt2(j.Path, "auto-discovered")) return j.Path;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] bundletool search failed in {root}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Downloads the latest official bundletool-all.jar from Google's GitHub releases into
        /// the project Library folder. Returns the path or null when download fails/declined.
        /// </summary>
        private static string TryDownloadBundletool()
        {
            if (!EditorUtility.DisplayDialog("bundletool.jar",
                    "Nenhum bundletool.jar completo (com aapt2 embutido) foi encontrado no sistema.\n\n" +
                    "Baixar bundletool-all.jar automaticamente da release oficial do GitHub\n" +
                    "(será salvo em Library/bundletool-all.jar do projeto)?",
                    "BAIXAR", "CANCELAR"))
                return null;

            var destDir = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(destDir)) return null;

            try
            {
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers.Add(System.Net.HttpRequestHeader.UserAgent, "UnityBuildPipeline");

                    EditorUtility.DisplayProgressBar("bundletool", "Consultando releases do GitHub...", 0.1f);
                    var json = wc.DownloadString("https://api.github.com/repos/google/bundletool/releases/latest");

                    string assetUrl = null;
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"browser_download_url\":\\s*\"([^\"]+)\""))
                    {
                        var url = m.Groups[1].Value;
                        if (url.IndexOf("bundletool-all", StringComparison.OrdinalIgnoreCase) >= 0 && url.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        {
                            assetUrl = url;
                            break;
                        }
                    }

                    if (assetUrl == null)
                    {
                        EditorUtility.ClearProgressBar();
                        Debug.LogWarning("[BuildPipeline] No bundletool-all asset found in latest GitHub release.");
                        return null;
                    }

                    var fileName = Path.GetFileName(assetUrl);
                    var destFolder = Path.Combine(destDir, "Library");
                    Directory.CreateDirectory(destFolder);
                    var destPath = Path.Combine(destFolder, fileName);

                    var downloadTask = wc.DownloadFileTaskAsync(assetUrl, destPath);
                    while (!downloadTask.IsCompleted)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("bundletool", $"Downloading {fileName}...", 0.5f))
                        {
                            wc.CancelAsync();
                            EditorUtility.ClearProgressBar();
                            try { System.Threading.Tasks.Task.WaitAny(downloadTask); } catch { }
                            if (File.Exists(destPath)) File.Delete(destPath);
                            return null;
                        }
                        System.Threading.Thread.Sleep(100);
                    }
                    downloadTask.Wait();
                }

                EditorUtility.ClearProgressBar();

                var libFolder = Path.Combine(destDir, "Library");
                var actual = Directory.GetFiles(libFolder, "bundletool-all*.jar")
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

                if (actual == null || !JarHasAapt2(actual, "downloaded")) return null;

                Debug.Log($"[BuildPipeline] bundletool downloaded: {actual}");
                return actual;
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogWarning($"[BuildPipeline] bundletool download failed: {ex.Message}");
                return null;
            }
        }

        private static string ResolveBundletool()
        {
            var jar = FindBundletool();
            if (jar != null) return jar;

            jar = TryDownloadBundletool();
            if (jar != null) return jar;

            jar = EditorUtility.OpenFilePanel(
                "Select bundletool-all.jar (the full build, not the slim SDK copy)",
                "",
                "jar");
            if (string.IsNullOrEmpty(jar) || !File.Exists(jar)) return null;
            if (!JarHasAapt2(jar, "manually selected")) return null;

            EditorPrefs.SetString("BuildPipeline_BundletoolJar", jar);
            Debug.Log($"[BuildPipeline] bundletool.jar saved for future runs: {jar}");
            return jar;
        }

        private static void InstallAndLaunchAab(BuildHistoryEntry entry)
        {
            var bundletool = ResolveBundletool();
            if (bundletool == null)
            {
                EditorUtility.DisplayDialog("Run Build (AAB)",
                    "bundletool.jar não encontrado.\n\n" +
                    "Baixe em https://github.com/google/bundletool/releases e selecione o arquivo na próxima janela " +
                    "(ou coloque-o em Library/bundletool.jar do projeto).", "OK");
                return;
            }

            var java = FindJava();
            var apksPath = Path.Combine(Path.GetTempPath(),
                $"{Path.GetFileNameWithoutExtension(entry.outputPath)}.apks");

            try
            {
                var buildOut = RunCommand(java,
                    $"-jar \"{bundletool}\" build-apks --connected-device --bundle=\"{entry.outputPath}\" --output=\"{apksPath}\" --overwrite",
                    "Run Build (AAB)", "Generating device-specific APKs from .aab (bundletool build-apks)...");
                if (buildOut == null) return; // cancelled by user
                Debug.Log($"[BuildPipeline] bundletool build-apks:\n{buildOut}");

                var installOut = RunCommand(java,
                    $"-jar \"{bundletool}\" install-apks --apks=\"{apksPath}\"",
                    "Run Build (AAB)", "Installing APKs on device (bundletool install-apks)...");
                if (installOut == null) return; // cancelled by user
                Debug.Log($"[BuildPipeline] bundletool install-apks:\n{installOut}");

                if (installOut.IndexOf("Success", StringComparison.OrdinalIgnoreCase) < 0 &&
                    buildOut.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    EditorUtility.DisplayDialog("Run Build (AAB)",
                        $"bundletool failed.\n\nOutput:\n{buildOut}\n{installOut}\n\n" +
                        "Check that a device is connected and USB debugging is enabled.", "OK");
                    return;
                }

                var launchOut = "";
                if (!string.IsNullOrEmpty(entry.bundleId))
                {
                    launchOut = RunCommand(FindAdb(), $"shell monkey -p {entry.bundleId} 1", "Run Build (AAB)", "Launching game on device");
                    if (launchOut == null) return;
                }

                Debug.Log($"[BuildPipeline] AAB installed & launched. bundleId: {entry.bundleId}\n{launchOut}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
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

            try
            {
                var output = RunCommand(adb, $"install -r \"{entry.outputPath}\"", "Run Build", $"Installing APK on device ({adb}) (adb install -r)");
                if (output == null) return; // cancelled by user

                if (output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    EditorUtility.DisplayDialog("Run Build (APK)",
                        $"APK install did not report success.\n\nCommand: adb install -r\nOutput:\n{output}\n\n" +
                        "Check that a device is connected and USB debugging is enabled.", "OK");
                    return;
                }

                var launchOut = "";
                if (!string.IsNullOrEmpty(entry.bundleId))
                {
                    launchOut = RunCommand(adb, $"shell monkey -p {entry.bundleId} 1", "Run Build", "Launching game on device");
                    if (launchOut == null) return;
                }

                Debug.Log($"[BuildPipeline] APK installed & launched. bundleId: {entry.bundleId}\n{output}\n{launchOut}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string RunCommand(string fileName, string arguments, string progressTitle = null, string progressInfo = null)
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
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();

                var sw = Stopwatch.StartNew();
                while (!p.WaitForExit(200))
                {
                    if (progressTitle == null) continue;

                    var progress = Mathf.Min(0.95f, (float)(sw.ElapsedMilliseconds / 120000.0));
                    if (EditorUtility.DisplayCancelableProgressBar(progressTitle, progressInfo, progress))
                    {
                        try { p.Kill(); } catch { }
                        Debug.LogWarning($"[BuildPipeline] Cancelled by user: {fileName} {arguments}");
                        return null;
                    }
                }

                return $"{stdoutTask.Result}\n{stderrTask.Result}".Trim();
            }
        }
    }
}
