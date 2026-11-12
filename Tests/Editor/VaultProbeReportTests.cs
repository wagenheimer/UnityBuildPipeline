using System;

using NUnit.Framework;

using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    /// <summary>
    /// The probe report is what the signing section and the menu dialog show when the vault test runs, so it has to
    /// carry everything needed to diagnose a build (which credential, which file, how long) and no secret at all.
    /// </summary>
    public sealed class VaultProbeReportTests
    {
        [Test]
        public void Report_ListsCredentialFileAndTimings()
        {
            string cachePath = "Library/KeystoreCache/93d7e1cf.keystore";

            string report = AppDeployHubVaultClient.BuildProbeReport("Release", "forgottentales", 4096, cachePath,
                2, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(340));

            StringAssert.Contains("2 credencial(is)", report);
            StringAssert.Contains("Release", report);
            StringAssert.Contains("forgottentales", report);
            StringAssert.Contains("4.0 KB", report);
            StringAssert.Contains(cachePath, report);
            StringAssert.Contains("120 ms", report);
            StringAssert.Contains("340 ms", report);
        }

        [Test]
        public void Report_NeverCarriesPasswords()
        {
            string report = AppDeployHubVaultClient.BuildProbeReport("Release", "release", 1024, "Library/KeystoreCache/x.keystore",
                1, TimeSpan.Zero, TimeSpan.Zero);

            StringAssert.DoesNotContain("password", report);
            StringAssert.Contains("Senhas: recebidas", report);
        }

        [Test]
        public void FormatBytes_UsesTheReadableUnit()
        {
            Assert.AreEqual("512 B", AppDeployHubVaultClient.FormatBytes(512));
            Assert.AreEqual("2.0 KB", AppDeployHubVaultClient.FormatBytes(2048));
            Assert.AreEqual("5.0 MB", AppDeployHubVaultClient.FormatBytes(5 * 1024 * 1024));
        }
    }
}
