using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class BuildProfileValidatorTests
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

        private ProjectBuildConfig Config(params PublisherProfile[] profiles)
        {
            var config = Profiles.Config(null, profiles);
            _created.Add(config);
            return config;
        }

        private static bool HasError(List<BuildProfileIssue> issues, string code) =>
            issues.Any(issue => issue.Code == code && issue.Severity == BuildProfileIssueSeverity.Error);

        private static bool HasWarning(List<BuildProfileIssue> issues, string code) =>
            issues.Any(issue => issue.Code == code && issue.Severity == BuildProfileIssueSeverity.Warning);

        [Test]
        public void EmptyConfigReportsNoProfiles()
        {
            var issues = BuildProfileValidator.Validate(Config(), null);

            Assert.IsTrue(HasError(issues, "no-profiles"));
        }

        [Test]
        public void MobileProfileWithoutBundleIsAnError()
        {
            var config = Config(Profiles.Mobile(
                Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, ""));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsTrue(HasError(issues, "bundle-missing"));
        }

        [Test]
        public void DesktopProfileWithoutBundleIsNotAProblem()
        {
            var config = Config(Profiles.Mobile(
                Publisher.Steam, "Steam", PlatformType.Windows64, true, ""));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsFalse(issues.Any(issue => issue.Code == "bundle-missing"));
        }

        [Test]
        public void DuplicateIdIsAnError()
        {
            var config = Config(
                Profiles.Mobile(Publisher.iOSFree, "", PlatformType.iOS, false),
                Profiles.Mobile(Publisher.iOSFree, "", PlatformType.iOS, false));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsTrue(HasError(issues, "id-duplicate"));
        }

        [Test]
        public void SameStoreFreeAndFullSharingBundleIsAnError()
        {
            var config = Config(
                Profiles.Mobile(Publisher.GoogleAndroidFree, "GoogleAndroidFree", PlatformType.Android, false, "com.same"),
                Profiles.Mobile(Publisher.GoogleAndroidFull, "GoogleAndroidFull", PlatformType.Android, true, "com.same"));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsTrue(HasError(issues, "bundle-duplicate"));
        }

        [Test]
        public void DifferentStoresMayShareTheBundle()
        {
            var config = Config(
                Profiles.Mobile(Publisher.GoogleAndroidFree, "GoogleAndroidFree", PlatformType.Android, false, "com.same"),
                Profiles.Mobile(Publisher.AmazonAndroidFull, "AmazonAndroidFull", PlatformType.Android, true, "com.same"));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsFalse(HasError(issues, "bundle-duplicate"));
        }

        [Test]
        public void FullIosWithAdsDefineIsAnError()
        {
            var config = Config(Profiles.Mobile(
                Publisher.iOSFull, "iOSFull", PlatformType.iOS, true, "com.example.full", "", "ADS"));

            var issues = BuildProfileValidator.Validate(config, Profiles.Tracking());

            Assert.IsTrue(HasError(issues, "full-with-ads"));
        }

        [Test]
        public void FreeIosWithoutAdsDefineIsAWarning()
        {
            var config = Config(Profiles.Mobile(
                Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.example", "", "TRACK"));

            var issues = BuildProfileValidator.Validate(config, Profiles.Tracking());

            Assert.IsTrue(HasWarning(issues, "free-without-ads"));
        }

        [Test]
        public void InvalidDefineTokenIsAnError()
        {
            var config = Config(Profiles.Mobile(
                Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.example", "", "! ADS", "COM,MA"));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsTrue(HasError(issues, "define-invalid"));
        }

        [Test]
        public void DefineAddedAndRemovedIsAnError()
        {
            var config = Config(Profiles.Mobile(
                Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.example", "", "ADS", "!ADS"));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsTrue(HasError(issues, "define-conflict"));
        }

        [Test]
        public void UnknownPresetIsAnError()
        {
            var profile = Profiles.Mobile(Publisher.iOSFree, "iOSFree", PlatformType.iOS, false, "com.example");
            profile.definePresets.Add("missing");

            var config = Config(profile);

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsTrue(HasError(issues, "preset-missing"));
        }

        [Test]
        public void ValidMatrixHasNoErrors()
        {
            var config = Config(
                Profiles.Mobile(Publisher.iOSFree, "iOSFree", PlatformType.iOS, false,
                    "com.example", "Builds/iOS Free/", "ADS", "TRACK"),
                Profiles.Mobile(Publisher.iOSFull, "iOSFull", PlatformType.iOS, true,
                    "com.example.full", "Builds/iOS/"),
                Profiles.Mobile(Publisher.GoogleAndroidFree, "GoogleAndroidFree", PlatformType.Android, false,
                    "com.example", "Google/Free/", "ADS"),
                Profiles.Mobile(Publisher.GoogleAndroidFull, "GoogleAndroidFull", PlatformType.Android, true,
                    "com.example.full", "Google/Full/", "!ADS"));

            var issues = BuildProfileValidator.Validate(config, Profiles.Tracking());

            Assert.IsFalse(issues.Any(issue => issue.Severity == BuildProfileIssueSeverity.Error),
                string.Join("\n", issues.Select(issue => issue.ToString())));
        }

        [Test]
        public void SteamSiblingsMayShareTheSameOutputSubfolder()
        {
            var config = Config(
                SteamProfile("steam-windows", "Steam (Windows)", PlatformType.Windows64),
                SteamProfile("steam-linux", "Steam (Linux)", PlatformType.Linux64),
                SteamProfile("steam-mac", "Steam (Mac)", PlatformType.macOS));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsFalse(HasWarning(issues, "output-duplicate"),
                string.Join("\n", issues.Select(issue => issue.ToString())));
        }

        [Test]
        public void TwoSteamProfilesForTheSamePlatformStillWarnAboutTheOutput()
        {
            var config = Config(
                SteamProfile("steam-a", "Steam A", PlatformType.Windows64),
                SteamProfile("steam-b", "Steam B", PlatformType.Windows64));

            var issues = BuildProfileValidator.Validate(config, null);

            Assert.IsTrue(HasWarning(issues, "output-duplicate"));
        }

        private static PublisherProfile SteamProfile(string id, string name, PlatformType platform) =>
            new PublisherProfile(Publisher.Steam, name, platform)
            {
                id = id,
                outputSubfolder = "Builds/Publishers/Steam/"
            };
    }
}
