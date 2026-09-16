using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

            // Show which toolchain will actually do the work so the user can verify it.
            if (entry.IsAab)
            {
                var bt = GetBundletoolPath();
                info += $"\n\nbundletool: {(string.IsNullOrEmpty(bt) ? "NOT FOUND (will offer download)" : bt)}";
                info += $"\njava: {FindJava()}";
            }
            else if (entry.IsApk)
            {
                info += $"\nadb: {FindAdb()}";
            }

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

        private static string _bundletoolPath;

        /// <summary>Cached bundletool path for UI display (Run dialog + Recent Builds header).</summary>
        public static string GetBundletoolPath()
        {
            if (_bundletoolPath == null || !File.Exists(_bundletoolPath))
                _bundletoolPath = FindBundletool();
            return _bundletoolPath;
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
        /// "bundletool-all" build): EditorPrefs override -> env var -> stable shared folder ->
        /// project folders -> Unity's bundled Android player -> .NET Android SDK packs ->
        /// Visual Studio Xamarin. Slim SDK-bundled jars (no aapt2 inside) are rejected with a
        /// warning. Returns null when nothing valid is found (caller can then download or prompt).
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

            // Stable machine-wide folder where TryDownloadBundletool saves copies - survives
            // Unity regenerating the project's Library folder and is shared across projects.
            if (Directory.Exists(StableBundletoolDir))
            {
                try
                {
                    var stable = Directory.GetFiles(StableBundletoolDir, "bundletool-all*.jar")
                        .OrderByDescending(File.GetLastWriteTimeUtc);
                    foreach (var j in stable)
                    {
                        if (JarHasAapt2(j, "stable folder")) return j;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BuildPipeline] bundletool search failed in {StableBundletoolDir}: {ex.Message}");
                }
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

        /// <summary>Machine-wide folder for downloaded tools (survives Unity's Library regeneration).</summary>
        private static string StableBundletoolDir => Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Wagenheimer", "BuildPipeline");

        /// <summary>True when the jar embeds the aapt2 binary (only the official bundletool-all
        /// build does). bundletool extracts it from an OS folder inside the jar
        /// (windows/aapt2.exe, macos/aapt2, linux/aapt2); class files such as Aapt2Command.class
        /// must NOT count as a match.</summary>
        private static bool JarHasAapt2(string jarPath, string origin)
        {
            try
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(jarPath))
                {
                    foreach (var e in zip.Entries)
                    {
                        var name = e.FullName;
                        var slash = name.LastIndexOf('/');
                        var fileName = slash >= 0 ? name.Substring(slash + 1) : name;
                        if (fileName.Equals("aapt2", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("aapt2.exe", StringComparison.OrdinalIgnoreCase))
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

            var jars = new List<string>();
            try
            {
                jars.AddRange(Directory.GetFiles(root, "bundletool*.jar", SearchOption.AllDirectories));
            }
            catch (Exception ex)
            {
                // A single unreadable subfolder must not kill the whole search: fall back to a
                // shallow scan of the first levels (enough for SDK pack layouts).
                Debug.LogWarning($"[BuildPipeline] Deep bundletool search failed in {root} ({ex.Message}); using shallow scan.");
                jars.AddRange(ShallowJarScan(root, 3));
            }

            if (jars.Count == 0) return null;

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

        /// <summary>Depth-limited jar scan that tolerates unreadable folders.</summary>
        private static IEnumerable<string> ShallowJarScan(string root, int maxDepth)
        {
            var queue = new Queue<(string dir, int depth)>();
            queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (dir, depth) = queue.Dequeue();

                string[] files;
                try { files = Directory.GetFiles(dir, "bundletool*.jar"); }
                catch { files = Array.Empty<string>(); }
                foreach (var f in files) yield return f;

                if (depth >= maxDepth) continue;

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { subdirs = Array.Empty<string>(); }
                foreach (var d in subdirs) queue.Enqueue((d, depth + 1));
            }
        }

        /// <summary>
        /// Downloads the latest official bundletool-all.jar into a stable machine-wide folder
        /// (%LOCALAPPDATA%\Wagenheimer\BuildPipeline). Prefers the OS curl.exe (with timeouts,
        /// no hang possible); falls back to WebClient with a 30s stall watchdog so a stuck
        /// connection can never hang the Editor forever.
        /// </summary>
        private static string TryDownloadBundletool()
        {
            if (!EditorUtility.DisplayDialog("bundletool.jar",
                    "Nenhum bundletool.jar completo (com aapt2 embutido) foi encontrado no sistema.\n\n" +
                    "Baixar bundletool-all.jar automaticamente da release oficial do GitHub?\n" +
                    "(será salvo em %LOCALAPPDATA%\\Wagenheimer\\BuildPipeline, compartilhado entre projetos)",
                    "BAIXAR", "CANCELAR"))
                return null;

            var destDir = StableBundletoolDir;
            try
            {
                Directory.CreateDirectory(destDir);

                EditorUtility.DisplayProgressBar("bundletool", "Consultando releases do GitHub...", 0.1f);
                var json = DownloadString("https://api.github.com/repos/google/bundletool/releases/latest");
                if (string.IsNullOrEmpty(json))
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogWarning("[BuildPipeline] Could not query GitHub releases API.");
                    return null;
                }

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
                var destPath = Path.Combine(destDir, fileName);

                var outp = DownloadFile(assetUrl, destPath, fileName);
                if (outp == null) return null; // cancelled / failed (already logged)

                EditorUtility.ClearProgressBar();

                if (!JarHasAapt2(destPath, "downloaded"))
                {
                    try { File.Delete(destPath); } catch { }
                    return null;
                }

                Debug.Log($"[BuildPipeline] bundletool downloaded: {destPath}");
                return destPath;
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogWarning($"[BuildPipeline] bundletool download failed: {ex.Message}");
                return null;
            }
        }

        private static string FindCurl()
        {
            var sys = Path.Combine(System.Environment.SystemDirectory, "curl.exe");
            if (File.Exists(sys)) return sys;
            return null;
        }

        /// <summary>GET with curl.exe (timeouts, no hang) falling back to WebClient + 30s stall watchdog.</summary>
        private static string DownloadString(string url)
        {
            var curl = FindCurl();
            if (curl != null)
            {
                var outp = RunCommand(curl, $"-sSL -A UnityBuildPipeline --connect-timeout 15 --max-time 30 \"{url}\"");
                if (outp != null && outp.Length > 0) return outp;
                Debug.LogWarning("[BuildPipeline] curl string fetch failed; falling back to WebClient.");
            }

            try
            {
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers.Add(System.Net.HttpRequestHeader.UserAgent, "UnityBuildPipeline");
                    var task = wc.DownloadStringTaskAsync(url);
                    var sw = Stopwatch.StartNew();
                    while (!task.IsCompleted)
                    {
                        if (sw.Elapsed.TotalSeconds > 30)
                        {
                            wc.CancelAsync();
                            return null;
                        }
                        System.Threading.Thread.Sleep(100);
                    }
                    return task.Result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] DownloadString failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>File download with curl.exe; WebClient fallback cancels when stalled for 30s.</summary>
        private static bool DownloadFile(string url, string destPath, string displayName)
        {
            var curl = FindCurl();
            if (curl != null)
            {
                var outp = RunCommand(curl,
                    $"-sSL -A UnityBuildPipeline --connect-timeout 15 --max-time 600 -o \"{destPath}\" \"{url}\"",
                    "bundletool", $"Downloading {displayName} (curl)...");
                if (outp == null) return false; // cancelled by user (process killed)

                if (File.Exists(destPath) && new FileInfo(destPath).Length > 1024 * 1024)
                    return true;
                Debug.LogWarning($"[BuildPipeline] curl download produced no file: {outp}");
            }
            else
            {
                Debug.LogWarning("[BuildPipeline] curl.exe not found; using WebClient with 30s stall watchdog.");
            }

            try
            {
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers.Add(System.Net.HttpRequestHeader.UserAgent, "UnityBuildPipeline");
                    var lastProgress = DateTime.UtcNow;
                    wc.DownloadProgressChanged += (s, e) => lastProgress = DateTime.UtcNow;

                    var task = wc.DownloadFileTaskAsync(url, destPath);
                    while (!task.IsCompleted)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("bundletool", $"Downloading {displayName}...", 0.5f))
                        {
                            wc.CancelAsync();
                            EditorUtility.ClearProgressBar();
                            try { System.Threading.Tasks.Task.WaitAny(task); } catch { }
                            if (File.Exists(destPath)) File.Delete(destPath);
                            return false;
                        }

                        if ((DateTime.UtcNow - lastProgress).TotalSeconds > 30)
                        {
                            wc.CancelAsync();
                            EditorUtility.ClearProgressBar();
                            try { System.Threading.Tasks.Task.WaitAny(task); } catch { }
                            if (File.Exists(destPath)) File.Delete(destPath);
                            Debug.LogWarning("[BuildPipeline] Download stalled (no progress for 30s) - aborted. Check proxy/network or download bundletool-all.jar manually.");
                            return false;
                        }

                        System.Threading.Thread.Sleep(200);
                    }
                    task.Wait();
                }

                return File.Exists(destPath) && new FileInfo(destPath).Length > 1024 * 1024;
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogWarning($"[BuildPipeline] bundletool download failed: {ex.Message}");
                if (File.Exists(destPath)) { try { File.Delete(destPath); } catch { } }
                return false;
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
                    "(ou coloque bundletool-all.jar em %LOCALAPPDATA%\\Wagenheimer\\BuildPipeline).", "OK");
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
