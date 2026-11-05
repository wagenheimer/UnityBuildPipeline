using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Wagenheimer.BuildPipeline;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    /// <summary>Standalone Windows x86 (32-bit) profiles, added for GameHouse.</summary>
    public sealed class Windows32PlatformTests
    {
        [Test]
        public void Windows32MapsToTheX86StandaloneTarget()
        {
            Assert.That(PlatformType.Windows32.ToBuildTarget(), Is.EqualTo(BuildTarget.StandaloneWindows));
            Assert.That(PlatformType.Windows32.ToBuildTargetGroup(), Is.EqualTo(BuildTargetGroup.Standalone));
            Assert.That(PlatformType.Windows32.IsWindows(), Is.True);
            Assert.That(PlatformType.Windows32.IsGenericArtifact(), Is.True);
        }

        [Test]
        public void Windows32BuildsAnExeInsideTheProfileFolder()
        {
            var config = ScriptableObject.CreateInstance<ProjectBuildConfig>();
            config.projectName = "Queen's Garden 6";
            config.buildOutputRoot = "C:/Builds";

            var profile = new PublisherProfile(Publisher.GameHouse, "GameHouse", PlatformType.Windows32)
            {
                outputSubfolder = "Publishers/GameHouse/"
            };

            var context = BuildContextFactory.Create(config, profile);
            context.ResolveOutputPaths();

            Assert.That(context.Platform, Is.EqualTo(PlatformType.Windows32));
            Assert.That(context.ResolvedOutputFilePath, Does.EndWith(".exe"));
            Assert.That(context.ResolvedOutputDirectory.Replace('\\', '/'), Does.Contain("Publishers/GameHouse"));
        }

        [Test]
        public void SteamWindows32GetsItsOwnPlatformFolder()
        {
            var steam = new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows32)
            {
                outputSubfolder = "Publishers/Steam/"
            };

            Assert.That(ProfileOutputPaths.EffectiveSubfolder(steam, PlatformType.Windows32),
                Is.EqualTo("Publishers/Steam/Windows32"));
        }
    }
}
