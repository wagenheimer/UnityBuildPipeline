using UnityEditor;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Applies the game-specific build variant declared in <see cref="GameConfig.buildVariants"/> to the
    /// project's <see cref="GameConfig"/> (or its subclass) before the build runs.
    /// <para>
    /// The package knows nothing about editions: it only forwards the selected id to
    /// <see cref="GameConfig.ApplyBuildVariant"/>, so a game can translate "ce" into its own
    /// <c>CollectorEdition</c> flag (or any other field) without touching this package.
    /// Runs before <see cref="SyncGameConfigStep"/> so the shared fields win if both write the same value.
    /// </para>
    /// </summary>
    public class ApplyBuildVariantStep : IPreBuildStep
    {
        public int Order => 5;

        public bool ExecutePreBuild(BuildContext context)
        {
            var config = context.Config != null ? context.Config.gameConfig : null;
            if (config == null || string.IsNullOrEmpty(context.VariantId))
                return true;

            // Resolve the declared variant (if the caller passed only an id) so the output suffix matches.
            if (context.Variant == null)
                context.Variant = config.FindBuildVariant(context.VariantId);

            var variantId = context.Variant != null ? context.Variant.EffectiveId : context.VariantId;

            try
            {
                config.ApplyBuildVariant(variantId);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                context.Log($"Applied build variant '{variantId}' ({context.VariantLabel}) to {config.name}.");
            }
            catch (System.Exception ex)
            {
                context.LogError($"Failed to apply build variant '{variantId}': {ex.Message}");
                return false;
            }

            return true;
        }
    }
}
