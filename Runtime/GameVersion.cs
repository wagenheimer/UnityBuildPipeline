using System;

[Serializable]
public class GameVersion
{
    public int Major = 0;
    public int Minor = 1;
    public int Build = 0;

    public GameVersion() { }

    public GameVersion(int major, int minor, int build = 0)
    {
        Major = major;
        Minor = minor;
        Build = build;
    }

    public string GameVersionAsText => Build > 0 ? $"{Major}.{Minor}.{Build}" : $"{Major}.{Minor}";
    public string GameVersionAsTextWithBetaLabel => Major > 0 ? GameVersionAsText : $"{GameVersionAsText} Beta";

    public override string ToString() => GameVersionAsTextWithBetaLabel;
}
