using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    /// <summary>Pure define engine: expression evaluation, tags/rules, stack layers, source parsing, impact.</summary>
    public sealed class DefineEngineTests
    {
        private static HashSet<string> Set(params string[] symbols) => new HashSet<string>(symbols, StringComparer.Ordinal);

        // ── DefineExpression ─────────────────────────────────────────────────

        [TestCase("A", true)]
        [TestCase("!A", false)]
        [TestCase("A && B", false)]
        [TestCase("A || B", true)]
        [TestCase("A && !B", true)]
        [TestCase("(A || B) && C", true)]
        [TestCase("!(A && C)", false)]
        [TestCase("A == true", true)]
        [TestCase("B != false", false)]
        [TestCase("UNKNOWN", false)]
        [TestCase("A // trailing comment", true)]
        public void EvaluatesPreprocessorConditions(string expression, bool expected)
        {
            Assert.That(DefineExpression.Evaluate(expression, Set("A", "C")), Is.EqualTo(expected));
        }

        [Test]
        public void MalformedExpressionIsNotEvaluated()
        {
            Assert.That(DefineExpression.TryEvaluate("(A && ", Set("A"), out _), Is.False);
            Assert.That(DefineExpression.TryEvaluate("", Set("A"), out _), Is.False);
        }

        [Test]
        public void SymbolsSkipsLiteralsAndDuplicates()
        {
            Assert.That(DefineExpression.Symbols("A && (B || !A) && true"), Is.EqualTo(new[] { "A", "B" }));
        }

        // ── ProfileTags ──────────────────────────────────────────────────────

        [Test]
        public void TagsDescribeTheProfile()
        {
            var profile = new PublisherProfile(Publisher.iOSFree, "iOS Free", PlatformType.iOS, full: false) { id = "st-ios-free" };
            var tags = ProfileTags.For(profile);

            Assert.That(tags, Does.Contain("free").And.Contain("ios").And.Contain("mobile").And.Contain("id:st-ios-free").And.Contain("publisher:iOSFree"));
            Assert.That(tags, Does.Not.Contain("full").And.Not.Contain("steam"));
        }

        [Test]
        public void RulesSupportAndAndNegation()
        {
            var tags = Set("ios", "free", "mobile");
            Assert.That(ProfileTags.Matches("ios+free", tags), Is.True);
            Assert.That(ProfileTags.Matches("ios+full", tags), Is.False);
            Assert.That(ProfileTags.Matches("mobile+!android", tags), Is.True);
            Assert.That(ProfileTags.FirstMatch(new[] { "steam", "free" }, tags), Is.EqualTo("free"));
            Assert.That(ProfileTags.Matches("", tags), Is.False);
        }

        [Test]
        public void SteamConstructorSetsExplicitFlag()
        {
            Assert.That(new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows64).isSteam, Is.True);
            Assert.That(new PublisherProfile(Publisher.BigFish, "Big Fish", PlatformType.Windows64).isSteam, Is.False);
        }

        [Test]
        public void LegacySteamHeuristicStillRecognisesRenamedProfiles()
        {
            var demo = new PublisherProfile(Publisher.GreenSauceGames, "Steam Demo", PlatformType.Windows64);
            Assert.That(PublisherProfile.LooksLikeSteam(demo), Is.True);
            Assert.That(demo.isSteam, Is.False, "the flag is only filled by the migration");
        }

        // ── Stack ────────────────────────────────────────────────────────────

        [Test]
        public void StackAppliesRemovalsBeforeAdditionsAndRecordsOrigins()
        {
            var profile = new PublisherProfile(Publisher.GoogleAndroidFree, "Google Free", PlatformType.Android, full: false) { id = "gp-free" };
            profile.scriptingDefines = new List<string> { "ADS", "!DISABLESTEAMWORKS" };

            var stack = DefineStackResolver.Resolve(null, profile, new[] { "DOTWEEN;DISABLESTEAMWORKS" },
                new[] { new AutoDefine("AUTO_X", "test") }, new[] { "CLI_Y" }, "Android");

            Assert.That(stack.Final, Is.EqualTo(new[] { "DOTWEEN", "ADS", "AUTO_X", "CLI_Y" }));
            Assert.That(stack.Removed, Is.EqualTo(new[] { "DISABLESTEAMWORKS" }));
            Assert.That(stack.For("ADS").Single().layer, Is.EqualTo(DefineLayer.Profile));
            Assert.That(stack.For("DOTWEEN").Single().source, Is.EqualTo("PlayerSettings (Android)"));
            Assert.That(stack.For("AUTO_X").Single().layer, Is.EqualTo(DefineLayer.Auto));
            Assert.That(stack.For("CLI_Y").Single().layer, Is.EqualTo(DefineLayer.Cli));
        }

        [Test]
        public void CliAddOverridesAProfileRemovalAndIsReported()
        {
            var profile = new PublisherProfile(Publisher.iOSFull, "iOS Full", PlatformType.iOS) { id = "ios-full" };
            profile.scriptingDefines = new List<string> { "!ADS" };

            var stack = DefineStackResolver.Resolve(null, profile, new[] { "ADS" }, null, new[] { "ADS" });

            Assert.That(stack.Has("ADS"), Is.True);
            Assert.That(stack.CliOverrides, Is.EqualTo(new[] { "ADS" }));
            Assert.That(stack.Removed, Is.Empty, "a symbol put back by the CLI is not reported as removed");
        }

        [Test]
        public void ProfileWithoutDefinesKeepsOnlyGlobals()
        {
            var profile = new PublisherProfile(Publisher.BigFish, "Big Fish", PlatformType.Windows64);
            var stack = DefineStackResolver.Resolve(null, profile, new[] { "A", "B" });

            Assert.That(stack.Final, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(stack.Added, Is.Empty);
            Assert.That(stack.Removed, Is.Empty);
        }

        // ── Registry rules ───────────────────────────────────────────────────

        private static DefineRegistry Registry(params DefineDeclaration[] declarations)
        {
            var registry = new DefineRegistry();
            foreach (var declaration in declarations)
            {
                declaration.source = string.IsNullOrEmpty(declaration.source) ? "com.test" : declaration.source;
                registry.Add(declaration);
            }

            return registry;
        }

        [Test]
        public void RequiredDefineMissingIsAViolation()
        {
            var registry = Registry(new DefineDeclaration { symbol = "ADS", requiredWhen = new List<string> { "free" }, forbiddenWhen = new List<string> { "full" } });
            var free = new PublisherProfile(Publisher.GoogleAndroidFree, "Free", PlatformType.Android, full: false);
            var full = new PublisherProfile(Publisher.GoogleAndroidFull, "Full", PlatformType.Android);
            full.scriptingDefines = new List<string> { "ADS" };

            var freeViolations = DefineRules.Check(registry, free, DefineStackResolver.Resolve(null, free, new string[0]));
            var fullViolations = DefineRules.Check(registry, full, DefineStackResolver.Resolve(null, full, new string[0]));

            Assert.That(freeViolations.Single().Required, Is.True);
            Assert.That(fullViolations.Single().Required, Is.False);
            Assert.That(fullViolations.Single().Rule, Is.EqualTo("full"));
        }

        [Test]
        public void AssemblyScopedDeclarationsAreNotCheckedAgainstTheGlobalSet()
        {
            var registry = Registry(new DefineDeclaration { symbol = "PKG_LOCAL", scope = DefineScopes.Assembly, requiredWhen = new List<string> { "android" } });
            var profile = new PublisherProfile(Publisher.GoogleAndroidFull, "Full", PlatformType.Android);

            Assert.That(DefineRules.Check(registry, profile, DefineStackResolver.Resolve(null, profile, new string[0])), Is.Empty);
        }

        [Test]
        public void DuplicateDeclarationsAreReportedAsConflicts()
        {
            var registry = Registry(
                new DefineDeclaration { symbol = "X", source = "com.a" },
                new DefineDeclaration { symbol = "X", source = "com.b" });

            Assert.That(registry.Declarations.Count, Is.EqualTo(1));
            Assert.That(registry.Conflicts["X"], Is.EqualTo(new[] { "com.a", "com.b" }));
        }

        [Test]
        public void InvalidSymbolsAreIgnored()
        {
            var registry = Registry(new DefineDeclaration { symbol = "NOT VALID" }, new DefineDeclaration { symbol = "" });
            Assert.That(registry.Declarations, Is.Empty);
        }

        // ── Source parsing ───────────────────────────────────────────────────

        [Test]
        public void ParsesDirectivesConditionalsAndWriters()
        {
            const string code =
                "#if A && !B // why\n" +
                "  #elif C\n" +
                "#define LOCAL_X\n" +
                "#undef LOCAL_Y\n" +
                "[System.Diagnostics.Conditional(\"TRACE_ME\")] void F() {}\n" +
                "PlayerSettings.SetScriptingDefineSymbols(t, s);\n" +
                "// PlayerSettings.SetScriptingDefineSymbols(commented);\n";

            var usages = DefineUsageParser.ParseCSharp("Assets/X.cs", code);

            Assert.That(usages.Where(u => u.kind == DefineUsageKind.Condition).Select(u => u.symbol), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(usages.First(u => u.symbol == "A").expression, Is.EqualTo("A && !B"));
            Assert.That(usages.First(u => u.symbol == "C").line, Is.EqualTo(2));
            Assert.That(usages.Single(u => u.kind == DefineUsageKind.LocalDefine).symbol, Is.EqualTo("LOCAL_X"));
            Assert.That(usages.Single(u => u.kind == DefineUsageKind.LocalUndef).symbol, Is.EqualTo("LOCAL_Y"));
            Assert.That(usages.Single(u => u.kind == DefineUsageKind.ConditionalAttribute).symbol, Is.EqualTo("TRACE_ME"));
            Assert.That(usages.Count(u => u.kind == DefineUsageKind.GlobalWriter), Is.EqualTo(1));
        }

        [Test]
        public void IgnoresNonConditionalDirectives()
        {
            var usages = DefineUsageParser.ParseCSharp("Assets/X.cs", "#region Foo\n#pragma warning disable CS0618\n#endif\n#else\n");
            Assert.That(usages, Is.Empty);
        }

        [Test]
        public void ParsesResponseFiles()
        {
            var usages = DefineUsageParser.ParseResponseFile("Assets/csc.rsp", "-nowarn:0618\n-define:A;B\n/d:C\n");
            Assert.That(usages.Select(u => u.symbol), Is.EqualTo(new[] { "A", "B", "C" }));
        }

        // ── Impact ───────────────────────────────────────────────────────────

        [Test]
        public void ImpactListsOnlyLocationsThatChange()
        {
            var usages = DefineUsageParser.ParseCSharp("Assets/Ads.cs",
                "#if ADS\n#endif\n#if ADS || FULL\n#endif\n#if OTHER\n#endif\n#if !ADS\n#endif\n");

            var impact = DefineImpact.Compare(Set("ADS"), Set(), usages);

            Assert.That(impact.Select(i => i.Usage.line), Is.EqualTo(new[] { 1, 3, 7 }));
            Assert.That(impact.Single(i => i.Usage.line == 7).InA, Is.False);
            Assert.That(impact.Single(i => i.Usage.line == 7).InB, Is.True);
        }

        [Test]
        public void ToggleMeasuresRemovingOneSymbol()
        {
            var usages = DefineUsageParser.ParseCSharp("Assets/A.cs", "#if X && Y\n#endif\n#if Y\n#endif\n");
            Assert.That(DefineImpact.Toggle(Set("X", "Y"), "X", usages).Count, Is.EqualTo(1));
        }

        // ── Language ─────────────────────────────────────────────────────────

        [Test]
        public void OverrideBeatsLockAndLockBeatsDefault()
        {
            var locked = new PublisherProfile(Publisher.BigFish, "Big Fish DE", PlatformType.Windows64)
            {
                languageMode = ProfileLanguageMode.Locked,
                lockedLanguage = GameLanguage.German
            };
            var auto = new PublisherProfile(Publisher.BigFish, "Big Fish", PlatformType.Windows64);

            Assert.That(BuildLanguageResolver.Resolve(null, locked, null, out var lockSource), Is.EqualTo(GameLanguage.German));
            Assert.That(lockSource, Is.EqualTo(BuildLanguageSource.ProfileLock));

            Assert.That(BuildLanguageResolver.Resolve(null, locked, GameLanguage.French, out var overrideSource), Is.EqualTo(GameLanguage.French));
            Assert.That(overrideSource, Is.EqualTo(BuildLanguageSource.Override));

            Assert.That(BuildLanguageResolver.Resolve(null, auto, null, out var defaultSource), Is.EqualTo(GameLanguage.AutoDetect));
            Assert.That(defaultSource, Is.EqualTo(BuildLanguageSource.ProjectDefault));
        }

        [Test]
        public void MatrixBuildsALockedProfileOnceInItsLanguage()
        {
            var locked = new PublisherProfile(Publisher.BigFish, "DE", PlatformType.Windows64) { languageMode = ProfileLanguageMode.Locked, lockedLanguage = GameLanguage.German };
            var auto = new PublisherProfile(Publisher.BigFish, "Auto", PlatformType.Windows64);
            var selected = new[] { GameLanguage.English, GameLanguage.French };

            Assert.That(BuildLanguageResolver.MatrixLanguages(locked, selected), Is.EqualTo(new[] { GameLanguage.German }));
            Assert.That(BuildLanguageResolver.MatrixLanguages(auto, selected), Is.EqualTo(selected));
        }

        [Test]
        public void LockedToAutoDetectIsNotALock()
        {
            var profile = new PublisherProfile { languageMode = ProfileLanguageMode.Locked, lockedLanguage = GameLanguage.AutoDetect };
            Assert.That(profile.IsLanguageLocked, Is.False);
        }

        [TestCase("de", GameLanguage.German)]
        [TestCase("German", GameLanguage.German)]
        [TestCase("ptbr", GameLanguage.Portuguese)]
        [TestCase("auto", GameLanguage.AutoDetect)]
        [TestCase("AutoDetect", GameLanguage.AutoDetect)]
        public void ParsesLanguageNamesAndCodes(string value, GameLanguage expected)
        {
            Assert.That(BuildLanguageResolver.Parse(value), Is.EqualTo(expected));
        }

        [Test]
        public void UnknownLanguageParsesToNull()
        {
            Assert.That(BuildLanguageResolver.Parse("klingon"), Is.Null);
            Assert.That(BuildLanguageResolver.Parse(""), Is.Null);
        }
    }
}
