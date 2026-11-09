using System.IO;
using System.Linq;
using NUnit.Framework;
using Wagenheimer.BuildPipeline.Editor;

namespace Wagenheimer.BuildPipeline.Tests
{
    public sealed class SteamAutoCloudTests
    {
        private const string Leaf = "Secrets of Magic 3 - Happy Halloween (Steam)";

        private static SteamAutoCloudConfig GoodConfig() => new SteamAutoCloudConfig
        {
            enabled = true,
            filePattern = "*.sav",
            recursive = true,
            windowsSubdirectory = $"Green Sauce Games/{Leaf}/",
            macSubdirectory = $@"Green Sauce Games\{Leaf}\ ",
            linuxSubdirectory = $".config/unity3d/Green Sauce Games/{Leaf}"
        };

        [Test]
        public void Rows_AreOneWindowsRootPlusTwoOverrides()
        {
            var rows = SteamAutoCloudPlanner.Rows(GoodConfig());

            Assert.That(rows, Has.Count.EqualTo(3));

            Assert.That(rows[0].IsOverride, Is.False);
            Assert.That(rows[0].Root, Is.EqualTo(SteamAutoCloudPlanner.WindowsRoot));
            Assert.That(rows[0].Subdirectory, Is.EqualTo($"Green Sauce Games/{Leaf}"));
            Assert.That(rows[0].FilePattern, Is.EqualTo("*.sav"));
            Assert.That(rows[0].Recursive, Is.True);
            Assert.That(rows[0].Label, Does.Contain("Root Path"));

            Assert.That(rows[1].IsOverride, Is.True);
            Assert.That(rows[1].OriginalRoot, Is.EqualTo(SteamAutoCloudPlanner.WindowsRoot));
            Assert.That(rows[1].Root, Is.EqualTo(SteamAutoCloudPlanner.MacRoot));
            Assert.That(rows[1].Subdirectory, Is.EqualTo($"Green Sauce Games/{Leaf}"));
            Assert.That(rows[1].Os, Does.Contain("macOS"));

            Assert.That(rows[2].IsOverride, Is.True);
            Assert.That(rows[2].Root, Is.EqualTo(SteamAutoCloudPlanner.LinuxRoot));
            Assert.That(rows[2].Subdirectory, Is.EqualTo($".config/unity3d/Green Sauce Games/{Leaf}"));
            Assert.That(rows[2].Os, Does.Contain("Linux"));
        }

        [Test]
        public void Normalize_TrimsSeparatorsAndConvertsBackslashes()
        {
            Assert.That(SteamAutoCloudPlanner.Normalize(@"  Green Sauce Games\My Game\ "), Is.EqualTo("Green Sauce Games/My Game"));
            Assert.That(SteamAutoCloudPlanner.Normalize("//a//b//"), Is.EqualTo("a/b"));
            Assert.That(SteamAutoCloudPlanner.Normalize(null), Is.Empty);
            Assert.That(SteamAutoCloudPlanner.Leaf(@"Green Sauce Games\My Game\ "), Is.EqualTo("My Game"));
        }

        [Test]
        public void GoodConfig_HasNoWarnings_AndTheTableHasEveryRow()
        {
            Assert.That(SteamAutoCloudPlanner.Check(GoodConfig(), steamEnabled: true).Where(i => i.IsWarning), Is.Empty);

            var table = SteamAutoCloudPlanner.SteamworksTable(GoodConfig());
            Assert.That(table, Does.Contain("WinAppDataRoaming"));
            Assert.That(table, Does.Contain("MacAppSupport"));
            Assert.That(table, Does.Contain("LinuxHome"));
            Assert.That(table, Does.Contain("Cross-Platform"));
            Assert.That(table, Does.Contain("Cross-platform sync: Yes"));
            Assert.That(table, Does.Contain("Replace path: yes"));
            Assert.That(table, Does.Contain("testappcloudpaths"));
        }

        [Test]
        public void Check_FlagsMacPersistentDataPath_AndBundleId_AndCaches()
        {
            var config = GoodConfig();
            config.macSubdirectory = "unity.Green Sauce Games.Secrets of Magic 3: Happy Halloween";
            Assert.That(Codes(config), Does.Contain("autocloud-mac-persistent-data"));

            config.macSubdirectory = "com.greensaucegames.secretsofmagic3";
            Assert.That(Codes(config), Does.Contain("autocloud-mac-bundle-id"));

            config.macSubdirectory = "Caches/Green Sauce Games";
            Assert.That(Codes(config), Does.Contain("autocloud-mac-caches"));
        }

        [Test]
        public void Check_FlagsEmptyFolders_BroadPattern_AndInvalidWindowsName()
        {
            var config = GoodConfig();
            config.windowsSubdirectory = "";
            config.linuxSubdirectory = "";
            config.filePattern = "*";
            var codes = Codes(config);
            Assert.That(codes, Does.Contain("autocloud-windows-empty"));
            Assert.That(codes, Does.Contain("autocloud-linux-empty"));
            Assert.That(codes, Does.Contain("autocloud-pattern-broad"));

            config = GoodConfig();
            config.windowsSubdirectory = "Green Sauce Games/Secrets of Magic 3: Happy Halloween";
            Assert.That(Codes(config), Does.Contain("autocloud-windows-invalid"));
        }

        [Test]
        public void Check_IsQuietWhenTheFeatureIsOff_AndWhenSteamIsOffItWarns()
        {
            Assert.That(SteamAutoCloudPlanner.Check(new SteamAutoCloudConfig(), true), Is.Empty);
            Assert.That(SteamAutoCloudPlanner.Check(null, true), Is.Empty);
            Assert.That(Codes(GoodConfig(), steamEnabled: false), Does.Contain("autocloud-steam-off"));
        }

        [Test]
        public void LocalFolder_ResolvesTheFolderPerOs()
        {
            var config = GoodConfig();

            Assert.That(
                SteamAutoCloudPlanner.LocalFolder(config, SteamAutoCloudPlatform.Windows,
                    userProfile: "/home/player", appData: "C:/Users/player/AppData/Roaming"),
                Is.EqualTo(Path.Combine("C:/Users/player/AppData/Roaming", "Green Sauce Games", Leaf)).IgnoreCase);

            Assert.That(
                SteamAutoCloudPlanner.LocalFolder(config, SteamAutoCloudPlatform.Mac, userProfile: "/Users/player"),
                Is.EqualTo(Path.Combine("/Users/player", "Library", "Application Support", "Green Sauce Games", Leaf)).IgnoreCase);

            Assert.That(
                SteamAutoCloudPlanner.LocalFolder(config, SteamAutoCloudPlatform.Linux, userProfile: "/home/player"),
                Is.EqualTo(Path.Combine("/home/player", ".config", "unity3d", "Green Sauce Games", Leaf)).IgnoreCase);
        }

        [Test]
        public void PhysicalPath_ShowsTheSteamworksRootOnEveryOs()
        {
            var config = GoodConfig();

            Assert.That(SteamAutoCloudPlanner.PhysicalPath(config, SteamAutoCloudPlatform.Windows),
                Is.EqualTo($"%APPDATA%/Green Sauce Games/{Leaf}"));
            Assert.That(SteamAutoCloudPlanner.PhysicalPath(config, SteamAutoCloudPlatform.Mac),
                Is.EqualTo($"~/Library/Application Support/Green Sauce Games/{Leaf}"));
            Assert.That(SteamAutoCloudPlanner.PhysicalPath(config, SteamAutoCloudPlatform.Linux),
                Is.EqualTo($"~/.config/unity3d/Green Sauce Games/{Leaf}"));
        }

        private static string[] Codes(SteamAutoCloudConfig config, bool steamEnabled = true) =>
            SteamAutoCloudPlanner.Check(config, steamEnabled).Select(i => i.Code).ToArray();
    }
}
