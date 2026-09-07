using UnityEditor;

namespace Wagenheimer.BuildPipeline.Editor
{
    public enum PlatformType
    {
        Windows64,
        macOS,
        Android,
        iOS,
        Linux64
    }

    public static class PlatformTypeExtensions
    {
        public static BuildTarget ToBuildTarget(this PlatformType platform)
        {
            return platform switch
            {
                PlatformType.Windows64 => BuildTarget.StandaloneWindows64,
                PlatformType.macOS => BuildTarget.StandaloneOSX,
                PlatformType.Android => BuildTarget.Android,
                PlatformType.iOS => BuildTarget.iOS,
                PlatformType.Linux64 => BuildTarget.StandaloneLinux64,
                _ => BuildTarget.StandaloneWindows64
            };
        }

        public static BuildTargetGroup ToBuildTargetGroup(this PlatformType platform)
        {
            return platform switch
            {
                PlatformType.Windows64 or PlatformType.macOS or PlatformType.Linux64 => BuildTargetGroup.Standalone,
                PlatformType.Android => BuildTargetGroup.Android,
                PlatformType.iOS => BuildTargetGroup.iOS,
                _ => BuildTargetGroup.Standalone
            };
        }
    }
}
