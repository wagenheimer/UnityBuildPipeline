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

    public static bool TryParse(string text, out GameVersion version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(1).Trim();

        var parts = text.Split(new[] { '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        if (!int.TryParse(parts[0], out int major)) return false;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out int parsedMinor) ? parsedMinor : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out int parsedBuild) ? parsedBuild : 0;

        version = new GameVersion(major, minor, build);
        return true;
    }

    public override string ToString() => GameVersionAsTextWithBetaLabel;
}
