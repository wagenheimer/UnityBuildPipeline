using System;
using System.Text;
using UnityEngine;

/// <summary>
/// One selectable build variant declared by a game (e.g. "Collector's Edition" vs "Standard Edition").
/// <para>
/// The pipeline never interprets a variant: it only enumerates the list on <see cref="GameConfig"/>,
/// passes <see cref="EffectiveId"/> back to <see cref="GameConfig.ApplyBuildVariant"/> before the build,
/// and appends <see cref="outputSuffix"/> to the output folder name. This keeps game-specific editions
/// out of the package while still letting the matrix, quick build, CLI and history know about them.
/// </para>
/// </summary>
[Serializable]
public class BuildVariant
{
    [Tooltip("Stable id passed to GameConfig.ApplyBuildVariant() and to the CLI (-variant / -matrixVariants). " +
             "Lowercase, no spaces (e.g. 'ce'). Auto-slugged from the display name when left empty.")]
    public string id = "";

    [Tooltip("Label shown in the build window, matrix and build history (e.g. \"Collector's Edition\").")]
    public string displayName = "";

    [Tooltip("Suffix appended to the build output folder (e.g. '_CE'). Leave empty for the default variant.")]
    public string outputSuffix = "";

    [Tooltip("Pre-selected in the Matrix Batch and Quick Build views.")]
    public bool enabledByDefault = true;

    public string EffectiveId => !string.IsNullOrWhiteSpace(id) ? id.Trim() : Slug(displayName);

    public string EffectiveDisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : EffectiveId;

    public bool IsValid => !string.IsNullOrWhiteSpace(EffectiveId);

    public static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if ((c == ' ' || c == '_' || c == '-') && builder.Length > 0 && builder[builder.Length - 1] != '_')
            {
                builder.Append('_');
            }
        }

        return builder.ToString().Trim('_');
    }

    public override string ToString() => EffectiveDisplayName;
}
