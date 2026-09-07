using System;
using UnityEngine;
using UnityEngine.U2D;

[CreateAssetMenu(fileName = "GameConfig", menuName = "Game Configuration", order = 1)]
public class GameConfig : ScriptableObject
{
    [Header("Settings")]
    public SpriteAtlas HudSpriteAtlas;
    public SpriteAtlas LevelHudSpriteAtlas;
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

    [Header("Version")]
    public GameVersion GameVersion = new GameVersion();
    public GameBuildDate VersionDate = new GameBuildDate();

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
}
