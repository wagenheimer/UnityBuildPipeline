using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Wagenheimer.BuildPipeline;

namespace Wagenheimer.BuildPipeline.Editor
{
    [Serializable]
    public class PublisherProfile
    {
        [Tooltip("Stable slug used by CI (-buildProfile <id>). If empty, the publisher enum name is used.")]
        public string id = "";
        public Publisher publisher = Publisher.Default;
        public string displayName = "Default";
        public PlatformType platform = PlatformType.Windows64;
        public string bundleIdentifier = "";
        public bool requiresSplash = false;
        public bool isFullGame = true;
        public bool isDemo = false;
        public ScriptingImplementation scriptingBackend = ScriptingImplementation.IL2CPP;
        [Tooltip("Extra scripting define symbols applied for this profile's build target, then restored afterwards (e.g. DEMO_FREE, NO_ADS).")]
        public List<string> scriptingDefines = new List<string>();
        public string outputSubfolder = "Builds/Publishers/{Publisher}/";
        public bool zipAfterBuild = false;
        public string zipDestinationTemplate = @"E:\Documentos\Dropbox\Builds\{ProjectName}\{Publisher}\";

        /// <summary>Effective CI id: explicit <see cref="id"/> when set, otherwise the publisher enum name.</summary>
        public string EffectiveId => string.IsNullOrEmpty(id) ? publisher.ToString() : id;

        public PublisherProfile() { }

        public PublisherProfile(Publisher pub, string name, PlatformType plat, bool splash = false, bool full = true, bool demo = false)
        {
            publisher = pub;
            displayName = name;
            platform = plat;
            requiresSplash = splash;
            isFullGame = full;
            isDemo = demo;
            outputSubfolder = $"Builds/Publishers/{pub}/";
        }
    }

    [Serializable]
    public class LanguageProfile
    {
        public GameLanguage language = GameLanguage.English;
        public bool enabled = true;

        public LanguageProfile() { }

        public LanguageProfile(GameLanguage lang, bool isEnabled = true)
        {
            language = lang;
            enabled = isEnabled;
        }

        public string Code => language.AsLanguageCode();
        public string Name => language.AsLanguageName();
    }
}
