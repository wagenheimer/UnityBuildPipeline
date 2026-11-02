using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Project-wide index of where every define is used: Assets/, embedded/local packages and
    /// Library/PackageCache. Scans on a background thread and caches per file (path + mtime) in
    /// Library/BuildPipeline, so only changed files are re-read.
    /// </summary>
    public static class DefineUsageIndex
    {
        private const string CachePath = "Library/BuildPipeline/define-usage-cache.json";
        private const int CacheVersion = 1;

        private static List<DefineUsage> _usages = new List<DefineUsage>();
        private static Task<ScanResult> _running;
        private static readonly List<Action> PendingCallbacks = new List<Action>();

        public static bool IsScanning => _running != null && !_running.IsCompleted;
        public static bool HasResult { get; private set; }
        public static DateTime LastScanUtc { get; private set; }
        public static int FileCount { get; private set; }
        public static string LastError { get; private set; } = "";
        public static IReadOnlyList<DefineUsage> Usages => _usages;

        /// <summary>Raised on the main thread when a scan finishes.</summary>
        public static event Action Changed;

        public static void EnsureScanned(Action onDone = null)
        {
            if (HasResult && !IsScanning)
            {
                onDone?.Invoke();
                return;
            }

            Scan(onDone);
        }

        public static void Scan(Action onDone = null)
        {
            if (onDone != null)
                PendingCallbacks.Add(onDone);

            if (IsScanning)
                return;

            var roots = CollectRoots();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _running = Task.Run(() => RunScan(roots, projectRoot));
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        public static List<DefineUsage> UsagesOf(string symbol) =>
            _usages.Where(usage => string.Equals(usage.symbol, symbol, StringComparison.Ordinal)).ToList();

        public static List<DefineUsage> Writers() =>
            _usages.Where(usage => usage.kind == DefineUsageKind.GlobalWriter).ToList();

        public static List<DefineUsage> VersionDefines() =>
            _usages.Where(usage => usage.kind == DefineUsageKind.AssemblyVersionDefine).ToList();

        /// <summary>Symbols referenced anywhere (excluding writers), ordered.</summary>
        public static List<string> Symbols() =>
            _usages.Where(usage => usage.kind != DefineUsageKind.GlobalWriter && !string.IsNullOrEmpty(usage.symbol))
                .Select(usage => usage.symbol).Distinct(StringComparer.Ordinal).OrderBy(symbol => symbol, StringComparer.Ordinal).ToList();

        /// <summary>Opens the file at the usage line in the configured code editor.</summary>
        public static void Open(DefineUsage usage)
        {
            if (usage == null || string.IsNullOrEmpty(usage.file))
                return;

            if (usage.file.StartsWith("Assets/", StringComparison.Ordinal) || usage.file.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(usage.file);
                if (asset != null && AssetDatabase.OpenAsset(asset, Math.Max(1, usage.line)))
                    return;
            }

            string absolute = Path.IsPathRooted(usage.file)
                ? usage.file
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", usage.file));
            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(absolute, Math.Max(1, usage.line));
        }

        public static void Ping(DefineUsage usage)
        {
            if (usage == null)
                return;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(usage.file);
            if (asset != null)
                EditorGUIUtility.PingObject(asset);
        }

        private static void Poll()
        {
            if (_running == null || !_running.IsCompleted)
                return;

            EditorApplication.update -= Poll;
            var task = _running;
            _running = null;

            if (task.IsFaulted)
            {
                LastError = task.Exception?.GetBaseException().Message ?? "scan failed";
                Debug.LogWarning("[BuildPipeline] Varredura de defines falhou: " + LastError);
            }
            else
            {
                _usages = task.Result.Usages;
                FileCount = task.Result.FileCount;
                LastError = "";
            }

            HasResult = true;
            LastScanUtc = DateTime.UtcNow;

            var callbacks = PendingCallbacks.ToList();
            PendingCallbacks.Clear();
            foreach (var callback in callbacks)
            {
                try { callback(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }

            Changed?.Invoke();
        }

        private sealed class Root
        {
            public string Absolute;
            public string Display;
            public string Package;
        }

        private sealed class ScanResult
        {
            public List<DefineUsage> Usages;
            public int FileCount;
        }

        private static List<Root> CollectRoots()
        {
            var roots = new List<Root>
            {
                new Root { Absolute = Path.GetFullPath(Application.dataPath), Display = "Assets", Package = "Assets" }
            };

            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (package == null || string.IsNullOrEmpty(package.resolvedPath) || !Directory.Exists(package.resolvedPath))
                    continue;

                roots.Add(new Root
                {
                    Absolute = Path.GetFullPath(package.resolvedPath),
                    Display = "Packages/" + package.name,
                    Package = package.name
                });
            }

            return roots;
        }

        private static ScanResult RunScan(List<Root> roots, string projectRoot)
        {
            var cache = LoadCache(projectRoot);
            var nextCache = new CacheFile { version = CacheVersion };
            var usages = new List<DefineUsage>();
            var asmdefs = new List<KeyValuePair<string, string>>(); // directory → assembly name
            int files = 0;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                foreach (string path in EnumerateSources(root.Absolute))
                {
                    // An embedded package can live under Assets/ (or two roots can overlap): count each file once.
                    if (!visited.Add(path))
                        continue;

                    files++;
                    string display = root.Display + path.Substring(root.Absolute.Length).Replace('\\', '/');
                    long ticks = File.GetLastWriteTimeUtc(path).Ticks;

                    CacheEntry entry;
                    if (!cache.TryGetValue(path, out entry) || entry.ticks != ticks)
                        entry = ParseFile(path, display, ticks);

                    entry.path = path;
                    nextCache.entries.Add(entry);

                    if (!string.IsNullOrEmpty(entry.assemblyName))
                        asmdefs.Add(new KeyValuePair<string, string>(Path.GetDirectoryName(path) + Path.DirectorySeparatorChar, entry.assemblyName));

                    foreach (var cached in entry.usages)
                    {
                        // Work on a copy: owner assembly/package are resolved per scan and must not leak into the cache.
                        var usage = JsonUtility.FromJson<DefineUsage>(JsonUtility.ToJson(cached));
                        usage.package = root.Package;
                        if (usage.kind != DefineUsageKind.AssemblyConstraint && usage.kind != DefineUsageKind.AssemblyVersionDefine)
                            usage.assembly = "";
                        usages.Add(usage);
                    }
                }
            }

            // Owner assembly = deepest asmdef folder containing the file.
            var ordered = asmdefs.OrderByDescending(pair => pair.Key.Length).ToList();
            var byFile = nextCache.entries.ToDictionary(e => e.display, e => e.path, StringComparer.Ordinal);
            foreach (var usage in usages)
            {
                if (!string.IsNullOrEmpty(usage.assembly))
                    continue;

                string absolute = byFile.TryGetValue(usage.file, out var abs) ? abs : usage.file;
                var owner = ordered.FirstOrDefault(pair => absolute.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase));
                usage.assembly = owner.Value ?? (usage.file.StartsWith("Assets/", StringComparison.Ordinal) ? "Assembly-CSharp" : "");
            }

            SaveCache(projectRoot, nextCache);
            return new ScanResult { Usages = usages, FileCount = files };
        }

        private static IEnumerable<string> EnumerateSources(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] children;
                string[] entries;
                try
                {
                    children = Directory.GetDirectories(directory);
                    entries = Directory.GetFiles(directory);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (string child in children)
                {
                    string name = Path.GetFileName(child);
                    // Unity never compiles hidden folders or folders ending in '~' (Samples~, Documentation~).
                    if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal))
                        continue;

                    pending.Push(child);
                }

                foreach (string file in entries)
                {
                    if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(file).Equals("csc.rsp", StringComparison.OrdinalIgnoreCase))
                        yield return file;
                }
            }
        }

        private static CacheEntry ParseFile(string path, string display, long ticks)
        {
            var entry = new CacheEntry { display = display, ticks = ticks };
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return entry;
            }

            if (path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
            {
                entry.usages = DefineUsageParser.ParseAsmdef(display, text, out string assembly);
                entry.assemblyName = assembly;
            }
            else if (path.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
            {
                entry.usages = DefineUsageParser.ParseResponseFile(display, text);
            }
            else if (text.IndexOf('#') >= 0 || text.IndexOf("Conditional", StringComparison.Ordinal) >= 0 ||
                     text.IndexOf("SetScriptingDefineSymbols", StringComparison.Ordinal) >= 0)
            {
                entry.usages = DefineUsageParser.ParseCSharp(display, text);
            }

            return entry;
        }

        private static Dictionary<string, CacheEntry> LoadCache(string projectRoot)
        {
            var result = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(projectRoot, CachePath);
                if (!File.Exists(path))
                    return result;

                var file = JsonUtility.FromJson<CacheFile>(File.ReadAllText(path));
                if (file == null || file.version != CacheVersion)
                    return result;

                foreach (var entry in file.entries)
                {
                    if (!string.IsNullOrEmpty(entry.path))
                        result[entry.path] = entry;
                }
            }
            catch (Exception)
            {
                // A corrupt cache only costs a full rescan.
            }

            return result;
        }

        private static void SaveCache(string projectRoot, CacheFile file)
        {
            try
            {
                string path = Path.Combine(projectRoot, CachePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(file));
            }
            catch (Exception)
            {
                // Cache is an optimisation; ignore write failures.
            }
        }

        [Serializable]
        private sealed class CacheFile
        {
            public int version;
            public List<CacheEntry> entries = new List<CacheEntry>();
        }

        [Serializable]
        private sealed class CacheEntry
        {
            public string path = "";
            public string display = "";
            public long ticks;
            public string assemblyName = "";
            public List<DefineUsage> usages = new List<DefineUsage>();
        }
    }

    /// <summary>Classifies symbols so the UI can hide Unity's own (UNITY_*, ENABLE_*...).</summary>
    public static class DefineSymbols
    {
        private static readonly string[] BuiltInPrefixes =
        {
            "UNITY_", "ENABLE_", "PLATFORM_", "NET_", "NETSTANDARD", "NETFX_", "NETCOREAPP", "CSHARP_", "DEVELOPMENT_BUILD"
        };

        private static readonly HashSet<string> BuiltInExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "DEBUG", "TRACE", "RELEASE", "NET", "NETFRAMEWORK", "WINDOWS_UWP", "UNITY_EDITOR"
        };

        public static bool IsUnityBuiltIn(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return false;

            if (BuiltInExact.Contains(symbol))
                return true;

            foreach (string prefix in BuiltInPrefixes)
            {
                if (symbol.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
