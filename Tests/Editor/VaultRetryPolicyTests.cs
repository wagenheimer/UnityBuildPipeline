using System;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;

using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    /// <summary>
    /// The vault shares AppDeployHub's API rate limit (60 requests per IP per minute), so a build machine that
    /// spends that budget on its own API traffic gets HTTP 429 on a single keystore fetch. These tests pin the
    /// retry policy that rides out the window instead of failing the build.
    /// </summary>
    public sealed class VaultRetryPolicyTests
    {
        [TestCase(408, true)]
        [TestCase(429, true)]
        [TestCase(500, true)]
        [TestCase(502, true)]
        [TestCase(503, true)]
        [TestCase(400, false)]
        [TestCase(401, false)]
        [TestCase(403, false)]
        [TestCase(404, false)]
        [TestCase(409, false)]
        public void RetryableStatus_MatchesOnlyTransientFailures(int statusCode, bool expected) =>
            Assert.AreEqual(expected, AppDeployHubVaultClient.IsRetryableStatus(statusCode));

        [Test]
        public void RetryDelay_EscalatesAndThenKeepsTheLastStep()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(5), AppDeployHubVaultClient.RetryDelay(1, null));
            Assert.AreEqual(TimeSpan.FromSeconds(15), AppDeployHubVaultClient.RetryDelay(2, null));
            Assert.AreEqual(TimeSpan.FromSeconds(35), AppDeployHubVaultClient.RetryDelay(3, null));
            Assert.AreEqual(TimeSpan.FromSeconds(35), AppDeployHubVaultClient.RetryDelay(9, null));
        }

        [Test]
        public void RetryDelay_HonorsRetryAfterButCapsItAtOneMinute()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(20), AppDeployHubVaultClient.RetryDelay(1, TimeSpan.FromSeconds(20)));
            Assert.AreEqual(TimeSpan.FromSeconds(60), AppDeployHubVaultClient.RetryDelay(1, TimeSpan.FromMinutes(10)));
        }

        [Test]
        public void RetryDelay_IgnoresZeroOrNegativeRetryAfter()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(5), AppDeployHubVaultClient.RetryDelay(1, TimeSpan.Zero));
            Assert.AreEqual(TimeSpan.FromSeconds(5), AppDeployHubVaultClient.RetryDelay(1, TimeSpan.FromSeconds(-3)));
        }

        [Test]
        public void RetryableException_CoversNetworkFailuresButNotAuthOrFormatErrors()
        {
            Assert.IsTrue(AppDeployHubVaultClient.IsRetryableException(new HttpRequestException("reset")));
            Assert.IsTrue(AppDeployHubVaultClient.IsRetryableException(new TaskCanceledException("timeout")));
            Assert.IsTrue(AppDeployHubVaultClient.IsRetryableException(new AggregateException(new HttpRequestException())));
            Assert.IsFalse(AppDeployHubVaultClient.IsRetryableException(new InvalidOperationException("json")));
        }

        [Test]
        public void TotalRetryWindow_IsLongEnoughForTheOneMinuteLimitToReset()
        {
            TimeSpan total = TimeSpan.Zero;
            for (int attempt = 1; attempt < AppDeployHubVaultClient.MaxAttempts; attempt++)
                total += AppDeployHubVaultClient.RetryDelay(attempt, null);

            Assert.GreaterOrEqual(total, TimeSpan.FromSeconds(55));
        }
    }
}
