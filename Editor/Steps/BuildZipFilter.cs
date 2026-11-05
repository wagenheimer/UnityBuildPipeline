using System;
using System.IO;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// What stays out of the build .zip: folders Unity itself marks as "do not ship" (IL2CPP symbols in
    /// <c>*_BackUpThisFolder_ButDontShipItWithYourGame</c>, Burst symbols in <c>*_BurstDebugInformation_DoNotShip</c>)
    /// and OS junk files. The folders stay on disk (they're needed to symbolicate crashes); they just don't go to players.
    /// </summary>
    internal static class BuildZipFilter
    {
        private static readonly string[] ExcludedFolderSuffixes = { "_ButDontShipItWithYourGame", "_DoNotShip" };
        private static readonly string[] ExcludedFileNames = { ".DS_Store", "Thumbs.db", "desktop.ini" };

        /// <summary>7-Zip switches with the same rules as <see cref="IsExcluded"/>.</summary>
        public const string SevenZipExcludeArgs = "-xr!*_ButDontShipItWithYourGame -xr!*_DoNotShip -xr!.DS_Store -xr!Thumbs.db -xr!desktop.ini";

        /// <param name="relativePath">Path relative to the folder being zipped (either separator).</param>
        public static bool IsExcluded(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;

            var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                var isLast = i == segments.Length - 1;
                foreach (var suffix in ExcludedFolderSuffixes)
                    if (segments[i].EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        return true;

                if (isLast)
                    foreach (var name in ExcludedFileNames)
                        if (string.Equals(segments[i], name, StringComparison.OrdinalIgnoreCase))
                            return true;
            }
            return false;
        }

        public static string RelativePath(string root, string fullPath)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(fullPath);
            return full.Length > rootFull.Length ? full.Substring(rootFull.Length + 1) : string.Empty;
        }
    }
}
