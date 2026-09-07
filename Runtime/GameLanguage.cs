using System;
using System.Collections.Generic;

public enum GameLanguage
{
    AutoDetect = 0,
    English = 1,
    French = 2,
    German = 3,
    Portuguese = 4,
    Spanish = 5,
    Italian = 6,
    Dutch = 7,     // NL
    Polish = 8,    // PL
    Czech = 9,     // CZ
    Russian = 10,  // RU
    Japanese = 11, // JA
    Chinese = 12   // ZH
}

public static class PublisherAndLanguageExtensions
{
    public static readonly List<GameLanguage> ListLanguages = new List<GameLanguage>
    {
        GameLanguage.AutoDetect,
        GameLanguage.English,
        GameLanguage.French,
        GameLanguage.German,
        GameLanguage.Spanish,
        GameLanguage.Dutch,
        GameLanguage.Italian,
        GameLanguage.Portuguese,
        GameLanguage.Russian,
        GameLanguage.Polish,
        GameLanguage.Czech,
        GameLanguage.Japanese,
        GameLanguage.Chinese
    };

    public static readonly List<Publisher> SitePublishers = new List<Publisher>
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
        Publisher.ItchIO
    };
}

// Backward compatibility for existing code typo
public static class PublisherAndLanguageExternsions
{
    public static List<GameLanguage> ListLanguages => PublisherAndLanguageExtensions.ListLanguages;
    public static List<Publisher> SitePublishers => PublisherAndLanguageExtensions.SitePublishers;
}

namespace Wagenheimer.BuildPipeline
{
    public static class LanguageExtensions
    {
        public static string AsLanguageCode(this GameLanguage lang)
        {
            return lang switch
            {
                GameLanguage.English => "en",
                GameLanguage.French => "fr",
                GameLanguage.German => "de",
                GameLanguage.Portuguese => "ptbr",
                GameLanguage.Spanish => "es",
                GameLanguage.Italian => "it",
                GameLanguage.Dutch => "nl",
                GameLanguage.Polish => "pl",
                GameLanguage.Czech => "cz",
                GameLanguage.Russian => "ru",
                GameLanguage.Japanese => "ja",
                GameLanguage.Chinese => "zh",
                _ => ""
            };
        }

        public static string AsLanguageName(this GameLanguage lang)
        {
            return lang switch
            {
                GameLanguage.English => "English",
                GameLanguage.French => "French",
                GameLanguage.German => "German",
                GameLanguage.Portuguese => "Portuguese",
                GameLanguage.Spanish => "Spanish",
                GameLanguage.Italian => "Italian",
                GameLanguage.Dutch => "Dutch",
                GameLanguage.Polish => "Polish",
                GameLanguage.Czech => "Czech",
                GameLanguage.Russian => "Russian",
                GameLanguage.Japanese => "Japanese",
                GameLanguage.Chinese => "Chinese",
                _ => "Auto"
            };
        }

        public static string FolderNameLanguage(this GameLanguage language)
        {
            var code = language.AsLanguageCode().ToUpper();
            return !string.IsNullOrEmpty(code) ? $"_{code}" : "";
        }
    }
}
