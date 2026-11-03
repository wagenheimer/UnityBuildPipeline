using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Shared Open/Copy actions for output folders, used by the Build list, Steam tab, and profile card.</summary>
    public static class BuildOutputFolderActions
    {
        public static void Open(ProjectBuildConfig config, PublisherProfile profile, PlatformType platform)
        {
            OpenPath(ProfileOutputPaths.ResolveFolder(config, profile, platform), "Open Build Output Folder");
        }

        public static void OpenPath(string folder, string title = "Open Folder")
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                EditorUtility.DisplayDialog(title, "The folder path is empty.", "OK");
                return;
            }

            try
            {
                if (!Directory.Exists(folder))
                {
                    if (!EditorUtility.DisplayDialog(title,
                            $"Folder does not exist yet:\n\n{folder}\n\nCreate and open it now?",
                            "Create and Open", "Cancel"))
                        return;

                    Directory.CreateDirectory(folder);
                }

                EditorUtility.RevealInFinder(folder);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(title, $"Could not open folder:\n\n{folder}\n\n{ex.Message}", "OK");
            }
        }

        public static void CopyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            EditorGUIUtility.systemCopyBuffer = path;
            Debug.Log($"[BuildPipeline] Path copied to clipboard: {path}");
        }
    }
}
