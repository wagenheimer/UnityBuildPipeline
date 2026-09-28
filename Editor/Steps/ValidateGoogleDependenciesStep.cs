using System;
using UnityEngine;
using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Pre-build step that ensures Google dependencies (EDM4U, Play Common, Play Core, Play Review)
    /// are properly configured via verified Git packages before an Android build begins.
    /// Eliminates Scoped Registry conflicts and prevents Gradle compilation failures.
    /// </summary>
    public class ValidateGoogleDependenciesStep : IPreBuildStep
    {
        public int Order => 5;

        public bool ExecutePreBuild(BuildContext context)
        {
            if (context.Platform != PlatformType.Android)
                return true;

            try
            {
                var diag = GoogleDependencyManager.Detect();
                if (!diag.IsRateControlInstalled)
                    return true;

                if (diag.NeedsMigration)
                {
                    context.LogWarning("[BuildPipeline] Google dependencies for RateControl require migration to recommended Git setup.");
                    foreach (var issue in diag.Issues)
                    {
                        context.LogWarning($"  • {issue}");
                    }

                    // In batch / CI mode, automatically migrate to avoid Gradle failure
                    if (Application.isBatchMode || diag.HasMissingPackages)
                    {
                        context.Log("[BuildPipeline] Automatically migrating Google dependencies to clean Git URLs...");
                        GoogleDependencyManager.MigrateToRecommended(false);
                    }
                }
                else
                {
                    context.Log("[BuildPipeline] Google dependencies verified (Clean Git setup).");
                }
            }
            catch (Exception ex)
            {
                context.LogWarning($"[BuildPipeline] ValidateGoogleDependenciesStep warning: {ex.Message}");
            }

            return true;
        }
    }
}
