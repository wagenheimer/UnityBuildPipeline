using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Reusable group of scripting defines (for example "FreeAds" or "ATT"). A profile references the
    /// preset by id in <see cref="PublisherProfile.definePresets"/>; the resolver expands the presets
    /// before the profile's own list, so a set of defines is edited in one place.
    /// </summary>
    [Serializable]
    public sealed class BuildDefinePreset
    {
        [Tooltip("Stable id referenced by the profile (e.g. 'free-ads').")]
        public string id = "";

        [Tooltip("Human label shown in the matrix/inspector.")]
        public string displayName = "";

        [Tooltip("One symbol per line. The '!DEFINE' / '-DEFINE' removal syntax is supported.")]
        public List<string> defines = new List<string>();
    }
}
