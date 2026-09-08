using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Wagenheimer.BuildPipeline;

namespace Wagenheimer.BuildPipeline.Editor
{
    public class BuildContext
    {
        public ProjectBuildConfig Config { get; set; }
        public PublisherProfile PublisherProfile { get; set; }
        public Publisher Publisher { get; set; }
        public GameLanguage Language { get; set; }
        public PlatformType Platform { get; set; }

        public bool CheatMode { get; set; }
        public bool DevelopmentBuild { get; set; }
        public bool Demo { get; set; }
        public bool LevelEditor { get; set; }
        public bool LogLevelsInfo { get; set; }
        public bool AppBundle { get; set; } = true;
        public bool AutoRun { get; set; } = false;

        public string CustomOutputPath { get; set; } = "";
        public string ResolvedOutputFilePath { get; set; } = "";
        public string ResolvedOutputDirectory { get; set; } = "";
        public List<string> ResolvedScenes { get; set; } = new List<string>();

        /// <summary>
        /// Profile id used for CI reporting (-buildProfile). Falls back to the publisher name.
        /// </summary>
        public string ProfileId { get; set; } = "";

        /// <summary>
        /// Extra scripting define symbols to apply for this build (profile defines + CLI -defines override),
        /// applied by <see cref="ApplyScriptingDefinesStep"/> and restored afterwards.
        /// </summary>
        public List<string> ScriptingDefines { get; set; } = new List<string>();

        public BuildPlayerOptions PlayerOptions;
        public Dictionary<string, object> ExtraData { get; } = new Dictionary<string, object>();

        public string ProjectName
        {
            get
            {
                if (Config != null && !string.IsNullOrEmpty(Config.projectName))
                    return Config.projectName;

                var s = Application.dataPath.Split('/');
                var name = s.Length >= 2 ? s[s.Length - 2] : Application.productName;
                if (Config != null && Config.gameConfig != null && !string.IsNullOrEmpty(Config.gameConfig.BuildFolderName))
                    name = Config.gameConfig.BuildFolderName;

                if (LevelEditor) name += " (Editor)";
                else if (CheatMode) name += " (Cheat)";
                return name;
            }
        }

        public string EffectiveGameName
        {
            get
            {
                if (Language == GameLanguage.Japanese && Config != null && !string.IsNullOrEmpty(Config.gameNameJapanese))
                    return Config.gameNameJapanese;

                if (Config != null && Config.gameConfig != null)
                {
                    if (Language == GameLanguage.Japanese && !string.IsNullOrEmpty(Config.gameConfig.GameNameJapanese))
                        return Config.gameConfig.GameNameJapanese;

                    if (Platform == PlatformType.iOS && !string.IsNullOrEmpty(Config.gameConfig.iOSGameName))
                        return Config.gameConfig.iOSGameName;

                    if (!string.IsNullOrEmpty(Config.gameConfig.GameName))
                        return Config.gameConfig.GameName;
                }

                return Application.productName;
            }
        }

        public string ResolveOutputPaths()
        {
            if (!string.IsNullOrEmpty(CustomOutputPath))
            {
                ResolvedOutputFilePath = Path.GetFullPath(CustomOutputPath);
                ResolvedOutputDirectory = Path.GetDirectoryName(ResolvedOutputFilePath);
                return ResolvedOutputFilePath;
            }

            var root = Config.GetEffectiveBuildOutputRoot(Platform);
            var versionText = Config.gameConfig != null ? Config.gameConfig.GameVersion.GameVersionAsTextWithBetaLabel : "1.0";
            var dateText = Config.gameConfig != null ? Config.gameConfig.VersionDate.AsTextNoYear : DateTime.Now.ToString("MMMdd");
            var langSuffix = Language.FolderNameLanguage();
            var demoSuffix = Demo ? "_Demo" : "";
            var devSuffix = DevelopmentBuild ? "_Develop" : "";
            var needSplash = PublisherProfile != null && PublisherProfile.requiresSplash;
            var pubPrefix = needSplash ? $"{Publisher}_" : "";

            switch (Platform)
            {
                case PlatformType.Windows64:
                case PlatformType.Linux64:
                    var winFolder = $"{pubPrefix}{ProjectName}_{dateText} ({versionText}){langSuffix}{demoSuffix}{devSuffix}";
                    var sub = PublisherProfile != null ? PublisherProfile.outputSubfolder.Replace("{Publisher}", Publisher.ToString()) : "Builds/";
                    var targetFolder = Path.Combine(root, sub, winFolder);
                    var ext = Platform == PlatformType.Linux64 ? ".x86_64" : ".exe";
                    ResolvedOutputDirectory = targetFolder;
                    ResolvedOutputFilePath = Path.Combine(targetFolder, $"{ProjectName}{ext}");
                    break;

                case PlatformType.macOS:
                    var macFolder = $"{pubPrefix}{ProjectName}_{dateText} ({versionText}){langSuffix}_MAC";
                    var macSub = PublisherProfile != null ? PublisherProfile.outputSubfolder.Replace("{Publisher}", Publisher.ToString()) : "Builds/";
                    var macTargetFolder = Path.Combine(root, macSub, macFolder);
                    ResolvedOutputDirectory = macTargetFolder;
                    ResolvedOutputFilePath = Path.Combine(macTargetFolder, $"{ProjectName}.app");
                    break;

                case PlatformType.Android:
                    var debugSuffix = DevelopmentBuild ? "_DEBUG" : "";
                    var androidExt = AppBundle ? ".aab" : ".apk";
                    var androidSub = PublisherProfile != null ? PublisherProfile.outputSubfolder.Replace("{Publisher}", Publisher.ToString()) : "Builds/Google/";
                    var androidDir = Path.Combine(root, androidSub);
                    var androidFileName = $"{ProjectName}_{dateText} ({versionText}){langSuffix}{debugSuffix}_bv{PlayerSettings.Android.bundleVersionCode}{androidExt}";
                    ResolvedOutputDirectory = androidDir;
                    ResolvedOutputFilePath = Path.Combine(androidDir, androidFileName);
                    break;

                case PlatformType.iOS:
                    var iosSub = PublisherProfile != null ? PublisherProfile.outputSubfolder.Replace("{Publisher}", Publisher.ToString()) : "Builds/iOS/";
                    var iosDir = Path.Combine(root, iosSub, $"{ProjectName} ({(PublisherProfile != null && PublisherProfile.isFullGame ? "Full" : "Free")})");
                    ResolvedOutputDirectory = iosDir;
                    ResolvedOutputFilePath = iosDir;
                    break;

                case PlatformType.WebGL:
                    var webglFolder = $"{pubPrefix}{ProjectName}_{dateText} ({versionText}){langSuffix}{demoSuffix}{devSuffix}_WebGL";
                    var webglSub = PublisherProfile != null ? PublisherProfile.outputSubfolder.Replace("{Publisher}", Publisher.ToString()) : "Builds/WebGL/";
                    var webglDir = Path.Combine(root, webglSub, webglFolder);
                    ResolvedOutputDirectory = webglDir;
                    ResolvedOutputFilePath = webglDir; // WebGL builds into a directory
                    break;
            }

            return ResolvedOutputFilePath;
        }

        public void Log(string msg) => Debug.Log($"<color=#3498db>[BuildPipeline]</color> {msg}");
        public void LogWarning(string msg) => Debug.LogWarning($"<color=#f39c12>[BuildPipeline]</color> {msg}");
        public void LogError(string msg) => Debug.LogError($"<color=#e74c3c>[BuildPipeline]</color> {msg}");
    }
}
