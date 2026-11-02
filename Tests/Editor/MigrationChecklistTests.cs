using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class MigrationChecklistTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
                if (asset != null) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        [Test]
        public void MissingConfigIsABlockingItem()
        {
            var items = MigrationChecklist.Build(null, null);
            Assert.That(items.Single().Blocking, Is.True);
        }

        [Test]
        public void LegacyRecordListsMovedFieldsAndManualReview()
        {
            var config = Profiles.Config(new BuildDefinePreset[0]);
            _created.Add(config);
            config.gameConfig = ScriptableObject.CreateInstance<GameConfig>();
            _created.Add(config.gameConfig);

            var record = new LegacyMigrationRecord
            {
                status = "done-unity",
                configClass = "FishWitchHalloweenConfig",
                movedFields = new List<string> { "MacAppStoreId" },
                manualReview = new List<string> { "RateControl.cs:790 Publisher.BigFishSE" },
                missingPublishers = new List<string> { "BigFishSE" }
            };

            var items = MigrationChecklist.Build(config, record);

            Assert.That(items.Any(i => i.Details.Any(d => d.Contains("MacAppStoreId"))), Is.True);
            Assert.That(items.Any(i => i.Blocking && i.Details.Contains("RateControl.cs:790 Publisher.BigFishSE")), Is.True);
            Assert.That(items.Any(i => i.Text.Contains("BigFishSE")), Is.True);
        }

        [Test]
        public void ClosedRecordAndCompleteConfigHaveNothingPending()
        {
            var config = Profiles.Config(new BuildDefinePreset[0]);
            _created.Add(config);
            config.gameConfig = ScriptableObject.CreateInstance<GameConfig>();
            _created.Add(config.gameConfig);

            Assert.That(MigrationChecklist.Build(config, new LegacyMigrationRecord { status = "closed" }), Is.Empty);
        }

        [Test]
        public void VaultWithoutCredentialBlocksAndroidProjects()
        {
            var android = Profiles.Mobile(Publisher.GoogleAndroidFull, "gp-full", PlatformType.Android, true);
            var config = Profiles.Config(new BuildDefinePreset[0], android);
            _created.Add(config);
            config.gameConfig = ScriptableObject.CreateInstance<GameConfig>();
            _created.Add(config.gameConfig);
            config.keystoreSource = KeystoreSource.AppDeployHubVault;

            var items = MigrationChecklist.Build(config, null);

            Assert.That(items.Single().Section, Is.EqualTo(BuildPipelineSections.Signing));
        }

        [Test]
        public void MigrationCardBuildsWithAndWithoutConfig()
        {
            Assert.DoesNotThrow(() => BuildPipelineWindow.MigrationCard(null));
            var config = Profiles.Config(new BuildDefinePreset[0]);
            _created.Add(config);
            Assert.DoesNotThrow(() => BuildPipelineWindow.MigrationCard(config, s => { }, () => { }));
        }
    }
}
