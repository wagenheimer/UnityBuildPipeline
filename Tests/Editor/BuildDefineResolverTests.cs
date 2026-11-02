using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class BuildDefineResolverTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
            {
                if (asset != null)
                    Object.DestroyImmediate(asset);
            }

            _created.Clear();
        }

        private ProjectBuildConfig Config(IEnumerable<BuildDefinePreset> presets, params PublisherProfile[] profiles)
        {
            var config = Profiles.Config(presets, profiles);
            _created.Add(config);
            return config;
        }

        [Test]
        public void PresetsExpandBeforeProfileDefines()
        {
            var profile = Profiles.Mobile(Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.example");
            profile.scriptingDefines.Add("ADS");
            profile.definePresets.Add("att");

            var config = Config(new[] { Profiles.Preset("att", "TRACK") }, profile);

            var resolved = BuildDefineResolver.Resolve(config, profile);

            CollectionAssert.AreEqual(new[] { "TRACK", "ADS" }, resolved);
        }

        [Test]
        public void UnknownPresetsAreReported()
        {
            var profile = Profiles.Mobile(Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.example");
            profile.definePresets.Add("missing");

            var config = Config(null, profile);

            CollectionAssert.AreEqual(new[] { "missing" }, BuildDefineResolver.UnknownPresets(config, profile));
        }

        [Test]
        public void CatalogProfileSeparatesRawEffectiveAndRemoved()
        {
            var profile = Profiles.Mobile(
                Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.example", "Builds/iOS Free/", "!ADS", "KEEP");
            profile.definePresets.Add("att");

            var config = Config(new[] { Profiles.Preset("att", "TRACK") }, profile);

            var entry = BuildProfileCatalog.ToCatalogProfile(config, profile);

            CollectionAssert.AreEqual(new[] { "TRACK", "!ADS", "KEEP" }, entry.scriptingDefines);
            CollectionAssert.AreEqual(new[] { "TRACK", "KEEP" }, entry.effectiveDefines);
            CollectionAssert.AreEqual(new[] { "ADS" }, entry.removedDefines);
            CollectionAssert.AreEqual(new[] { "att" }, entry.presetIds);
        }

        [Test]
        public void CatalogPathIsNextToTheAsset()
        {
            Assert.AreEqual(
                "Assets/_Game/Settings/ProjectBuildConfig.catalog.json",
                BuildProfileCatalog.GetCatalogPath("Assets/_Game/Settings/ProjectBuildConfig.asset"));
        }
    }
}
