using UnityEditor;
using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.BuildPipeline.Editor
{
    public static class UpdateChecker
    {
        [MenuItem("Tools/Wagenheimer/Build Pipeline/Check for Updates...", priority = 119)]
        public static void CheckForUpdateMenu() => CheckForUpdate(true);

        public static void CheckForUpdate(bool force = false)
        {
            PackageHubWindow.OpenToPackage("com.wagenheimer.buildpipeline");
        }
    }
}
