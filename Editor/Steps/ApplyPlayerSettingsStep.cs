using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class ApplyPlayerSettingsStep : IPreBuildStep, IPostBuildStep
    {
        public int Order => 20;

        private const string OrigAndroidBvc = "ApplyPlayerSettingsStep.OrigAndroidBundleVersionCode";
        private const string OrigIosBuild = "ApplyPlayerSettingsStep.OrigIosBuildNumber";
        private const string OrigMacBuild = "ApplyPlayerSettingsStep.OrigMacBuildNumber";

        public bool ExecutePreBuild(BuildContext context)
        {
            // Snapshot of the numbers auto-incremented below — restored in the post step so CI doesn't
            // leave ProjectSettings/ dirty (the build sets the real number via -version/-manifest).
            context.ExtraData[OrigAndroidBvc] = PlayerSettings.Android.bundleVersionCode;
            context.ExtraData[OrigIosBuild] = PlayerSettings.iOS.buildNumber;
            context.ExtraData[OrigMacBuild] = PlayerSettings.macOS.buildNumber;

            var targetGroup = context.Platform.ToBuildTargetGroup();
            PlayerSettings.productName = context.EffectiveGameName;

            if (context.Config != null && context.Config.gameConfig != null)
            {
                PlayerSettings.bundleVersion = context.Config.gameConfig.GameVersion.GameVersionAsText;
            }

            // Determine Bundle Identifier
            var bundleId = "";
            if (context.PublisherProfile != null && !string.IsNullOrEmpty(context.PublisherProfile.bundleIdentifier))
            {
                bundleId = context.PublisherProfile.bundleIdentifier;
            }
            else if (context.Config != null && context.Config.gameConfig != null)
            {
                var gc = context.Config.gameConfig;
                bundleId = context.Publisher switch
                {
                    Publisher.GoogleAndroidFull => !string.IsNullOrEmpty(gc.AndroidFull) ? gc.AndroidFull : gc.DefaultBundleIdentifier,
                    Publisher.GoogleAndroidFree => !string.IsNullOrEmpty(gc.AndroidFree) ? gc.AndroidFree : gc.DefaultBundleIdentifier,
                    Publisher.AmazonAndroidFull => !string.IsNullOrEmpty(gc.AmazonFull) ? gc.AmazonFull : gc.DefaultBundleIdentifier,
                    Publisher.AmazonAndroidFree => !string.IsNullOrEmpty(gc.AmazonFree) ? gc.AmazonFree : gc.DefaultBundleIdentifier,
                    Publisher.SamsungFull => !string.IsNullOrEmpty(gc.SamsungFull) ? gc.SamsungFull : gc.DefaultBundleIdentifier,
                    Publisher.SamsungFree => !string.IsNullOrEmpty(gc.SamsungFree) ? gc.SamsungFree : gc.DefaultBundleIdentifier,
                    Publisher.iOSFull => !string.IsNullOrEmpty(gc.IOSFull) ? gc.IOSFull : gc.DefaultBundleIdentifier,
                    Publisher.iOSFree => !string.IsNullOrEmpty(gc.IOSFree) ? gc.IOSFree : gc.DefaultBundleIdentifier,
                    Publisher.MacAppStore or Publisher.MacAppStoreFull => !string.IsNullOrEmpty(gc.IOSFree) ? gc.IOSFree : gc.DefaultBundleIdentifier,
                    _ => gc.DefaultBundleIdentifier
                };
            }

            if (string.IsNullOrEmpty(bundleId) && context.Config != null)
                bundleId = context.Config.defaultBundleIdentifier;

            if (!string.IsNullOrEmpty(bundleId))
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.FromBuildTargetGroup(targetGroup), bundleId);
                context.Log($"Set Application Identifier ({targetGroup}): {bundleId}");
            }

            // Platform-specific settings
            switch (context.Platform)
            {
                case PlatformType.Windows64:
                    var backend = context.PublisherProfile != null ? context.PublisherProfile.scriptingBackend : ScriptingImplementation.IL2CPP;
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
                    break;

                case PlatformType.macOS:
                    var macBackend = context.PublisherProfile != null ? context.PublisherProfile.scriptingBackend : ScriptingImplementation.Mono2x;
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, macBackend);
                    
                    // DEPRECATED / DANGEROUS: PlayerSettings.useMacAppStoreValidation forces the app to call exit(173)
                    // if Contents/_MASReceipt/receipt is missing, breaking ALL local testing and non-App Store runs.
                    // Apple does not require this setting for Mac App Store acceptance.
                    // Only enable if explicitly requested via CLI (-macAppStoreValidation true) or profile.
                    var enableMasValidation = CommandLineArgs.GetBool("macAppStoreValidation", false)
                        || (context.PublisherProfile != null && context.PublisherProfile.macAppStoreValidation);
                    PlayerSettings.useMacAppStoreValidation = enableMasValidation;
                    if (enableMasValidation)
                        context.LogWarning("⚠️ PlayerSettings.useMacAppStoreValidation=true: the app will fail with error 173 if run outside the Mac App Store!");
                    else
                        context.Log("🛡️ PlayerSettings.useMacAppStoreValidation=false (avoids a crash with error 173 during local testing)");

                    // Apple rejects Mac App Store uploads that carry only the arm64 slice unless the
                    // Info.plist minimum OS is 13.0+ (error 90981). Forcing Universal here means an
                    // engineer who never touched Player Settings still gets a build altool accepts.
                    // NOTE: PlayerSettings.SetArchitecture only applies to iOS/tvOS/visionOS — it's a
                    // silent no-op on macOS. The macOS "Architecture" dropdown is a standalone platform
                    // setting, only reachable via EditorUserBuildSettings.SetPlatformSettings.
                    var macArch = context.PublisherProfile != null ? context.PublisherProfile.macArchitecture : MacArchitecture.Universal;
                    var macArchValue = macArch switch
                    {
                        MacArchitecture.IntelOnly => "x64",
                        MacArchitecture.AppleSiliconOnly => "ARM64",
                        _ => "x64ARM64",
                    };
                    EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", macArchValue);
                    context.Log($"macOS Architecture set to {macArch} ({macArchValue})");
                    if (!CommandLineArgs.Has("buildNumber"))
                    {
                        // Apple rejects a re-upload whose CFBundleVersion isn't strictly higher than the
                        // last accepted one (error 90061). ProjectSettings.asset was already bumped on
                        // disk by the CI script before Unity even started, so whatever candidate this
                        // fallback computes must never regress below what's already loaded in memory —
                        // otherwise a stale/mismatched gameConfig.iOSBuildNumber (a field shared with iOS,
                        // not macOS-specific) can silently downgrade the macOS build number.
                        int currentMacBuild = 0;
                        int.TryParse(PlayerSettings.macOS.buildNumber, out currentMacBuild);

                        int candidate = currentMacBuild + 1;
                        if (context.Config != null && context.Config.gameConfig != null
                            && int.TryParse(context.Config.gameConfig.iOSBuildNumber, out var configuredBuild))
                        {
                            candidate = Math.Max(candidate, configuredBuild);
                        }

                        PlayerSettings.macOS.buildNumber = Math.Max(candidate, currentMacBuild).ToString();
                    }
                    break;

                case PlatformType.Android:
                    if (!CommandLineArgs.Has("buildNumber"))
                    {
                        if (context.Config != null && context.Config.gameConfig != null && context.Config.gameConfig.AndroidBundleVersionCode > 0)
                        {
                            PlayerSettings.Android.bundleVersionCode = context.Config.gameConfig.AndroidBundleVersionCode;
                        }
                        else
                        {
                            PlayerSettings.Android.bundleVersionCode += 1;
                        }
                    }
                    EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
                    EditorUserBuildSettings.buildAppBundle = context.AppBundle;

                    if (context.DevelopmentBuild)
                    {
                        ApplyAndroidDebugSymbols(false, false);
                        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                    }
                    else
                    {
                        // Native symbols are enabled by default on every Android release build: the Play
                        // Store uses them to symbolicate ANRs/crashes (otherwise it warns about missing
                        // native debug symbols). In .aab they're embedded in the bundle (no extra upload)
                        // and also packaged into a zip next to the artifact — see ExportAndroidSymbolsStep
                        // and the legacy androidCreateSymbols fallback below.
                        ApplyAndroidDebugSymbols(true, context.AppBundle);
                        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
                    }
                    break;

                case PlatformType.iOS:
                    if (!CommandLineArgs.Has("buildNumber"))
                    {
                        if (context.Config != null && context.Config.gameConfig != null && !string.IsNullOrEmpty(context.Config.gameConfig.iOSBuildNumber))
                        {
                            PlayerSettings.iOS.buildNumber = context.Config.gameConfig.iOSBuildNumber;
                        }
                        else
                        {
                            int iosBuild = 0;
                            int.TryParse(PlayerSettings.iOS.buildNumber, out iosBuild);
                            PlayerSettings.iOS.buildNumber = (iosBuild + 1).ToString();
                        }
                    }
                    break;
            }

            return true;
        }

        /// <summary>
        /// Configures Android native debug symbol generation.
        /// In Unity 6 the obsolete <see cref="EditorUserBuildSettings.androidCreateSymbols"/> property
        /// was replaced by <c>UnityEditor.Android.UserBuildSettings.DebugSymbols</c> and stopped having
        /// any effect — that's why builds came out without <c>*.symbols.zip</c> and the Play Store
        /// complained about missing native debug symbols. When <paramref name="embedInBundle"/> is true
        /// and the artifact is .aab, the symbols are embedded directly in the bundle (Play receives them
        /// with the .aab, no separate upload) and also packaged into a zip with the legacy .so extension.
        /// </summary>
        private static void ApplyAndroidDebugSymbols(bool create, bool embedInBundle)
        {
#if UNITY_6000_0_OR_NEWER
            UnityEditor.Android.UserBuildSettings.DebugSymbols.level = create
                ? Unity.Android.Types.DebugSymbolLevel.SymbolTable
                : Unity.Android.Types.DebugSymbolLevel.None;

            if (create)
            {
                var format = Unity.Android.Types.DebugSymbolFormat.Zip
                             | Unity.Android.Types.DebugSymbolFormat.LegacyExtensions;
                if (embedInBundle)
                    format |= Unity.Android.Types.DebugSymbolFormat.IncludeInBundle;
                UnityEditor.Android.UserBuildSettings.DebugSymbols.format = format;
            }
#else
#pragma warning disable CS0618
            EditorUserBuildSettings.androidCreateSymbols = create
                ? AndroidCreateSymbols.Public
                : AndroidCreateSymbols.Disabled;
#pragma warning restore CS0618
#endif
        }

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            // Saves PlayerSettings and asset changes to the project so the increment persists
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
