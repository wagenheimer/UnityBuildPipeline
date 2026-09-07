using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Wagenheimer.BuildPipeline.Editor
{
    [InitializeOnLoad]
    internal static class UpdateChecker
    {
        const string PackageDisplayName = "Unity Build Pipeline";
        internal const string GitUrl = "https://github.com/wagenheimer/UnityBuildPipeline.git";
        const string PackageJsonUrl = "https://raw.githubusercontent.com/wagenheimer/UnityBuildPipeline/master/package.json";
        const string ChangelogUrl = "https://raw.githubusercontent.com/wagenheimer/UnityBuildPipeline/master/CHANGELOG.md";
        const string RepoUrl = "https://github.com/wagenheimer/UnityBuildPipeline";
        const string PrefLastCheckTicks = "Wagenheimer.BuildPipeline.UpdateChecker.LastCheckTicks";
        const string PrefSkipVersion = "Wagenheimer.BuildPipeline.UpdateChecker.SkipVersion";
        const double CheckIntervalHours = 24;

        static UpdateChecker()
        {
            EditorApplication.delayCall += () => CheckForUpdate(force: false);
        }

        [MenuItem("Tools/Build Pipeline/Check for Updates...", priority = 100)]
        static void CheckForUpdateMenuItem() => CheckForUpdate(force: true);

        internal static void CheckForUpdate(bool force)
        {
            if (!force && !IntervalElapsed())
                return;

            var request = UnityWebRequest.Get(PackageJsonUrl);
            request.timeout = 5;
            var op = request.SendWebRequest();
            op.completed += _ => OnPackageJsonComplete(request, force);
        }

        static bool IntervalElapsed()
        {
            var stored = EditorPrefs.GetString(PrefLastCheckTicks, "0");
            if (!long.TryParse(stored, out var ticks))
                return true;

            return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalHours >= CheckIntervalHours;
        }

        static void OnPackageJsonComplete(UnityWebRequest request, bool force)
        {
            EditorPrefs.SetString(PrefLastCheckTicks, DateTime.UtcNow.Ticks.ToString());

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[BuildPipeline] Update check failed: {request.error}");
                if (force)
                    EditorUtility.DisplayDialog(PackageDisplayName, $"Failed to check for updates:\n{request.error}", "OK");
                request.Dispose();
                return;
            }

            string remoteVersion = null;
            var rawText = request.downloadHandler?.text;
            if (!string.IsNullOrEmpty(rawText))
            {
                // Strip UTF-8 BOM (\uFEFF) and whitespace
                rawText = rawText.Trim().Trim('\uFEFF', '\u200B');

                // Try fast and robust regex match first
                var match = System.Text.RegularExpressions.Regex.Match(rawText, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success)
                {
                    remoteVersion = match.Groups[1].Value.Trim();
                }
                else
                {
                    try
                    {
                        remoteVersion = JsonUtility.FromJson<PackageJsonVersionOnly>(rawText)?.version;
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"[BuildPipeline] Update check JSON parse warning: {e.Message}");
                    }
                }
            }

            request.Dispose();

            var localVersion = GetLocalVersion();
            if (string.IsNullOrEmpty(remoteVersion))
            {
                Debug.LogWarning("[BuildPipeline] Update check: remote package.json has no valid version field.");
                if (force)
                    EditorUtility.DisplayDialog(PackageDisplayName, "Could not determine latest version from GitHub repository.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(localVersion))
            {
                Debug.LogWarning("[BuildPipeline] Update check: could not resolve installed package version.");
                if (force)
                    EditorUtility.DisplayDialog(PackageDisplayName, "Failed to identify installed version of Build Pipeline.", "OK");
                return;
            }

            if (!IsNewer(remoteVersion, localVersion))
            {
                Debug.Log($"[BuildPipeline] Up to date (installed: {localVersion}, remote: {remoteVersion}).");
                if (force)
                    EditorUtility.DisplayDialog(PackageDisplayName, $"You are already using the latest version of Unity Build Pipeline ({localVersion}).", "OK");
                return;
            }
                    EditorUtility.DisplayDialog(PackageDisplayName, $"You are already using the latest version ({localVersion}).", "OK");
                return;
            }

            if (!force && EditorPrefs.GetString(PrefSkipVersion, "") == remoteVersion)
            {
                Debug.Log($"[BuildPipeline] Version {remoteVersion} available (installed: {localVersion}) but ignored by user preference.");
                return;
            }

            Debug.Log($"[BuildPipeline] New version available: {remoteVersion} (installed: {localVersion}). See {RepoUrl}/releases/latest");
            FetchChangelogAndShow(localVersion, remoteVersion);
        }

        static void FetchChangelogAndShow(string localVersion, string remoteVersion)
        {
            var request = UnityWebRequest.Get(ChangelogUrl);
            request.timeout = 5;
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                string notes = null;
                if (request.result == UnityWebRequest.Result.Success)
                    notes = ExtractVersionNotes(request.downloadHandler.text, remoteVersion);

                request.Dispose();
                UpdateAvailableWindow.Show("Unity Build Pipeline", localVersion, remoteVersion, RepoUrl, GitUrl, notes, PrefSkipVersion);
            };
        }

        static string ExtractVersionNotes(string changelog, string version)
        {
            var marker = $"## [{version}]";
            var start = changelog.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return null;

            var bodyStart = changelog.IndexOf('\n', start);
            if (bodyStart < 0)
                return null;

            var next = changelog.IndexOf("\n## [", bodyStart, StringComparison.Ordinal);
            var end = next >= 0 ? next : changelog.Length;
            return changelog.Substring(bodyStart, end - bodyStart).Trim();
        }

        static string GetLocalVersion()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UpdateChecker).Assembly);
            return packageInfo?.version ?? "1.0.0";
        }

        static bool IsNewer(string remote, string local)
        {
            if (Version.TryParse(remote, out var remoteVer) && Version.TryParse(local, out var localVer))
                return remoteVer > localVer;

            return string.CompareOrdinal(remote, local) > 0;
        }

        [Serializable]
        class PackageJsonVersionOnly
        {
            public string version;
        }
    }
}
