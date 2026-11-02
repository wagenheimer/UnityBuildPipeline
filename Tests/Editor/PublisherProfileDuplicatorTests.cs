using NUnit.Framework;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class PublisherProfileDuplicatorTests
    {
        [Test]
        public void FreeToFullAdjustsEverything()
        {
            var source = Profiles.Mobile(
                Publisher.iOSFree, "iOSFree", PlatformType.iOS, false,
                "com.example.game", "Builds/iOS Free/", "ADS", "TRACK", "DEMO_FREE");
            source.displayName = "iOS (Free)";

            var clone = PublisherProfileDuplicator.Create(source, "ADS", "TRACK");

            Assert.NotNull(clone);
            Assert.AreEqual("iOSFull", clone.id);
            Assert.AreEqual("iOS (Full)", clone.displayName);
            Assert.AreEqual(Publisher.iOSFull, clone.publisher);
            Assert.IsTrue(clone.isFullGame);
            Assert.AreEqual("com.example.gameFull", clone.bundleIdentifier);
            Assert.AreEqual("Builds/iOS Full/", clone.outputSubfolder);
            CollectionAssert.Contains(clone.scriptingDefines, "!ADS");
            CollectionAssert.DoesNotContain(clone.scriptingDefines, "ADS");
            CollectionAssert.DoesNotContain(clone.scriptingDefines, "TRACK");
            CollectionAssert.Contains(clone.scriptingDefines, "DEMO_FREE");
        }

        [Test]
        public void FullToFreeAddsAdsAndTracking()
        {
            var source = Profiles.Mobile(
                Publisher.iOSFull, "iOSFull", PlatformType.iOS, true,
                "com.example.gameFull", "Builds/iOS/");
            source.displayName = "iOS (Full)";

            var clone = PublisherProfileDuplicator.Create(source, "ADS", "TRACK");

            Assert.NotNull(clone);
            Assert.AreEqual("iOSFree", clone.id);
            Assert.AreEqual("iOS (Free)", clone.displayName);
            Assert.AreEqual(Publisher.iOSFree, clone.publisher);
            Assert.IsFalse(clone.isFullGame);
            Assert.AreEqual("com.example.game", clone.bundleIdentifier);
            Assert.AreEqual("Builds/iOS Free/", clone.outputSubfolder);
            CollectionAssert.Contains(clone.scriptingDefines, "ADS");
            CollectionAssert.Contains(clone.scriptingDefines, "TRACK");
        }

        [Test]
        public void AndroidFullRemovesTheAdsDefine()
        {
            var source = Profiles.Mobile(
                Publisher.GoogleAndroidFree, "GoogleAndroidFree", PlatformType.Android, false,
                "com.example.game", "Google/Free/", "ADS");

            var clone = PublisherProfileDuplicator.Create(source, "ADS", "TRACK");

            Assert.NotNull(clone);
            Assert.AreEqual("GoogleAndroidFull", clone.id);
            Assert.AreEqual("com.example.gameFull", clone.bundleIdentifier);
            Assert.AreEqual("Google/Full/", clone.outputSubfolder);
            CollectionAssert.Contains(clone.scriptingDefines, "!ADS");
            CollectionAssert.DoesNotContain(clone.scriptingDefines, "TRACK");
        }

        [Test]
        public void CounterpartsAreMapped()
        {
            Assert.IsTrue(PublisherProfileDuplicator.TryGetCounterpart(Publisher.iOSFree, out var full));
            Assert.AreEqual(Publisher.iOSFull, full);

            Assert.IsTrue(PublisherProfileDuplicator.TryGetCounterpart(Publisher.GoogleAndroidFull, out var free));
            Assert.AreEqual(Publisher.GoogleAndroidFree, free);

            Assert.IsFalse(PublisherProfileDuplicator.TryGetCounterpart(Publisher.Steam, out _));
        }

        [Test]
        public void ProfileWithoutCounterpartReturnsNull()
        {
            var source = Profiles.Mobile(Publisher.Steam, "Steam", PlatformType.Windows64, true, "com.example");

            Assert.IsNull(PublisherProfileDuplicator.Create(source, "ADS", "TRACK"));
        }
    }
}
