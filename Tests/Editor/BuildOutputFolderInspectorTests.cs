using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Wagenheimer.BuildPipeline;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class BuildOutputFolderInspectorTests
    {
        private string TempFolder(string name) =>
            Path.Combine(Path.GetTempPath(), "bp-inspector-" + name + "-" + System.Guid.NewGuid().ToString("N"));

        [Test]
        public void EmptyRootIsAnError()
        {
            var check = BuildOutputFolderInspector.InspectRoot("", prepareFolder: false);

            Assert.That(check.HasError, Is.True);
            Assert.That(check.Summary, Does.Contain("empty").IgnoreCase);
        }

        [Test]
        public void ExistingFolderIsValidAndWritable()
        {
            string folder = TempFolder("existing");
            Directory.CreateDirectory(folder);

            try
            {
                var check = BuildOutputFolderInspector.InspectRoot(folder, prepareFolder: true);

                Assert.That(check.HasError, Is.False);
                Assert.That(check.Exists, Is.True);
                Assert.That(check.Writable, Is.True);
                Assert.That(check.Created, Is.False);
                Assert.That(check.Severity, Is.EqualTo(BuildOutputCheckSeverity.Ok));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void MissingFolderIsCreatedOnlyWhenPreparing()
        {
            string folder = TempFolder("missing");

            try
            {
                var preview = BuildOutputFolderInspector.InspectRoot(folder, prepareFolder: false);
                Assert.That(preview.Exists, Is.False);
                Assert.That(preview.Created, Is.False);
                Assert.That(preview.HasError, Is.False);

                var prepared = BuildOutputFolderInspector.InspectRoot(folder, prepareFolder: true);
                Assert.That(prepared.Exists, Is.True);
                Assert.That(prepared.Created, Is.True);
                Assert.That(prepared.Writable, Is.True);
                Assert.That(Directory.Exists(folder), Is.True);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void FolderInsideProjectIsAWarning()
        {
            string folder = Path.Combine(Application.dataPath, "BuildPipelineOutputTest");

            var check = BuildOutputFolderInspector.InspectRoot(folder, prepareFolder: false);

            Assert.That(check.InsideProject, Is.True);
            Assert.That(check.Severity, Is.EqualTo(BuildOutputCheckSeverity.Warning));
        }

        [Test]
        public void SteamProfileCheckShowsThePlatformFolder()
        {
            var steam = new PublisherProfile(Publisher.Steam, "Steam (Linux)", PlatformType.Linux64)
            {
                id = "steam-linux",
                outputSubfolder = "Builds/Publishers/Steam/"
            };
            var config = Profiles.Config(null, steam);
            config.buildOutputRoot = Path.Combine(Path.GetTempPath(), "bp-inspector-root");
            config.macBuildOutputRoot = config.buildOutputRoot;

            try
            {
                var check = BuildOutputFolderInspector.Inspect(config, steam, PlatformType.Linux64, prepareFolder: false);

                Assert.That(check.UsesPlatformFolder, Is.True);
                Assert.That(check.PlatformFolder, Is.EqualTo("Linux"));
                StringAssert.EndsWith(Path.Combine("Builds", "Publishers", "Steam", "Linux"), check.BaseFolder);
                Assert.That(check.Details.Any(d => d.Contains("Automatic platform")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
