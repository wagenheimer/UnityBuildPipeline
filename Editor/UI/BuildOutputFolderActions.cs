using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>Shared Open/Copy actions for output folders, used by the Build list and the profile card.</summary>
    internal static class BuildOutputFolderActions
    {
        public static void Open(ProjectBuildConfig config, PublisherProfile profile, PlatformType platform)
        {
            OpenPath(ProfileOutputPaths.ResolveFolder(config, profile, platform), "Abrir pasta de saída");
        }

        public static void OpenPath(string folder, string title = "Abrir pasta")
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                EditorUtility.DisplayDialog(title, "O caminho da pasta está vazio.", "OK");
                return;
            }

            try
            {
                if (!Directory.Exists(folder))
                {
                    if (!EditorUtility.DisplayDialog(title,
                            $"A pasta ainda não existe:\n\n{folder}\n\nCriar agora?",
                            "Criar e abrir", "Cancelar"))
                        return;

                    Directory.CreateDirectory(folder);
                }

                EditorUtility.RevealInFinder(folder);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(title, $"Não foi possível abrir:\n\n{folder}\n\n{ex.Message}", "OK");
            }
        }

        public static void CopyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            EditorGUIUtility.systemCopyBuffer = path;
            Debug.Log($"[BuildPipeline] Caminho copiado: {path}");
        }
    }
}
