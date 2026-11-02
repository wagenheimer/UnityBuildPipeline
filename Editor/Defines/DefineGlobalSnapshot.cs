using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Remembers the PlayerSettings scripting defines per target group the last time the user reviewed them,
    /// so the window can flag when something else (a package's setup/audit script, a manual edit, a merge)
    /// changed the global set that every profile inherits. Stored per machine in Library/BuildPipeline.
    /// </summary>
    public static class DefineGlobalSnapshot
    {
        private const string SnapshotPath = "Library/BuildPipeline/global-defines-snapshot.json";

        public static readonly BuildTargetGroup[] Groups =
        {
            BuildTargetGroup.Standalone, BuildTargetGroup.Android, BuildTargetGroup.iOS, BuildTargetGroup.WebGL
        };

        public sealed class GroupDiff
        {
            public BuildTargetGroup Group;
            public List<string> Added = new List<string>();
            public List<string> Removed = new List<string>();
        }

        public static List<string> Current(BuildTargetGroup group)
        {
            try
            {
                var named = NamedBuildTarget.FromBuildTargetGroup(group);
                return DefineStackResolver.Split(new[] { PlayerSettings.GetScriptingDefineSymbols(named) }).ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        /// <summary>Differences since the last accepted snapshot. The first call records a baseline and returns nothing.</summary>
        public static List<GroupDiff> Diff()
        {
            var saved = Load();
            if (saved == null)
            {
                Accept();
                return new List<GroupDiff>();
            }

            var result = new List<GroupDiff>();
            foreach (var group in Groups)
            {
                var before = saved.groups.FirstOrDefault(g => g.group == group.ToString())?.defines ?? new List<string>();
                var now = Current(group);

                var diff = new GroupDiff
                {
                    Group = group,
                    Added = now.Except(before, StringComparer.Ordinal).ToList(),
                    Removed = before.Except(now, StringComparer.Ordinal).ToList()
                };

                if (diff.Added.Count > 0 || diff.Removed.Count > 0)
                    result.Add(diff);
            }

            return result;
        }

        /// <summary>Marks the current globals as reviewed.</summary>
        public static void Accept()
        {
            var file = new SnapshotFile { savedAtUtc = DateTime.UtcNow.ToString("o") };
            foreach (var group in Groups)
                file.groups.Add(new SnapshotGroup { group = group.ToString(), defines = Current(group) });

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath));
                File.WriteAllText(SnapshotPath, JsonUtility.ToJson(file, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildPipeline] Não foi possível gravar o snapshot dos defines globais: " + ex.Message);
            }
        }

        public static string SavedAt() => Load()?.savedAtUtc ?? "";

        private static SnapshotFile Load()
        {
            try
            {
                return File.Exists(SnapshotPath) ? JsonUtility.FromJson<SnapshotFile>(File.ReadAllText(SnapshotPath)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Serializable]
        private sealed class SnapshotFile
        {
            public string savedAtUtc = "";
            public List<SnapshotGroup> groups = new List<SnapshotGroup>();
        }

        [Serializable]
        private sealed class SnapshotGroup
        {
            public string group = "";
            public List<string> defines = new List<string>();
        }
    }
}
