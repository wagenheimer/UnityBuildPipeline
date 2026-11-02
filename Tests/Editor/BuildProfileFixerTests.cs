using NUnit.Framework;
using UnityEngine;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class BuildProfileFixerTests
    {
        private static BuildProfileIssue Issue(string code) => new BuildProfileIssue
        {
            Code = code,
            Severity = BuildProfileIssueSeverity.Error,
            ProfileId = "p"
        };

        [Test]
        public void IdImplicitIsFixed()
        {
            var profile = Profiles.Mobile(Publisher.iOSFree, "", PlatformType.iOS, false);

            Assert.IsTrue(BuildProfileFixer.TryFix(null, profile, Issue("id-implicit"), null));
            Assert.AreEqual("iOSFree", profile.id);
        }

        [Test]
        public void FullWithAdsBecomesARemoval()
        {
            var profile = Profiles.Mobile(Publisher.iOSFull, "iOSFull", PlatformType.iOS, true, "com.x", "", "ADS");

            Assert.IsTrue(BuildProfileFixer.TryFix(null, profile, Issue("full-with-ads"), Profiles.Tracking()));
            CollectionAssert.Contains(profile.scriptingDefines, "!ADS");
            CollectionAssert.DoesNotContain(profile.scriptingDefines, "ADS");
        }

        [Test]
        public void FreeWithoutAdsGetsTheDefine()
        {
            var profile = Profiles.Mobile(Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.x");

            Assert.IsTrue(BuildProfileFixer.TryFix(null, profile, Issue("free-without-ads"), Profiles.Tracking()));
            CollectionAssert.Contains(profile.scriptingDefines, "ADS");
        }

        [Test]
        public void BundleMissingUsesTheDefaultBundle()
        {
            var config = Profiles.Config(null);
            config.defaultBundleIdentifier = "com.base";

            try
            {
                var full = Profiles.Mobile(Publisher.iOSFull, "iOSFull", PlatformType.iOS, true, "");
                var free = Profiles.Mobile(Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "");

                Assert.IsTrue(BuildProfileFixer.TryFix(config, full, Issue("bundle-missing"), null));
                Assert.AreEqual("com.baseFull", full.bundleIdentifier);

                Assert.IsTrue(BuildProfileFixer.TryFix(config, free, Issue("bundle-missing"), null));
                Assert.AreEqual("com.base", free.bundleIdentifier);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void BundleDuplicateAppendsTheSuffix()
        {
            var profile = Profiles.Mobile(Publisher.iOSFull, "iOSFull", PlatformType.iOS, true, "com.base");

            Assert.IsTrue(BuildProfileFixer.TryFix(null, profile, Issue("bundle-duplicate"), null));
            Assert.AreEqual("com.baseFull", profile.bundleIdentifier);
        }

        [Test]
        public void OutputDuplicateGetsAMarker()
        {
            var profile = Profiles.Mobile(Publisher.iOSFull, "iOSFull", PlatformType.iOS, true, "com.base", "Builds/iOS/");

            Assert.IsTrue(BuildProfileFixer.TryFix(null, profile, Issue("output-duplicate"), null));
            Assert.AreEqual("Builds/iOS Full/", profile.outputSubfolder);
        }

        [Test]
        public void UnknownPresetIsRemoved()
        {
            var profile = Profiles.Mobile(Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.x");
            profile.definePresets.Add("missing");

            var config = Profiles.Config(null, profile);

            try
            {
                Assert.IsTrue(BuildProfileFixer.TryFix(config, profile, Issue("preset-missing"), null));
                CollectionAssert.IsEmpty(profile.definePresets);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DefineConflictKeepsTheAddition()
        {
            var profile = Profiles.Mobile(
                Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.x", "", "ADS", "!ADS");

            Assert.IsTrue(BuildProfileFixer.TryFix(null, profile, Issue("define-conflict"), null));
            CollectionAssert.Contains(profile.scriptingDefines, "ADS");
            CollectionAssert.DoesNotContain(profile.scriptingDefines, "!ADS");
        }

        [Test]
        public void InvalidDefineHasNoAutomaticFix()
        {
            Assert.IsFalse(BuildProfileFixer.CanFix("define-invalid"));
        }
    }
}
