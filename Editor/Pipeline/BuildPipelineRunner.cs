using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class BuildResultSummary
    {
        public bool Success;
        public BuildResult Result;
        public string OutputPath;
        public TimeSpan Duration;
        public ulong TotalSize;
        public int TotalErrors;
        public int TotalWarnings;
        public string ErrorMessage;
    }

    public static class BuildPipelineRunner
    {
        private static readonly List<IPreBuildStep> PreSteps = new List<IPreBuildStep>
        {
            new SyncGameConfigStep(),
            new ApplyPlayerSettingsStep(),
            new SetupKeystoreStep(),
            new FilterScenesStep()
        };

        private static readonly List<IPostBuildStep> PostSteps = new List<IPostBuildStep>
        {
            new CopyPublisherSplashStep(),
            new CleanupObsoleteFilesStep(),
            new ZipArchiveStep()
        };

        public static void RegisterPreStep(IPreBuildStep step) => PreSteps.Add(step);
        public static void RegisterPostStep(IPostBuildStep step) => PostSteps.Add(step);

        public static BuildResultSummary Execute(BuildContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            context.Log($"=== START BUILD: {context.ProjectName} | {context.Publisher} | {context.Language} | {context.Platform} ===");

            context.ResolveOutputPaths();

            var outDir = Path.GetDirectoryName(context.ResolvedOutputFilePath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            // Execute Pre-Build Steps
            foreach (var step in PreSteps.OrderBy(s => s.Order))
            {
                try
                {
                    if (!step.ExecutePreBuild(context))
                    {
                        stopwatch.Stop();
                        var err = $"Pre-build step {step.GetType().Name} failed.";
                        context.LogError(err);
                        return new BuildResultSummary { Success = false, Duration = stopwatch.Elapsed, ErrorMessage = err };
                    }
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    context.LogError($"Exception in pre-build step {step.GetType().Name}: {ex}");
                    return new BuildResultSummary { Success = false, Duration = stopwatch.Elapsed, ErrorMessage = ex.Message };
                }
            }

            // Switch build target
            var buildTarget = context.Platform.ToBuildTarget();
            var targetGroup = context.Platform.ToBuildTargetGroup();
            if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
            {
                context.Log($"Switching Active Build Target to {buildTarget}...");
                EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);
            }

            // Build Options
            var options = BuildOptions.CompressWithLz4HC;
            if (context.DevelopmentBuild)
            {
                options = BuildOptions.CompressWithLz4 | BuildOptions.Development;
            }

            if (context.AutoRun && !Application.isBatchMode)
            {
                options |= BuildOptions.AutoRunPlayer;
            }

            var playerOptions = new BuildPlayerOptions
            {
                scenes = context.ResolvedScenes.ToArray(),
                locationPathName = context.ResolvedOutputFilePath,
                target = buildTarget,
                targetGroup = targetGroup,
                options = options
            };

            context.PlayerOptions = playerOptions;
            context.Log($"Building player to: {context.ResolvedOutputFilePath}...");

            var report = UnityEditor.BuildPipeline.BuildPlayer(playerOptions);
            stopwatch.Stop();

            var summary = report.summary;
            var result = new BuildResultSummary
            {
                Success = summary.result == BuildResult.Succeeded,
                Result = summary.result,
                OutputPath = context.ResolvedOutputFilePath,
                Duration = stopwatch.Elapsed,
                TotalSize = summary.totalSize,
                TotalErrors = summary.totalErrors,
                TotalWarnings = summary.totalWarnings
            };

            if (result.Success)
            {
                context.Log($"<color=#2ecc71>BUILD SUCCEEDED!</color> Time: {stopwatch.Elapsed:mm\\:ss}, Size: {summary.totalSize / (1024 * 1024):F1} MB");

                // Execute Post-Build Steps
                foreach (var step in PostSteps.OrderBy(s => s.Order))
                {
                    try
                    {
                        step.ExecutePostBuild(context, report);
                    }
                    catch (Exception ex)
                    {
                        context.LogError($"Exception in post-build step {step.GetType().Name}: {ex}");
                    }
                }

                if (context.Platform == PlatformType.Android && context.AutoRun && !Application.isBatchMode)
                {
                    EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Window/Analysis/Android Logcat");
                }

                if (!Application.isBatchMode && !string.IsNullOrEmpty(outDir) && Directory.Exists(outDir))
                {
                    EditorUtility.RevealInFinder(outDir);
                }
            }
            else
            {
                context.LogError($"<color=#e74c3c>BUILD FAILED!</color> Result: {summary.result}, Errors: {summary.totalErrors}");
            }

            return result;
        }
    }
}
