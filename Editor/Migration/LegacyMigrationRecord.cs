using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// ProjectSettings/BuildPipelineLegacyMigration.json, written by Tools/migrate-legacy-project.ps1 when it migrates
    /// a game off its own GameConfig.cs. Status: "pending-unity" (script ran, Unity not opened yet) → "done-unity"
    /// (ProjectBuildConfig created) → "closed" (the user checked the remaining items).
    /// </summary>
    [Serializable]
    public sealed class LegacyMigrationRecord
    {
        public const string RelativePath = "ProjectSettings/BuildPipelineLegacyMigration.json";

        public int schema;
        public string migratedAt = "";
        public string status = "";
        public string configClass = "";
        public List<string> movedFields = new List<string>();
        public List<string> keptTypes = new List<string>();
        public List<string> retypedFiles = new List<string>();
        public List<string> removedScripts = new List<string>();
        public List<string> configAssets = new List<string>();
        public List<string> missingPublishers = new List<string>();
        public List<string> manualReview = new List<string>();
        public string backup = "";

        public bool IsOpen => status != "closed";

        public static LegacyMigrationRecord Load()
        {
            try
            {
                if (!File.Exists(RelativePath))
                    return null;

                return JsonUtility.FromJson<LegacyMigrationRecord>(File.ReadAllText(RelativePath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildPipeline] Não foi possível ler " + RelativePath + ": " + ex.Message);
                return null;
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(RelativePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildPipeline] Não foi possível gravar " + RelativePath + ": " + ex.Message);
            }
        }

        /// <summary>Called after the in-editor step created the ProjectBuildConfig.</summary>
        public static void MarkUnityStepDone()
        {
            var record = Load();
            if (record == null || record.status != "pending-unity")
                return;

            record.status = "done-unity";
            record.Save();
        }

        public static void Close()
        {
            var record = Load();
            if (record == null)
                return;

            record.status = "closed";
            record.Save();
        }
    }
}
