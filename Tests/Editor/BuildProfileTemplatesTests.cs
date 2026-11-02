using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class BuildProfileTemplatesTests
    {
        [Test]
        public void PlatformFollowsThePublisher()
        {
            Assert.AreEqual(PlatformType.iOS, BuildProfileTemplates.PlatformFor(Publisher.iOSFree));
            Assert.AreEqual(PlatformType.Android, BuildProfileTemplates.PlatformFor(Publisher.GoogleAndroidFree));
            Assert.AreEqual(PlatformType.macOS, BuildProfileTemplates.PlatformFor(Publisher.MacAppStore));
            Assert.AreEqual(PlatformType.Windows64, BuildProfileTemplates.PlatformFor(Publisher.Steam));
        }

        [Test]
        public void IdAndOutputUseTheFreeFullMarker()
        {
            Assert.AreEqual("GoogleAndroidFull", BuildProfileTemplates.SuggestedId(Publisher.GoogleAndroidFree, true));
            Assert.AreEqual("Builds/Publishers/iOSFree/Free/", BuildProfileTemplates.SuggestedOutput(Publisher.iOSFree, false));
            Assert.AreEqual("iOS (Full)", BuildProfileTemplates.SuggestedDisplayName(Publisher.iOSFull, true));
            Assert.AreEqual("Google Play (Free)", BuildProfileTemplates.SuggestedDisplayName(Publisher.GoogleAndroidFree, false));
        }

        [Test]
        public void BundleUsesTheDefaultPlusFull()
        {
            var config = Profiles.Config(null);
            config.defaultBundleIdentifier = "com.base";

            try
            {
                Assert.AreEqual("com.base", BuildProfileTemplates.SuggestedBundle(config, false));
                Assert.AreEqual("com.baseFull", BuildProfileTemplates.SuggestedBundle(config, true));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void CreatedFreeProfileAppliesAdsAndTracking()
        {
            var profile = BuildProfileTemplates.Create(
                Publisher.iOSFree, false, "iOSFree", "com.x", "", Profiles.Tracking());

            Assert.AreEqual(PlatformType.iOS, profile.platform);
            Assert.IsFalse(profile.isFullGame);
            CollectionAssert.Contains(profile.scriptingDefines, "ADS");
            CollectionAssert.Contains(profile.scriptingDefines, "TRACK");
        }

        [Test]
        public void CreatedFullProfileRemovesAds()
        {
            var profile = BuildProfileTemplates.Create(
                Publisher.iOSFull, true, "iOSFull", "com.xFull", "", Profiles.Tracking());

            Assert.IsTrue(profile.isFullGame);
            CollectionAssert.Contains(profile.scriptingDefines, "!ADS");
            CollectionAssert.DoesNotContain(profile.scriptingDefines, "ADS");
            CollectionAssert.DoesNotContain(profile.scriptingDefines, "TRACK");
        }
    }
}
