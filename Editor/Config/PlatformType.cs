using UnityEditor;

namespace Wagenheimer.BuildPipeline.Editor
{
    public enum PlatformType
    {
        Windows64,
        macOS,
        Android,
        iOS,
        Linux64,
        WebGL,
        /// <summary>Standalone Windows x86 (32-bit). Kept at the end so existing serialized profiles keep their values.</summary>
        Windows32
    }

    public static class PlatformTypeExtensions
    {
        public static BuildTarget ToBuildTarget(this PlatformType platform)
        {
            return platform switch
            {
                PlatformType.Windows64 => BuildTarget.StandaloneWindows64,
                PlatformType.Windows32 => BuildTarget.StandaloneWindows,
                PlatformType.macOS => BuildTarget.StandaloneOSX,
                PlatformType.Android => BuildTarget.Android,
                PlatformType.iOS => BuildTarget.iOS,
                PlatformType.Linux64 => BuildTarget.StandaloneLinux64,
                PlatformType.WebGL => BuildTarget.WebGL,
                _ => BuildTarget.StandaloneWindows64
            };
        }

        public static BuildTargetGroup ToBuildTargetGroup(this PlatformType platform)
        {
            return platform switch
            {
                PlatformType.Windows64 or PlatformType.Windows32 or PlatformType.macOS or PlatformType.Linux64 => BuildTargetGroup.Standalone,
                PlatformType.Android => BuildTargetGroup.Android,
                PlatformType.iOS => BuildTargetGroup.iOS,
                PlatformType.WebGL => BuildTargetGroup.WebGL,
                _ => BuildTargetGroup.Standalone
            };
        }

        /// <summary>True for both Windows architectures (x64 and x86).</summary>
        public static bool IsWindows(this PlatformType platform) =>
            platform is PlatformType.Windows64 or PlatformType.Windows32;

        /// <summary>
        /// True when the platform produces a standalone folder/site artifact (no store upload),
        /// packaged by the Forge as a generic .zip.
        /// </summary>
        public static bool IsGenericArtifact(this PlatformType platform)
        {
            return platform is PlatformType.Windows64
                or PlatformType.Windows32
                or PlatformType.macOS
                or PlatformType.Linux64
                or PlatformType.WebGL;
        }
    }
}
