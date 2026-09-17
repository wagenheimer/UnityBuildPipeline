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
    Chinese = 12,  // ZH
    Korean = 13    // KR
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
                GameLanguage.Korean => "ko",
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
                GameLanguage.Korean => "Korean",
                _ => "Auto"
            };
        }

        public static string FolderNameLanguage(this GameLanguage language)
        {
            var code = language.AsLanguageCode().ToUpper();
            return !string.IsNullOrEmpty(code) ? $"_{code}" : "";
        }

        /// <summary>
        /// Maps a (partial, case-insensitive) language display name to a <see cref="GameLanguage"/>.
        /// Falls back to English when nothing matches.
        /// </summary>
        public static GameLanguage AsGameLanguage(this string lang)
        {
            if (string.IsNullOrEmpty(lang)) return GameLanguage.English;

            var lower = lang.ToLower();
            if (lower.Contains("english")) return GameLanguage.English;
            if (lower.Contains("french")) return GameLanguage.French;
            if (lower.Contains("portuguese")) return GameLanguage.Portuguese;
            if (lower.Contains("german")) return GameLanguage.German;
            if (lower.Contains("spanish")) return GameLanguage.Spanish;
            if (lower.Contains("italian")) return GameLanguage.Italian;
            if (lower.Contains("dutch")) return GameLanguage.Dutch;
            if (lower.Contains("polish")) return GameLanguage.Polish;
            if (lower.Contains("czech")) return GameLanguage.Czech;
            if (lower.Contains("russian")) return GameLanguage.Russian;
            if (lower.Contains("japanese")) return GameLanguage.Japanese;
            if (lower.Contains("chinese")) return GameLanguage.Chinese;
            if (lower.Contains("korean")) return GameLanguage.Korean;

            return GameLanguage.English;
        }

        /// <summary>
        /// Sprite name (without path/extension) for the flag of the given language, e.g. "Usa", "France".
        /// Games use these names for their flag sprites in the atlas.
        /// </summary>
        public static string AsFlagSprite(this string langtext)
        {
            var lang = langtext.AsGameLanguage();

            switch (lang)
            {
                case GameLanguage.English: return "Usa";
                case GameLanguage.French: return "France";
                case GameLanguage.German: return "Germany";
                case GameLanguage.Portuguese: return "Brazil";
                case GameLanguage.Spanish: return "Spain";
                case GameLanguage.Italian: return "Italy";
                case GameLanguage.Dutch: return "Netherlands";
                case GameLanguage.Polish: return "Poland";
                case GameLanguage.Czech: return "Czech-Republic";
                case GameLanguage.Russian: return "Russia";
                case GameLanguage.Japanese: return "Japan";
                case GameLanguage.Chinese: return "China";
                case GameLanguage.Korean: return "South-Korea";
                default: return "Usa";
            }
        }
    }
}
