using UnityEditor;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class SyncGameConfigStep : IPreBuildStep
    {
        public int Order => 10;

        public bool ExecutePreBuild(BuildContext context)
        {
            if (context.Config == null || context.Config.gameConfig == null)
            {
                context.LogWarning("No GameConfig asset assigned to ProjectBuildConfig. Skipping runtime sync.");
                return true;
            }

            var gc = context.Config.gameConfig;
            gc.Publisher = context.Publisher;
            gc.GameLanguage = context.Language;
            gc.CheatMode = context.CheatMode;
            gc.FullGame = context.PublisherProfile != null ? context.PublisherProfile.isFullGame : !context.Demo;
            gc.Demo = context.Demo;
            gc.LogLevelsInfo = context.LogLevelsInfo;
            gc.LevelEditor = context.LevelEditor;

            EditorUtility.SetDirty(gc);
            AssetDatabase.SaveAssets();
            context.Log($"Synchronized GameConfig.asset -> Publisher: {gc.Publisher}, Lang: {gc.GameLanguage}, Cheat: {gc.CheatMode}, Full: {gc.FullGame}");
            return true;
        }
    }
}
