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
            new ApplyBuildVariantStep(),
            new SyncGameConfigStep(),
            new ValidateGoogleDependenciesStep(),
            new ApplyScriptingDefinesStep(),
            new ApplyPlayerSettingsStep(),
            new SetupKeystoreStep(),
            new FilterScenesStep()
        };

        private static readonly List<IPostBuildStep> PostSteps = new List<IPostBuildStep>
        {
            new ApplyScriptingDefinesStep(),
            new ApplyPlayerSettingsStep(),
            new CopyPublisherSplashStep(),
            new CleanupObsoleteFilesStep(),
            new ExportAndroidSymbolsStep(),
            new ZipArchiveStep()
        };

        public static void RegisterPreStep(IPreBuildStep step) => PreSteps.Add(step);
        public static void RegisterPostStep(IPostBuildStep step) => PostSteps.Add(step);

        /// <summary>
        /// Runs the pipeline. Steps that change ProjectSettings (scripting defines, player settings) are always
        /// restored: after a successful build by the post steps, and on ANY other exit (a pre-step failing or
        /// throwing, a failed build, BuildPlayer/SwitchActiveBuildTarget throwing) by the finally block.
        /// </summary>
        public static BuildResultSummary Execute(BuildContext context)
        {
            bool restored = false;
            try
            {
                return ExecuteCore(context, () => restored = true);
            }
            finally
            {
                if (!restored)
                    RunCleanup(context, null, "build aborted");
            }
        }

        private static void RunCleanup(BuildContext context, BuildReport report, string reason)
        {
            foreach (var cleanup in new IPostBuildStep[] { new ApplyScriptingDefinesStep(), new ApplyPlayerSettingsStep() })
            {
                try { cleanup.ExecutePostBuild(context, report); }
                catch (Exception ex) { context.LogError($"Exception in cleanup step {cleanup.GetType().Name} ({reason}): {ex}"); }
            }
        }

        private static BuildResultSummary ExecuteCore(BuildContext context, Action markRestored)
        {
            var stopwatch = Stopwatch.StartNew();
            var variantText = string.IsNullOrEmpty(context.VariantId) ? "" : $" | Variant: {context.VariantLabel}";
            context.Log($"=== START BUILD: {context.ProjectName} | {context.Publisher} | {context.Language} | {context.Platform}{variantText} ===");

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

            CleanConflictingBackendArtifacts(context);

            var report = UnityEditor.BuildPipeline.BuildPlayer(playerOptions);
            stopwatch.Stop();

            // Record into build history BEFORE post steps restore ProjectSettings,
            // so the bundle id captured is the one actually built with.
            BuildHistory.Record(context, new BuildResultSummary
            {
                Success = report.summary.result == BuildResult.Succeeded,
                OutputPath = context.ResolvedOutputFilePath,
                Duration = stopwatch.Elapsed,
                TotalSize = report.summary.totalSize,
                TotalErrors = report.summary.totalErrors
            }, PlayerSettings.applicationIdentifier);

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

                // Execute Post-Build Steps (they include the ProjectSettings restore).
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

                markRestored();

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

                // On failure the normal post steps are skipped, but the cleanup steps that keep
                // ProjectSettings pristine must still run (scripting defines + version-code restore).
                RunCleanup(context, report, "after failed build");
                markRestored();
            }

            return result;
        }

        private static void CleanConflictingBackendArtifacts(BuildContext context)
        {
            try
            {
                var outputFolder = System.IO.Path.GetDirectoryName(context.ResolvedOutputFilePath);
                if (string.IsNullOrEmpty(outputFolder) || !System.IO.Directory.Exists(outputFolder))
                    return;

                var targetGroup = context.Platform.ToBuildTargetGroup();
                var targetBackend = PlayerSettings.GetScriptingBackend(targetGroup);
                if (targetBackend == ScriptingImplementation.IL2CPP)
                {
                    var monoFolder = System.IO.Path.Combine(outputFolder, "MonoBleedingEdge");
                    if (System.IO.Directory.Exists(monoFolder))
                    {
                        context.Log($"Removing leftover MonoBleedingEdge runtime directory from IL2CPP destination: {monoFolder}");
                        System.IO.Directory.Delete(monoFolder, true);
                    }
                }
                else if (targetBackend == ScriptingImplementation.Mono2x)
                {
                    var gameAssembly = System.IO.Path.Combine(outputFolder, "GameAssembly.dll");
                    if (System.IO.File.Exists(gameAssembly))
                    {
                        context.Log($"Removing leftover IL2CPP GameAssembly.dll from Mono destination: {gameAssembly}");
                        System.IO.File.Delete(gameAssembly);
                    }
                }
            }
            catch (System.Exception ex)
            {
                context.LogWarning($"Could not clean conflicting backend artifacts: {ex.Message}");
            }
        }
    }
}
