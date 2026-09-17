using System;
using UnityEngine;
using UnityEngine.U2D;

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

    [Header("Extra Settings")]
    public bool FreeToPlay = false;
    public bool NoCustomCursor = false;
    public bool LogLevelsInfo = true;
    public bool LevelEditor = false;
    public bool ExternalTranslation = false;
    [Tooltip("Bypasses the multi-profile player list and loads the in-Editor player directly.")]
    public bool UseOnlyEditorPlayer = true;
    [Tooltip("Whether the player can switch the game language from in-game UI.")]
    public bool CanChangeLanguage = false;

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

    [Header("Mac App Store")]
    public string MacAppStoreID = "";

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
}
