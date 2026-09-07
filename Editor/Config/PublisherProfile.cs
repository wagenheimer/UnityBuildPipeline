using System;
using UnityEditor;
using UnityEngine;
using Wagenheimer.BuildPipeline;

namespace Wagenheimer.BuildPipeline.Editor
{
    [Serializable]
    public class PublisherProfile
    {
        public Publisher publisher = Publisher.Default;
        public string displayName = "Default";
        public PlatformType platform = PlatformType.Windows64;
        public string bundleIdentifier = "";
        public bool requiresSplash = false;
        public bool isFullGame = true;
        public bool isDemo = false;
        public ScriptingImplementation scriptingBackend = ScriptingImplementation.IL2CPP;
        public string outputSubfolder = "Builds/Publishers/{Publisher}/";
        public bool zipAfterBuild = false;
        public string zipDestinationTemplate = @"E:\Documentos\Dropbox\Builds\{ProjectName}\{Publisher}\";

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
