using System;
using System.Collections.Generic;
using System.Linq;

namespace Wagenheimer.BuildPipeline.Editor
{
    public enum BuildLanguageSource
    {
        /// <summary>Project default (<see cref="ProjectBuildConfig.defaultLanguage"/>).</summary>
        ProjectDefault,
        /// <summary>The profile is locked to one language.</summary>
        ProfileLock,
        /// <summary>One-off choice for this build (Quick Build picker, CLI -language). Never saved.</summary>
        Override
    }

    /// <summary>
    /// Single rule for the build language: an explicit one-off override wins, then the profile lock, then the
    /// project default. The matrix never overrides a locked profile (it builds the locked language once).
    /// </summary>
    public static class BuildLanguageResolver
    {
        public static GameLanguage Resolve(ProjectBuildConfig config, PublisherProfile profile, GameLanguage? oneOffOverride,
            out BuildLanguageSource source)
        {
            if (oneOffOverride.HasValue)
            {
                source = BuildLanguageSource.Override;
                return oneOffOverride.Value;
            }

            if (profile != null && profile.IsLanguageLocked)
            {
                source = BuildLanguageSource.ProfileLock;
                return profile.lockedLanguage;
            }

            source = BuildLanguageSource.ProjectDefault;
            return config != null ? config.defaultLanguage : GameLanguage.AutoDetect;
        }

        /// <summary>Languages the matrix builds for a profile: its locked language, or the selected ones.</summary>
        public static List<GameLanguage> MatrixLanguages(PublisherProfile profile, IEnumerable<GameLanguage> selected)
        {
            if (profile != null && profile.IsLanguageLocked)
                return new List<GameLanguage> { profile.lockedLanguage };

            return (selected ?? Enumerable.Empty<GameLanguage>()).Distinct().ToList();
        }

        /// <summary>Parses a language name or code (English, en, ptbr, AutoDetect). Null when unknown/empty.</summary>
        public static GameLanguage? Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string wanted = value.Trim();
            foreach (GameLanguage language in Enum.GetValues(typeof(GameLanguage)))
            {
                if (language.ToString().Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
                    (language.AsLanguageCode().Length > 0 && language.AsLanguageCode().Equals(wanted, StringComparison.OrdinalIgnoreCase)))
                    return language;
            }

            if (wanted.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return GameLanguage.AutoDetect;

            return null;
        }

        public static string Describe(GameLanguage language, BuildLanguageSource source)
        {
            string name = language == GameLanguage.AutoDetect ? "AutoDetect" : language.ToString();
            switch (source)
            {
                case BuildLanguageSource.ProfileLock: return $"{name} (🔒 travado no perfil)";
                case BuildLanguageSource.Override: return $"{name} (escolhido só para este build)";
                default: return $"{name} (padrão do projeto)";
            }
        }
    }
}
