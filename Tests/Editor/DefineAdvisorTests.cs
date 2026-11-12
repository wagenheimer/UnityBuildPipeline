using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class DefineAdvisorTests
    {
        private static DefineProfileFootprint Footprint(string id, bool has, params string[] tags) => new DefineProfileFootprint
        {
            ProfileId = id,
            DisplayName = id,
            PlatformGroup = tags.Contains("ios") ? "iOS" : "Android",
            Tags = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase),
            Has = has
        };

        [Test]
        public void InfersRequiredFreeAndForbiddenFullFromProfileLayout()
        {
            var profiles = new List<DefineProfileFootprint>
            {
                Footprint("iOSFree", true, "free", "ios", "mobile"),
                Footprint("AndroidFree", true, "free", "android", "mobile"),
                Footprint("iOSFull", false, "full", "ios", "mobile"),
                Footprint("AndroidFull", false, "full", "android", "mobile")
            };

            var suggestion = DefineAdvisor.Suggest("GAME_ADS", profiles, new List<DefineUsage>(), new List<DefineUsage>());

            CollectionAssert.AreEqual(new[] { "free" }, suggestion.Declaration.requiredWhen);
            CollectionAssert.AreEqual(new[] { "full" }, suggestion.Declaration.forbiddenWhen);
            StringAssert.Contains("anúncios", suggestion.Declaration.description);
            Assert.That(suggestion.Reasons, Is.Not.Empty);
        }

        [Test]
        public void NoRuleWhenTheDefineIsInEveryProfile()
        {
            var profiles = new List<DefineProfileFootprint>
            {
                Footprint("A", true, "free", "ios"),
                Footprint("B", true, "full", "android")
            };

            var suggestion = DefineAdvisor.Suggest("GAME_X", profiles, null, null);

            Assert.That(suggestion.Declaration.requiredWhen, Is.Empty);
            StringAssert.Contains("todos os perfis", suggestion.Declaration.description);
        }

        [Test]
        public void DescriptionMentionsCodeUsage()
        {
            var usages = new List<DefineUsage>
            {
                new DefineUsage { symbol = "GAME_X", kind = DefineUsageKind.Condition, file = "Assets/Scripts/Main.cs" },
                new DefineUsage { symbol = "GAME_X", kind = DefineUsageKind.Condition, file = "Assets/Scripts/Cannon.cs" }
            };

            var suggestion = DefineAdvisor.Suggest("GAME_X", new List<DefineProfileFootprint>(), usages, null);

            Assert.AreEqual(2, suggestion.UsageCount);
            Assert.AreEqual(2, suggestion.FileCount);
            StringAssert.Contains("Main.cs", suggestion.Declaration.description);
        }

        [Test]
        public void VersionDefineMakesItAssemblyScopedAndAutomatic()
        {
            var versionDefines = new List<DefineUsage>
            {
                new DefineUsage { symbol = "HAS_FOO", kind = DefineUsageKind.AssemblyVersionDefine, versionDefinePackage = "com.example.foo" }
            };

            var suggestion = DefineAdvisor.Suggest("HAS_FOO", new List<DefineProfileFootprint>(), null, versionDefines);

            Assert.AreEqual(DefineScopes.Assembly, suggestion.Declaration.scope);
            Assert.AreEqual("com.example.foo", suggestion.Declaration.autoFromPackage);
        }

        [Test]
        public void NearestSymbolFindsTypos()
        {
            var known = new[] { "GAME_MONETIZATION", "GAME_TRACKING", "STEAMWORKS" };

            Assert.AreEqual("GAME_MONETIZATION", DefineAdvisor.NearestSymbol("GAME_MONETIZATON", known));
            Assert.IsNull(DefineAdvisor.NearestSymbol("COMPLETELY_DIFFERENT", known));
        }
    }

    public sealed class SteamProfileAdvisorTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private ProjectBuildConfig Config(params PublisherProfile[] profiles)
        {
            var config = Profiles.Config(null, profiles);
            _created.Add(config);
            return config;
        }

        [Test]
        public void ReportsAGapWhenNoProfileIsSteam()
        {
            var config = Config(Profiles.Mobile(Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.example"));

            var gaps = SteamProfileAdvisor.FindGaps(config);

            Assert.AreEqual(1, gaps.Count);
            StringAssert.Contains("Nenhum perfil Steam", gaps[0].Title);
        }

        [Test]
        public void NoGapWhenASteamProfileExists()
        {
            var steam = new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows64) { id = "steam" };
            var config = Config(steam);

            Assert.IsEmpty(SteamProfileAdvisor.FindGaps(config));
        }

        [Test]
        public void CreatedProfileIsSteamAndHasAUniqueId()
        {
            var existing = new PublisherProfile(Publisher.Default, "Other", PlatformType.Windows64) { id = "steam" };
            var config = Config(existing);

            var created = SteamProfileAdvisor.Create(config);

            Assert.IsTrue(created.isSteam);
            Assert.AreEqual("steam-2", created.EffectiveId);
            Assert.AreEqual(PlatformType.Windows64, created.platform);
        }
    }
}
