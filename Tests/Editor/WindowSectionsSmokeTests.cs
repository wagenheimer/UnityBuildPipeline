using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    /// <summary>Every section of the window must build without throwing, for a realistic profile matrix.</summary>
    public sealed class WindowSectionsSmokeTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
                if (asset != null) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private ProjectBuildConfig Matrix()
        {
            var free = Profiles.Mobile(Publisher.GoogleAndroidFree, "gp-free", PlatformType.Android, false, "com.x.free", "", "ADS");
            var full = Profiles.Mobile(Publisher.GoogleAndroidFull, "gp-full", PlatformType.Android, true, "com.x.full", "", "!ADS");
            var ios = Profiles.Mobile(Publisher.iOSFree, "ios-free", PlatformType.iOS, false, "com.x.ios", "", "ADS", "TRACK");
            ios.definePresets.Add("att");
            var steam = new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows64) { id = "steam" };
            var german = new PublisherProfile(Publisher.BigFish, "Big Fish DE", PlatformType.Windows64)
            {
                id = "bigfish-de",
                languageMode = ProfileLanguageMode.Locked,
                lockedLanguage = GameLanguage.German,
                hidden = true
            };

            var config = Profiles.Config(new[] { Profiles.Preset("att", "TRACK") }, free, full, ios, steam, german);
            config.languages = LanguageProfile.GetDefaultLanguages();
            config.gameDefines.Add(new DefineDeclaration { symbol = "ADS", requiredWhen = new List<string> { "free" }, forbiddenWhen = new List<string> { "full" } });
            _created.Add(config);
            return config;
        }

        private static readonly string[] Sections =
        {
            BuildPipelineSections.Build, BuildPipelineSections.History, BuildPipelineSections.Profiles,
            BuildPipelineSections.Defines, BuildPipelineSections.Languages, BuildPipelineSections.Project,
            BuildPipelineSections.Signing, BuildPipelineSections.Steam, BuildPipelineSections.Automation
        };

        [Test]
        public void EverySectionBuilds([ValueSource(nameof(Sections))] string section)
        {
            var config = Matrix();
            var selection = new BuildPipelineSelectionState { selectedProfileId = "ios-free", selectedDefine = "ADS" };
            var context = new BuildPipelineSectionContext(config, selection, () => { }, () => { }, (s, p) => { });

            VisualElement root = null;
            Assert.DoesNotThrow(() => root = BuildPipelineWindow.CreateSection(context, section));
            Assert.That(root, Is.Not.Null);
            Assert.That(root.childCount, Is.GreaterThan(0));
        }

        [TestCase("matrix")]
        [TestCase("")]
        public void ProfilesTabsBuild(string tab)
        {
            var selection = new BuildPipelineSelectionState { profilesTab = tab, selectedProfileId = "bigfish-de" };
            var context = new BuildPipelineSectionContext(Matrix(), selection, () => { }, () => { }, (s, p) => { });
            Assert.DoesNotThrow(() => BuildPipelineWindow.CreateSection(context, BuildPipelineSections.Profiles));
        }

        [TestCase("registry")]
        [TestCase("presets")]
        [TestCase("globals")]
        public void DefinesTabsBuild(string tab)
        {
            var selection = new BuildPipelineSelectionState { definesTab = tab };
            var context = new BuildPipelineSectionContext(Matrix(), selection, () => { }, () => { }, (s, p) => { });
            Assert.DoesNotThrow(() => BuildPipelineWindow.CreateSection(context, BuildPipelineSections.Defines));
        }

        [Test]
        public void CatalogV4CarriesLanguageSteamAndStack()
        {
            var config = Matrix();
            var catalog = BuildProfileCatalog.Build(config, "");

            Assert.That(catalog.schema, Is.EqualTo(4));
            var german = catalog.profiles.Single(p => p.id == "bigfish-de");
            Assert.That(german.languageMode, Is.EqualTo("Locked"));
            Assert.That(german.lockedLanguage, Is.EqualTo("German"));
            Assert.That(catalog.profiles.Single(p => p.id == "steam").isSteam, Is.True);

            var ios = catalog.profiles.Single(p => p.id == "ios-free");
            Assert.That(ios.defineStack.Any(d => d.symbol == "TRACK" && d.layer == "Preset"), Is.True);
            Assert.That(ios.finalDefines, Does.Contain("ADS").And.Contain("TRACK"));

            var full = catalog.profiles.Single(p => p.id == "gp-full");
            Assert.That(full.finalDefines, Does.Not.Contain("ADS"));
            Assert.That(catalog.defineRegistry.Any(d => d.symbol == "ADS" && d.source == "game"), Is.True);
        }
    }
}
