using System;
using System.Collections.Generic;

public enum Publisher
{
    Default = 0,
    BigFish = 1,
    MacAppStore = 2,
    GameHouse = 3,
    IWIN = 4,
    Alawar = 5,
    Immanitas = 6,
    WindowsStore = 7,
    GreenSauceGames = 8,
    SAD = 9,
    Wildtangent = 10,
    Intenium = 11,
    MacGameStore = 12,

    GoogleAndroidFree = 13,
    GoogleAndroidFull = 14,
    iOSFree = 15,
    iOSFull = 16,
    AmazonAndroidFree = 17,
    AmazonAndroidFull = 18,

    Steam = 19,
    MacAppStoreFull = 20,
    WindowsStoreFull = 22,

    NintendoSwitch = 25,
    SunRise = 70,

    SamsungFull = 101,
    SamsungFree = 1012,

    Denda = 125,
    Gamigo = 126,
    LegacyGames = 127,

    ItchIO = 200
}

public static class PublisherCatalog
{
    public static readonly List<Publisher> DesktopPublishers = new List<Publisher>
    {
        Publisher.GreenSauceGames,
        Publisher.BigFish,
        Publisher.GameHouse,
        Publisher.SAD,
        Publisher.IWIN,
        Publisher.Alawar,
        Publisher.Immanitas,
        Publisher.Wildtangent,
        Publisher.Intenium,
        Publisher.SunRise,
        Publisher.Denda,
        Publisher.Gamigo,
        Publisher.LegacyGames,
        Publisher.Steam,
        Publisher.ItchIO,
        Publisher.MacGameStore
    };

    public static readonly List<Publisher> MobilePublishers = new List<Publisher>
    {
        Publisher.GoogleAndroidFree,
        Publisher.GoogleAndroidFull,
        Publisher.AmazonAndroidFree,
        Publisher.AmazonAndroidFull,
        Publisher.iOSFree,
        Publisher.iOSFull,
        Publisher.SamsungFree,
        Publisher.SamsungFull
    };

    public static bool IsAndroid(this Publisher publisher)
    {
        return publisher is Publisher.GoogleAndroidFree or Publisher.GoogleAndroidFull
            or Publisher.AmazonAndroidFree or Publisher.AmazonAndroidFull
            or Publisher.SamsungFree or Publisher.SamsungFull;
    }

    public static bool IsIOS(this Publisher publisher)
    {
        return publisher is Publisher.iOSFree or Publisher.iOSFull;
    }

    public static bool IsMac(this Publisher publisher)
    {
        return publisher is Publisher.MacAppStore or Publisher.MacAppStoreFull or Publisher.MacGameStore;
    }

    public static bool IsStandaloneDesktop(this Publisher publisher)
    {
        return !publisher.IsAndroid() && !publisher.IsIOS();
    }
}
