using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Runtime source of truth for a game build: store/publisher, language, version, bundle ids and assets.
/// <para>
/// <b>Extending it for one game:</b> create a subclass in the game project
/// (<c>public class MyGameConfig : GameConfig</c>) and add whatever serialized fields that game needs
/// (editions, analytics switches, store ids...). The pipeline stores and builds the subclass through this
/// base type, the GameConfig inspector renders the extra fields automatically, and the package never
/// needs to know about them. Use <see cref="buildVariants"/> + <see cref="ApplyBuildVariant"/> when the
/// extra field also changes the output (e.g. Collector's Edition vs Standard Edition), so the matrix,
/// quick build and CLI can compile every edition for you.
/// </para>
/// </summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "Game Configuration", order = 1)]
public class GameConfig : ScriptableObject
{
    [Header("Settings")]
    public SpriteAtlas HudSpriteAtlas;
    public SpriteAtlas LevelHudSpriteAtlas;
    public SpriteAtlas DefaultSpriteAtlas;
    public Texture2D cursorTexture;
    public string BuildFolderName = "";
    public Publisher Publisher = Publisher.Default;
    public GameLanguage GameLanguage = GameLanguage.AutoDetect;

    public bool CheatMode;
    public bool FullGame = true;
    public bool Demo = false;
    public bool UseAchievements = true;

    [Header("Extra Settings & Developer Options")]
    [Tooltip("Forces the native OS cursor instead of rendering the custom game cursor texture.")]
    public bool NoCustomCursor = false;

    [Tooltip("Logs level completion metrics (score, moves, time) to disk and sends online telemetry to analytics servers.")]
    public bool LogLevelsInfo = true;

    [Tooltip("Shows the in-game board/puzzle level editor button in Main Menu for designer testing and level creation.")]
    public bool LevelEditor = false;

    [Tooltip("Loads translations dynamically from StreamingAssets/{languageCode}.txt into I2 Localization at runtime.")]
    public bool ExternalTranslation = false;

    [Tooltip("Bypasses the multi-profile player list and loads the in-Editor player directly to speed up testing.")]
    public bool UseOnlyEditorPlayer = true;

    [Header("Version")]
    public GameVersion GameVersion = new GameVersion();
    public GameBuildDate VersionDate = new GameBuildDate();

    [Header("Mobile Store Build Numbers")]
    [Tooltip("Android bundleVersionCode for Google Play (must be strictly incremented for each APK/AAB release). " +
             "This asset is the source of truth — NOT ProjectBuildConfig.asset, which has no version fields. " +
             "Editing this also updates PlayerSettings.Android.bundleVersionCode live, but the change only reaches disk (and git) when you Save Project (Ctrl+S).")]
    public int AndroidBundleVersionCode = 1;
    [Tooltip("iOS / macOS build number (CFBundleVersion) for Apple App Store & TestFlight. " +
             "Same rule: this asset is the source of truth, and you must Save Project (Ctrl+S) for the change to be committable.")]
    public string iOSBuildNumber = "1";

    [Header("Icons")]
    public Texture2D IconFree;
    public Texture2D IconFull;

    [Header("Game Name")]
    public string DefaultBundleIdentifier = "";
    public string iOSGameName = "";
    public string GameNameIOSAndroid = "";
    public string GameName = "";
    public string GameNameJapanese = "";
    public string GameNameWSA = "";

    [Header("Google Play Store")]
    public string AndroidFull = "";
    public string AndroidFree = "";

    [Header("iOS App Store")]
    public string IOSFull = "";
    public string IOSFree = "";
    public string iOSAppIDFree = "";
    public string iOSAppIDFull = "";

    [Header("Amazon App Store")]
    public string AmazonFull = "";
    public string AmazonFree = "";

    [Header("Samsung Galaxy Store")]
    public string SamsungFull = "";
    public string SamsungFree = "";

    public bool PublisherIsGoogleAndroid => Publisher is Publisher.GoogleAndroidFree or Publisher.GoogleAndroidFull;
    public bool PublisherIsiOS => Publisher is Publisher.iOSFree or Publisher.iOSFull;
    public bool PublisherIsAmazonAndroid => Publisher is Publisher.AmazonAndroidFree or Publisher.AmazonAndroidFull;
    public bool PublisherIsSamsungAndroid => Publisher is Publisher.SamsungFree or Publisher.SamsungFull;
    public bool PublisherIsStandalone => Publisher.IsStandaloneDesktop();

    public bool CanRate =>
        Publisher is Publisher.iOSFree or Publisher.iOSFull
                   or Publisher.MacAppStore or Publisher.MacAppStoreFull
                   or Publisher.GoogleAndroidFree or Publisher.GoogleAndroidFull
                   or Publisher.AmazonAndroidFree or Publisher.AmazonAndroidFull
                   or Publisher.WindowsStore or Publisher.WindowsStoreFull
                   or Publisher.SamsungFull or Publisher.SamsungFree;

    // ---------------------------------------------------------------------------------------------
    // Build variants (game-specific editions)
    // ---------------------------------------------------------------------------------------------

    [Header("Build Variants (game-specific editions)")]
    [Tooltip("Editions compiled by the pipeline for this game (e.g. Collector's Edition / Standard Edition). " +
             "Leave empty when the game has a single edition. Each id is passed to ApplyBuildVariant() before the build " +
             "and the output suffix is appended to the build folder name.")]
    public List<BuildVariant> buildVariants = new List<BuildVariant>();

    /// <summary>True when at least one valid variant is declared.</summary>
    public bool HasBuildVariants
    {
        get
        {
            if (buildVariants == null) return false;
            foreach (var variant in buildVariants)
                if (variant != null && variant.IsValid) return true;
            return false;
        }
    }

    /// <summary>Finds a declared variant by id (case-insensitive) or by display name.</summary>
    public BuildVariant FindBuildVariant(string variantId)
    {
        if (buildVariants == null || string.IsNullOrWhiteSpace(variantId)) return null;

        var wanted = variantId.Trim();
        foreach (var variant in buildVariants)
        {
            if (variant == null || !variant.IsValid) continue;
            if (string.Equals(variant.EffectiveId, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(variant.EffectiveDisplayName, wanted, StringComparison.OrdinalIgnoreCase))
                return variant;
        }

        return null;
    }

    /// <summary>Variants marked as <see cref="BuildVariant.enabledByDefault"/> (falls back to all valid ones).</summary>
    public List<BuildVariant> GetDefaultBuildVariants()
    {
        var result = new List<BuildVariant>();
        if (buildVariants == null) return result;

        foreach (var variant in buildVariants)
            if (variant != null && variant.IsValid && variant.enabledByDefault)
                result.Add(variant);

        if (result.Count == 0)
            foreach (var variant in buildVariants)
                if (variant != null && variant.IsValid)
                    result.Add(variant);

        return result;
    }

    /// <summary>
    /// Called by the pipeline right before a build whose <c>BuildContext.VariantId</c> matches one of
    /// <see cref="buildVariants"/>. Override it in the game's <see cref="GameConfig"/> subclass to apply the
    /// edition to your own fields (e.g. <c>CollectorEdition = variantId == "ce"</c>).
    /// </summary>
    public virtual void ApplyBuildVariant(string variantId)
    {
    }

    /// <summary>
    /// Optional product name for a variant. When it returns a non-empty string the pipeline uses it for
    /// <c>PlayerSettings.productName</c> instead of <see cref="GameName"/> (e.g. "My Game - Collector's Edition").
    /// </summary>
    public virtual string GetBuildVariantGameName(string variantId) => "";
}
