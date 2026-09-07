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

        [MenuItem("Tools/Wagenheimer/Build Pipeline/Check for Updates...", priority = 141)]
        [MenuItem("Tools/Build Pipeline/Check for Updates...", priority = 141)]
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
            try
            {
                remoteVersion = JsonUtility.FromJson<PackageJsonVersionOnly>(request.downloadHandler.text)?.version;
            }
            catch (Exception e)
            {
                Debug.Log($"[BuildPipeline] Update check failed: could not parse remote package.json ({e.Message})");
            }

            request.Dispose();

            var localVersion = GetLocalVersion();
            if (string.IsNullOrEmpty(remoteVersion))
            {
                Debug.Log("[BuildPipeline] Update check failed: remote package.json has no version field.");
                if (force)
                    EditorUtility.DisplayDialog(PackageDisplayName, "Failed to check for updates: remote package.json has no version field.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(localVersion))
            {
                Debug.Log("[BuildPipeline] Update check failed: could not resolve the installed package version " +
                    "(PackageInfo.FindForAssembly returned null for this assembly).");
                if (force)
                    EditorUtility.DisplayDialog(PackageDisplayName, "Failed to check for updates: could not identify the installed version of this package.", "OK");
                return;
            }

            if (!IsNewer(remoteVersion, localVersion))
            {
                Debug.Log($"[BuildPipeline] Up to date (installed: {localVersion}).");
                if (force)
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
