using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class FilterScenesStep : IPreBuildStep
    {
        public int Order => 40;

        public bool ExecutePreBuild(BuildContext context)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToList();

            var pubSplash = context.Config != null ? context.Config.publisherSplashScenePath : "Assets/_Game/Scenes/publishersplash.unity";
            var defSplash = context.Config != null ? context.Config.defaultSplashScenePath : "Assets/_Game/Scenes/splash.unity";
            var editorScene = context.Config != null ? context.Config.levelEditorScenePath : "Assets/_Game/Scenes/editor.unity";

            scenes.Remove(pubSplash);
            scenes.Remove(defSplash);

            var needsSplash = context.PublisherProfile != null && context.PublisherProfile.requiresSplash;
            if (needsSplash)
            {
                scenes.Insert(0, pubSplash);
            }

            if (context.Platform != PlatformType.iOS && context.Platform != PlatformType.Android)
            {
                var insertIndex = needsSplash ? 1 : 0;
                if (insertIndex < scenes.Count)
                    scenes.Insert(insertIndex, defSplash);
                else
                    scenes.Add(defSplash);
            }

            if (!context.LevelEditor)
            {
                scenes.Remove(editorScene);
            }

            context.ResolvedScenes = scenes;
            context.Log($"Configured {scenes.Count} scenes for build (Splash: {needsSplash}, LevelEditor: {context.LevelEditor})");
            return true;
        }
    }
}
