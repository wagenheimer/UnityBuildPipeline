using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.PackageHubBootstrap
{
    // Lives in its own assembly with zero references so it can compile and run
    // even when Wagenheimer Package Hub isn't installed yet. That's what lets
    // this package's main Editor assembly (which does reference
    // Wagenheimer.PackageHub.Editor) compile: this bootstrapper installs the
    // companion package via git before that reference is needed.
    [InitializeOnLoad]
    internal static class PackageHubBootstrap
    {
        private const string PackageHubId = "com.wagenheimer.packagehub";
        private const string PackageHubGitUrl = "https://github.com/wagenheimer/UnityPackageHub.git#v1.0.4";
        private const string StartedSessionKey = "Wagenheimer.PackageHubBootstrap.Started";

        private static AddRequest _addRequest;

        static PackageHubBootstrap()
        {
            if (SessionState.GetBool(StartedSessionKey, false))
                return;
            SessionState.SetBool(StartedSessionKey, true);

            EditorApplication.delayCall += CheckAndInstall;
        }

        private static void CheckAndInstall()
        {
            foreach (var pkg in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (pkg.name == PackageHubId)
                    return;
            }

            Debug.Log("[Wagenheimer] Package Hub not found, installing companion package via git...");
            _addRequest = Client.Add(PackageHubGitUrl);
            EditorApplication.update += MonitorRequest;
        }

        private static void MonitorRequest()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= MonitorRequest;

            if (_addRequest.Status == StatusCode.Success)
                Debug.Log("[Wagenheimer] Package Hub installed successfully.");
            else
                Debug.LogWarning($"[Wagenheimer] Could not auto-install Package Hub: {_addRequest.Error?.message}. Add it manually: {PackageHubGitUrl}");

            _addRequest = null;
        }
    }
}
