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
            // Snapshot dos números auto-incrementados abaixo — restaurados no post step para não
            // deixar ProjectSettings/ sujo no CI (o build define o número real via -version/-manifest).
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
                    PlayerSettings.useMacAppStoreValidation = context.Publisher is Publisher.MacAppStore or Publisher.MacAppStoreFull;

                    // Apple rejects Mac App Store uploads that carry only the arm64 slice unless the
                    // Info.plist minimum OS is 13.0+ (error 90981). Forcing Universal here means an
                    // engineer who never touched Player Settings still gets a build altool accepts.
                    var macArch = context.PublisherProfile != null ? context.PublisherProfile.macArchitecture : MacArchitecture.Universal;
                    PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, (int)macArch);
                    context.Log($"macOS Architecture set to {macArch}");
                    if (!CommandLineArgs.Has("buildNumber"))
                    {
                        if (context.Config != null && context.Config.gameConfig != null && !string.IsNullOrEmpty(context.Config.gameConfig.iOSBuildNumber))
                        {
                            PlayerSettings.macOS.buildNumber = context.Config.gameConfig.iOSBuildNumber;
                        }
                        else
                        {
                            int buildNum = 0;
                            int.TryParse(PlayerSettings.macOS.buildNumber, out buildNum);
                            PlayerSettings.macOS.buildNumber = (buildNum + 1).ToString();
                        }
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

#pragma warning disable CS0618
                    if (context.DevelopmentBuild)
                    {
                        EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Disabled;
                        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                    }
                    else
                    {
                        EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;
                        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
                    }
#pragma warning restore CS0618
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

        public bool ExecutePostBuild(BuildContext context, BuildReport report)
        {
            // Salva as alterações de PlayerSettings e assets no projeto para que o incremento persista
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
