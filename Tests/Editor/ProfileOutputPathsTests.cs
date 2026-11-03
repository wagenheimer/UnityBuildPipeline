using System.IO;
using NUnit.Framework;
using UnityEngine;
using Wagenheimer.BuildPipeline;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class ProfileOutputPathsTests
    {
        private static PublisherProfile Steam(string output = "Builds/Publishers/Steam/") =>
            new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows64)
            {
                id = "steam",
                outputSubfolder = output
            };

        [Test]
        public void SteamDesktopProfilesGetOneFolderPerPlatform()
        {
            var steam = Steam();

            Assert.That(ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.Windows64),
                Is.EqualTo("Builds/Publishers/Steam/Windows"));
            Assert.That(ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.Linux64),
                Is.EqualTo("Builds/Publishers/Steam/Linux"));
            Assert.That(ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.macOS),
                Is.EqualTo("Builds/Publishers/Steam/Mac"));
        }

        [Test]
        public void NonSteamProfilesKeepTheDeclaredFolder()
        {
            var itch = new PublisherProfile(Publisher.ItchIO, "itch.io", PlatformType.Windows64)
            {
                outputSubfolder = "Builds/Publishers/ItchIO/"
            };

            Assert.That(ProfileOutputPaths.EffectiveSubfolder(itch, PlatformType.Windows64),
                Is.EqualTo("Builds/Publishers/ItchIO/"));
        }

        [Test]
        public void PlatformFolderIsNotAppendedTwice()
        {
            var steam = Steam("Builds/Publishers/Steam/Windows/");

            Assert.That(ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.Windows64),
                Is.EqualTo("Builds/Publishers/Steam/Windows/"));
        }

        [Test]
        public void ExplicitPlatformTokenIsReplaced()
        {
            var steam = Steam("Builds/Publishers/Steam/{Platform}/");

            Assert.That(ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.Linux64),
                Is.EqualTo("Builds/Publishers/Steam/Linux/"));
        }

        [Test]
        public void PublisherTokenIsStillExpanded()
        {
            var steam = Steam("Builds/Publishers/{Publisher}/");

            Assert.That(ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.macOS),
                Is.EqualTo("Builds/Publishers/Steam/Mac"));
        }

        [Test]
        public void GameFolderTokenIsExpandedWhenConfigProvided()
        {
            var steam = Steam("Publishers/Steam/{GameFolder}/");
            var config = ScriptableObject.CreateInstance<ProjectBuildConfig>();
            config.projectName = "Secrets of Magic 3 - Halloween";
            config.gameFolderName = "SecretsOfMagic3";

            try
            {
                string result = ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.Windows64, config);
                Assert.That(result, Is.EqualTo("Publishers/Steam/SecretsOfMagic3/Windows"));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RedundantBuildsPrefixIsStrippedWhenRootContainsBuilds()
        {
            var steam = Steam("Builds/Publishers/Steam/{GameFolder}/");
            var config = ScriptableObject.CreateInstance<ProjectBuildConfig>();
            config.buildOutputRoot = "E:/Games/Builds/";
            config.gameFolderName = "GameTest";

            try
            {
                string result = ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.Windows64, config);
                Assert.That(result, Is.EqualTo("Publishers/Steam/GameTest/Windows"));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void BuildContextResolvesTheSteamPlatformFolderDirectly()
        {
            var steam = Steam();
            var config = Profiles.Config(null, steam);
            config.buildOutputRoot = Path.Combine(Path.GetTempPath(), "bp-output-test");
            config.macBuildOutputRoot = config.buildOutputRoot;

            try
            {
                var windows = BuildContextFactory.Create(config, steam);
                windows.ResolveOutputPaths();

                string expected = Path.GetFullPath(Path.Combine(
                    config.buildOutputRoot, "Builds", "Publishers", "Steam", "Windows"));
                StringAssert.StartsWith(expected, windows.ResolvedOutputDirectory);

                var linux = BuildContextFactory.Create(config, steam,
                    new BuildContextFactory.Options { Platform = PlatformType.Linux64 });
                linux.ResolveOutputPaths();

                expected = Path.GetFullPath(Path.Combine(
                    config.buildOutputRoot, "Builds", "Publishers", "Steam", "Linux"));
                StringAssert.StartsWith(expected, linux.ResolvedOutputDirectory);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
