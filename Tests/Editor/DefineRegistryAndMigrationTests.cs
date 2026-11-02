using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    /// <summary>Parts that need Unity (JsonUtility, ScriptableObject): manifests, asmdefs, migration, factory.</summary>
    public sealed class DefineRegistryAndMigrationTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
                if (asset != null) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private ProjectBuildConfig Config(params PublisherProfile[] profiles)
        {
            var config = Profiles.Config(new BuildDefinePreset[0], profiles);
            _created.Add(config);
            return config;
        }

        [Test]
        public void ParsesTheWagenheimerBlockOfAPackageManifest()
        {
            const string json = @"{
                ""name"": ""com.wagenheimer.nativesocial"",
                ""version"": ""1.0.0"",
                ""wagenheimer"": { ""defines"": [
                    { ""symbol"": ""WAGENHEIMER_NATIVESOCIAL_STEAM"", ""description"": ""Steam calls"",
                      ""requiredWhen"": [""steam""], ""autoFromPackage"": ""com.rlabrecque.steamworks.net"" },
                    { ""symbol"": ""WAGENHEIMER_NATIVESOCIAL_GPGS"", ""scope"": ""assembly"" }
                ] }
            }";

            var declarations = DefineRegistry.ParsePackageManifest(json, "com.wagenheimer.nativesocial");

            Assert.That(declarations.Select(d => d.symbol), Is.EqualTo(new[] { "WAGENHEIMER_NATIVESOCIAL_STEAM", "WAGENHEIMER_NATIVESOCIAL_GPGS" }));
            Assert.That(declarations[0].requiredWhen, Is.EqualTo(new[] { "steam" }));
            Assert.That(declarations[0].IsAutomatic, Is.True);
            Assert.That(declarations[0].scope, Is.EqualTo(DefineScopes.Global));
            Assert.That(declarations[1].IsAssemblyScoped, Is.True);
            Assert.That(declarations.All(d => d.source == "com.wagenheimer.nativesocial"), Is.True);
        }

        [Test]
        public void ManifestWithoutBlockDeclaresNothing()
        {
            Assert.That(DefineRegistry.ParsePackageManifest(@"{ ""name"": ""com.unity.ugui"" }", "com.unity.ugui"), Is.Empty);
        }

        [Test]
        public void GameDefinesJoinTheRegistry()
        {
            var config = Config();
            config.gameDefines.Add(new DefineDeclaration { symbol = "ST2_ADS", requiredWhen = new List<string> { "free" } });

            var registry = DefineRegistry.Build(null, config);

            Assert.That(registry.Find("ST2_ADS").source, Is.EqualTo(DefineDeclaration.GameSource));
            Assert.That(config.gameDefines[0].source, Is.Empty.Or.Null, "the asset entry is not mutated");
        }

        [Test]
        public void ParsesAsmdefConstraintsAndVersionDefines()
        {
            const string json = @"{
                ""name"": ""Wagenheimer.NativeSocial"",
                ""defineConstraints"": [""!DISABLE_SOCIAL"", ""UNITY_IOS || UNITY_ANDROID""],
                ""versionDefines"": [ { ""name"": ""com.rlabrecque.steamworks.net"", ""expression"": """", ""define"": ""WAGENHEIMER_NATIVESOCIAL_STEAM"" } ]
            }";

            var usages = DefineUsageParser.ParseAsmdef("Packages/x/X.asmdef", json, out string assembly);

            Assert.That(assembly, Is.EqualTo("Wagenheimer.NativeSocial"));
            Assert.That(usages.Where(u => u.kind == DefineUsageKind.AssemblyConstraint).Select(u => u.symbol),
                Is.EqualTo(new[] { "DISABLE_SOCIAL", "UNITY_IOS", "UNITY_ANDROID" }));
            var versionDefine = usages.Single(u => u.kind == DefineUsageKind.AssemblyVersionDefine);
            Assert.That(versionDefine.symbol, Is.EqualTo("WAGENHEIMER_NATIVESOCIAL_STEAM"));
            Assert.That(versionDefine.versionDefinePackage, Is.EqualTo("com.rlabrecque.steamworks.net"));
        }

        [Test]
        public void MigrationFlagsLegacySteamProfilesOnce()
        {
            var renamed = new PublisherProfile(Publisher.GreenSauceGames, "Steam Demo", PlatformType.Windows64);
            var other = new PublisherProfile(Publisher.BigFish, "Big Fish", PlatformType.Windows64);
            var config = Config(renamed, other);
            config.schemaVersion = 0;

            Assert.That(ProjectBuildConfigMigrator.Migrate(config), Is.True);
            Assert.That(renamed.isSteam, Is.True);
            Assert.That(other.isSteam, Is.False);
            Assert.That(config.schemaVersion, Is.EqualTo(ProjectBuildConfigMigrator.CurrentSchema));

            renamed.isSteam = false;
            Assert.That(ProjectBuildConfigMigrator.Migrate(config), Is.False, "already migrated: a user's choice is kept");
            Assert.That(renamed.isSteam, Is.False);
        }

        [Test]
        public void FactoryUsesProfileLanguageLockAndPlatform()
        {
            var profile = new PublisherProfile(Publisher.BigFish, "Big Fish DE", PlatformType.Windows64)
            {
                id = "bigfish-de",
                languageMode = ProfileLanguageMode.Locked,
                lockedLanguage = GameLanguage.German,
                isDemo = true
            };
            var config = Config(profile);

            var context = BuildContextFactory.Create(config, profile);

            Assert.That(context.Language, Is.EqualTo(GameLanguage.German));
            Assert.That(context.LanguageSource, Is.EqualTo(BuildLanguageSource.ProfileLock));
            Assert.That(context.ProfileId, Is.EqualTo("bigfish-de"));
            Assert.That(context.Platform, Is.EqualTo(PlatformType.Windows64));
            Assert.That(context.Demo, Is.True);
            Assert.That(context.ScriptingDefines, Is.Empty, "profile defines are applied by the step, not copied");
        }

        [Test]
        public void FactoryPlatformOverrideIsUsedForSteamDesktopBuilds()
        {
            var steam = new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows64);
            var context = BuildContextFactory.Create(Config(steam), steam, new BuildContextFactory.Options { Platform = PlatformType.Linux64, AutoRun = true });

            Assert.That(context.Platform, Is.EqualTo(PlatformType.Linux64));
            Assert.That(context.AppBundle, Is.False, "auto-run forces an APK");
        }

        [Test]
        public void ValidatorFlagsRegistryViolationsAndUndocumentedDefines()
        {
            var free = new PublisherProfile(Publisher.GoogleAndroidFree, "Free", PlatformType.Android, full: false) { id = "gp-free", bundleIdentifier = "com.x.free" };
            free.scriptingDefines.Add("MYSTERY");
            var config = Config(free);
            config.gameDefines.Add(new DefineDeclaration { symbol = "ST2_ADS", requiredWhen = new List<string> { "free" } });
            var registry = DefineRegistry.Build(null, config);

            var issues = BuildProfileValidator.Validate(config, null, registry,
                p => DefineStackResolver.Resolve(config, p, new string[0]));

            Assert.That(issues.Any(i => i.Code == "define-required-missing" && i.Message.Contains("ST2_ADS")), Is.True);
            Assert.That(issues.Any(i => i.Code == "define-undocumented" && i.Message.Contains("MYSTERY")), Is.True);
        }

        [Test]
        public void ValidatorWarnsAboutSteamFlagMismatch()
        {
            var profile = new PublisherProfile(Publisher.GreenSauceGames, "Steam Playtest", PlatformType.Windows64) { id = "steam-playtest" };
            var issues = BuildProfileValidator.Validate(Config(profile), null);

            Assert.That(issues.Any(i => i.Code == "steam-unflagged"), Is.True);
        }
    }
}
