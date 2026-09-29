using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    [Serializable]
    public class SteamUploadHistoryEntry
    {
        public string timestampUtc;
        public string appId;
        public string branch;
        public bool windowsIncluded;
        public bool macIncluded;
        public bool linuxIncluded;
        public bool success;
        public int exitCode;
        public string summary;

        public DateTime Timestamp => DateTime.TryParse(timestampUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt.ToLocalTime() : DateTime.MinValue;

        public string PlatformsLabel()
        {
            var parts = new List<string>();
            if (windowsIncluded) parts.Add("Win");
            if (macIncluded) parts.Add("Mac");
            if (linuxIncluded) parts.Add("Linux");
            return parts.Count > 0 ? string.Join("+", parts) : "(none)";
        }
    }

    [Serializable]
    internal class SteamUploadHistoryFile
    {
        public List<SteamUploadHistoryEntry> entries = new List<SteamUploadHistoryEntry>();
    }

    /// <summary>
    /// Persists Steam upload attempts to <c>ProjectSettings/SteamUploadHistory.json</c> — inside the project,
    /// committed alongside every other ProjectSettings file, so "when did we last push to Steam, and did it
    /// work" survives machine changes and doesn't live only in Steamworks' own web dashboard or a throwaway
    /// console window.
    /// </summary>
    internal static class SteamUploadHistory
    {
        private const string FilePath = "ProjectSettings/SteamUploadHistory.json";
        private const int MaxEntries = 100;

        public static List<SteamUploadHistoryEntry> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<SteamUploadHistoryEntry>();
                var json = File.ReadAllText(FilePath);
                var file = JsonUtility.FromJson<SteamUploadHistoryFile>(json);
                return file?.entries ?? new List<SteamUploadHistoryEntry>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Could not read Steam upload history: {ex.Message}");
                return new List<SteamUploadHistoryEntry>();
            }
        }

        public static void Append(SteamUploadHistoryEntry entry)
        {
            var entries = Load();
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

            try
            {
                var file = new SteamUploadHistoryFile { entries = entries };
                File.WriteAllText(FilePath, JsonUtility.ToJson(file, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipeline] Could not save Steam upload history: {ex.Message}");
            }
        }
    }
}
