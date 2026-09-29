using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Builds the whole "Publishers &amp; Store Profiles" card: card-based summaries instead of Unity's default
    /// "Element 0/1/2..." reorderable-array rendering, the Steam define/output-folder/depot-completeness
    /// status, and the raw array as a collapsed escape hatch. Shared by <see cref="ProjectBuildConfigCustomEditor"/>
    /// (the Inspector) and <see cref="BuildPipelineConfigView"/> (the standalone Build Pipeline window's
    /// "Project Config" tab) — those two used to carry independent copies of this UI, so a fix made to one
    /// (like the card redesign) never reached the other. Don't let that happen again: touch this file, not a
    /// copy pasted into either caller.
    ///
    /// Every mutation here only calls <see cref="EditorUtility.SetDirty"/> and rebuilds locally — it never
    /// calls <see cref="AssetDatabase.SaveAssetIfDirty"/>. Saving eagerly on every click used to force an
    /// asset reimport mid-edit, which could invalidate the Inspector's <see cref="SerializedObject"/> and
    /// silently stop the panel from reflecting further changes until the user deselected/reselected the
    /// asset. Disk writes belong to the explicit Save/Discard bar (<see cref="BuildPipelineUIStyle.CreateSaveStatusBar"/>),
    /// not to every list edit.
    /// </summary>
    internal static class PublisherProfilesSectionBuilder
    {
        private const string DefaultSteamOutputSubfolder = "Builds/Publishers/Steam/";
        private static readonly HashSet<int> ExpandedPublisherIndices = new HashSet<int>();

        public static VisualElement BuildSection(ProjectBuildConfig config, SerializedObject so, Action onExternalChange = null)
        {
            var card = BuildPipelineUIStyle.CreateCard("Publishers & Store Profiles", "Custom publishers, splash overlays, and store build variants");
            var body = new VisualElement();
            card.Add(body);

            void Refresh()
            {
                body.Clear();
                config.publishers ??= new List<PublisherProfile>();
                var steamProfiles = config.publishers.Where(p => p.IsSteamProfile).ToList();

                if (steamProfiles.Count == 0)
                {
                    var noSteam = BuildPipelineUIStyle.CreateCallout(
                        "No Steam publisher profile is configured — there's nowhere for Steam-only settings " +
                        "(build output folder, the NativeSocial scripting define) to live, and a Steam quick-build " +
                        "won't find a profile to use.", "warning");
                    var addSteamBtn = new Button(() =>
                    {
                        config.publishers.Add(new PublisherProfile(Publisher.Steam, "Steam", PlatformType.Windows64, false, true, false));
                        EditorUtility.SetDirty(config);
                        so.Update();
                        Refresh();
                        onExternalChange?.Invoke();
                    })
                    { text = "➕ Add Steam Profile" };
                    addSteamBtn.AddToClassList("bp-btn");
                    addSteamBtn.AddToClassList("bp-btn--primary");
                    addSteamBtn.style.marginTop = 6;
                    noSteam.Add(addSteamBtn);
                    body.Add(noSteam);
                }
                else
                {
                    body.Add(BuildSteamDefineStatus(config, so, steamProfiles, Refresh, onExternalChange));
                    body.Add(BuildSteamDepotStatus(config));

                    foreach (var steamProfile in steamProfiles)
                        body.Add(BuildSteamFolderCard(config, steamProfile));
                }

                var pubProp = so.FindProperty("publishers");

                var listHeader = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 10, marginBottom = 4 } };
                listHeader.Add(new Label($"All Publishers ({config.publishers.Count})") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
                var addPubBtn = new Button(() =>
                {
                    config.publishers.Add(new PublisherProfile(Publisher.Default, "New Publisher", PlatformType.Windows64));
                    EditorUtility.SetDirty(config);
                    so.Update();
                    Refresh();
                    onExternalChange?.Invoke();
                })
                { text = "➕ Add Publisher" };
                addPubBtn.AddToClassList("bp-btn");
                listHeader.Add(addPubBtn);
                body.Add(listHeader);

                for (int i = 0; i < config.publishers.Count; i++)
                    body.Add(BuildPublisherCard(config, so, pubProp, i, () => { Refresh(); onExternalChange?.Invoke(); }));

                var rawFoldout = new Foldout { text = "Customize Publishers List (Raw Reorderable Array)", value = false };
                rawFoldout.style.opacity = 0.85f;
                rawFoldout.style.marginTop = 6;
                rawFoldout.Add(new PropertyField(pubProp, "Publishers Array"));
                body.Add(rawFoldout);

                var restorePubsBtn = new Button(() =>
                {
                    if (EditorUtility.DisplayDialog("Restore Default Publishers", "Reset publisher list to default presets?", "RESTORE", "CANCEL"))
                    {
                        config.publishers = PublisherProfile.GetDefaultProfiles();
                        EditorUtility.SetDirty(config);
                        so.Update();
                        Refresh();
                        onExternalChange?.Invoke();
                    }
                })
                { text = "↺ Restore Default Publishers List" };
                restorePubsBtn.AddToClassList("bp-btn");
                restorePubsBtn.style.marginTop = 6;
                body.Add(restorePubsBtn);
            }

            Refresh();
            return card;
        }

        private static VisualElement BuildSteamDefineStatus(ProjectBuildConfig config, SerializedObject so, List<PublisherProfile> steamProfiles, Action refresh, Action onExternalChange)
        {
            var missingDefine = steamProfiles
                .Where(p => !(p.scriptingDefines ?? new List<string>()).Contains(PublisherProfile.NativeSocialSteamDefine))
                .ToList();

            var defineStatus = missingDefine.Count == 0
                ? BuildPipelineUIStyle.CreateCallout(
                    $"✔ '{PublisherProfile.NativeSocialSteamDefine}' is set on every Steam profile ({string.Join(", ", steamProfiles.Select(p => p.displayName))}). " +
                    "Unity shares ONE scripting-define set across Windows/Mac/Linux (all three are the same \"Standalone\" build target group) — this single check already covers all three platforms, there is no separate per-OS define to verify.",
                    "info")
                : BuildPipelineUIStyle.CreateCallout(
                    $"⚠ {missingDefine.Count} Steam profile(s) ({string.Join(", ", missingDefine.Select(p => p.displayName))}) don't have '{PublisherProfile.NativeSocialSteamDefine}' in Scripting Defines. " +
                    "Windows/Mac/Linux share ONE scripting-define set in Unity (the \"Standalone\" build target group), so fixing this once here covers all three — there is no separate Win/Mac/Linux define to add. " +
                    "The build pipeline auto-adds it at build time when Steamworks.NET is detected, but adding it here makes it explicit and keeps it working even without that safety net.",
                    "warning");

            if (missingDefine.Count > 0)
            {
                var fixBtn = new Button(() =>
                {
                    foreach (var p in missingDefine)
                    {
                        p.scriptingDefines ??= new List<string>();
                        p.scriptingDefines.Add(PublisherProfile.NativeSocialSteamDefine);
                    }
                    EditorUtility.SetDirty(config);
                    so.Update();
                    refresh();
                    onExternalChange?.Invoke();
                })
                { text = $"🛠 Add define to {missingDefine.Count} Steam profile(s)" };
                fixBtn.AddToClassList("bp-btn");
                fixBtn.AddToClassList("bp-btn--primary");
                fixBtn.style.marginTop = 6;
                defineStatus.Add(fixBtn);
            }
            return defineStatus;
        }

        /// <summary>
        /// The other half of "does Steam have all 3 platforms configured": depot IDs are assigned by
        /// Steamworks itself (Partner Site), so there's no way to auto-generate or "guarantee" them the way
        /// the scripting define can be auto-added — but a missing one is a silent, easy-to-miss gap (that
        /// platform's build/upload will just be skipped), so this makes it an explicit, named warning
        /// instead, with a direct shortcut to where it's actually fixed.
        /// </summary>
        private static VisualElement BuildSteamDepotStatus(ProjectBuildConfig config)
        {
            var cfg = config.steamUpload;
            var missing = new List<string>();
            if (!cfg.HasWindowsDepot) missing.Add("Windows");
            if (!cfg.HasMacDepot) missing.Add("Mac");
            if (!cfg.HasLinuxDepot) missing.Add("Linux");

            var status = missing.Count == 0
                ? BuildPipelineUIStyle.CreateCallout("✔ Steam Upload has a Depot ID for all 3 platforms: Windows, Mac, Linux.", "info")
                : BuildPipelineUIStyle.CreateCallout(
                    $"⚠ Steam Upload is missing a Depot ID for: {string.Join(", ", missing)}. That platform's Steam build/upload will be silently skipped until you create the depot in the Steamworks Partner Site and enter its ID.",
                    "warning");

            var openBtn = new Button(SteamUploadWindow.Open) { text = "🎮 Open Steam Upload..." };
            openBtn.AddToClassList("bp-btn");
            openBtn.style.marginTop = 6;
            status.Add(openBtn);
            return status;
        }

        /// <summary>Prominent, dedicated "where do Steam builds go" control for one Steam profile — the generic
        /// reorderable Publishers array buries <see cref="PublisherProfile.outputSubfolder"/> behind several
        /// clicks and shows no resolved path. Picking an absolute folder here works because
        /// <c>Path.Combine(root, sub, ...)</c> discards everything before the last rooted (absolute) segment,
        /// so an absolute outputSubfolder fully overrides the global build root for just this profile.</summary>
        private static VisualElement BuildSteamFolderCard(ProjectBuildConfig config, PublisherProfile steamProfile)
        {
            var card = new VisualElement();
            card.AddToClassList("bp-card");
            card.style.marginTop = 8;
            card.style.marginBottom = 8;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.backgroundColor = new Color(0.15f, 0.18f, 0.24f, 0.6f);
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 4;

            var title = new Label($"📁 Steam Build Output Folder — \"{steamProfile.displayName}\" profile");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 12;
            title.style.marginBottom = 4;
            card.Add(title);

            var resolvedLabel = new Label();
            resolvedLabel.style.fontSize = 11;
            resolvedLabel.style.color = new Color(0.6f, 0.85f, 0.6f);
            resolvedLabel.style.whiteSpace = WhiteSpace.Normal;
            resolvedLabel.style.marginBottom = 6;
            card.Add(resolvedLabel);

            void RefreshResolvedPreview()
            {
                resolvedLabel.text = $"→ {ResolveSteamOutputPreview(config, steamProfile)}";
            }

            var subRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var subField = new TextField("Subfolder / absolute path") { value = steamProfile.outputSubfolder, style = { flexGrow = 1 } };
            subField.RegisterValueChangedCallback(evt =>
            {
                steamProfile.outputSubfolder = evt.newValue;
                EditorUtility.SetDirty(config);
                RefreshResolvedPreview();
            });
            subRow.Add(subField);
            card.Add(subRow);

            RefreshResolvedPreview();

            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };

            var browseBtn = new Button(() =>
            {
                var start = Directory.Exists(ResolveSteamOutputPreview(config, steamProfile))
                    ? ResolveSteamOutputPreview(config, steamProfile)
                    : config.GetEffectiveBuildOutputRoot(steamProfile.platform);
                var picked = EditorUtility.OpenFolderPanel("Choose Steam Build Output Folder", start, "");
                if (string.IsNullOrEmpty(picked)) return;

                steamProfile.outputSubfolder = picked.EndsWith("/") || picked.EndsWith("\\") ? picked : picked + "/";
                EditorUtility.SetDirty(config);
                subField.SetValueWithoutNotify(steamProfile.outputSubfolder);
                RefreshResolvedPreview();
            })
            { text = "📁 Choose Folder..." };
            browseBtn.AddToClassList("bp-btn");
            browseBtn.style.marginRight = 4;
            btnRow.Add(browseBtn);

            var openBtn = new Button(() =>
            {
                var resolved = ResolveSteamOutputPreview(config, steamProfile);
                if (Directory.Exists(resolved))
                    EditorUtility.RevealInFinder(resolved);
                else
                    EditorUtility.DisplayDialog("Folder Not Found",
                        $"'{resolved}' doesn't exist yet — it's created the first time a Steam build runs.", "OK");
            })
            { text = "📂 Open Folder" };
            openBtn.AddToClassList("bp-btn");
            openBtn.style.marginRight = 4;
            btnRow.Add(openBtn);

            var resetBtn = new Button(() =>
            {
                steamProfile.outputSubfolder = DefaultSteamOutputSubfolder;
                EditorUtility.SetDirty(config);
                subField.SetValueWithoutNotify(steamProfile.outputSubfolder);
                RefreshResolvedPreview();
            })
            { text = "↺ Reset to Default" };
            resetBtn.AddToClassList("bp-btn");
            btnRow.Add(resetBtn);

            card.Add(btnRow);
            return card;
        }

        /// <summary>
        /// One publisher profile as a real summary card instead of Unity's default "Element 0/1/2..."
        /// reorderable-array rendering. Shows name/publisher/platform/output folder/defines at a glance; the
        /// full field set is still there, collapsed behind a toggle, bound directly to the SerializedProperty
        /// so it stays undo-friendly and multi-edit-safe like the raw list was.
        /// </summary>
        private static VisualElement BuildPublisherCard(ProjectBuildConfig config, SerializedObject so, SerializedProperty pubProp, int index, Action onListChanged)
        {
            var profile = config.publishers[index];
            var elementProp = pubProp.GetArrayElementAtIndex(index);

            var card = new VisualElement { style = { marginBottom = 8, paddingTop = 8, paddingBottom = 8, paddingLeft = 10, paddingRight = 10, backgroundColor = new Color(0.15f, 0.18f, 0.24f, 0.5f) } };
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 4;

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var isExpanded = ExpandedPublisherIndices.Contains(index);
            var body = new VisualElement { style = { display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None, marginTop = 8, paddingTop = 8, borderTopWidth = 1, borderTopColor = new Color(1, 1, 1, 0.08f) } };

            var expandBtn = new Button { text = isExpanded ? "▾" : "▸" };
            expandBtn.AddToClassList("bp-btn");
            expandBtn.style.width = 26;
            expandBtn.style.marginRight = 6;
            expandBtn.clicked += () =>
            {
                var nowExpanded = body.style.display == DisplayStyle.None;
                body.style.display = nowExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                expandBtn.text = nowExpanded ? "▾" : "▸";
                if (nowExpanded) ExpandedPublisherIndices.Add(index); else ExpandedPublisherIndices.Remove(index);
            };
            header.Add(expandBtn);

            var titleCol = new VisualElement { style = { flexGrow = 1 } };
            var titleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap } };
            titleRow.Add(new Label(string.IsNullOrEmpty(profile.displayName) ? "(unnamed)" : profile.displayName)
                { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginRight = 8 } });
            titleRow.Add(BuildPipelineUIStyle.CreateBadge(profile.publisher.ToString(), "info"));
            var platBadge = BuildPipelineUIStyle.CreateBadge(profile.platform.ToString(), "ok");
            platBadge.style.marginLeft = 4;
            titleRow.Add(platBadge);
            if (profile.IsSteamProfile)
            {
                var steamBadge = BuildPipelineUIStyle.CreateBadge("🎮 Steam", "info");
                steamBadge.style.marginLeft = 4;
                titleRow.Add(steamBadge);
            }
            if (profile.isDemo)
            {
                var demoBadge = BuildPipelineUIStyle.CreateBadge("Demo", "warn");
                demoBadge.style.marginLeft = 4;
                titleRow.Add(demoBadge);
            }
            var definesCount = profile.scriptingDefines?.Count ?? 0;
            if (definesCount > 0)
            {
                var defBadge = BuildPipelineUIStyle.CreateBadge($"{definesCount} define(s)", "ok");
                defBadge.style.marginLeft = 4;
                defBadge.tooltip = string.Join(", ", profile.scriptingDefines);
                titleRow.Add(defBadge);
            }
            titleCol.Add(titleRow);

            var outPreview = ResolveQuickBuildOutputPreview(config, profile, profile.platform);
            titleCol.Add(new Label($"📁 {outPreview}") { style = { fontSize = 10, color = new Color(0.6f, 0.65f, 0.7f), whiteSpace = WhiteSpace.Normal, marginTop = 2 } });
            header.Add(titleCol);

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };

            var upBtn = new Button(() =>
            {
                if (index == 0) return;
                pubProp.MoveArrayElement(index, index - 1);
                so.ApplyModifiedProperties();
                onListChanged();
            })
            { text = "▲" };
            upBtn.AddToClassList("bp-btn");
            upBtn.SetEnabled(index > 0);
            upBtn.style.width = 24;
            actions.Add(upBtn);

            var downBtn = new Button(() =>
            {
                if (index >= config.publishers.Count - 1) return;
                pubProp.MoveArrayElement(index, index + 1);
                so.ApplyModifiedProperties();
                onListChanged();
            })
            { text = "▼" };
            downBtn.AddToClassList("bp-btn");
            downBtn.SetEnabled(index < config.publishers.Count - 1);
            downBtn.style.width = 24;
            actions.Add(downBtn);

            var dupBtn = new Button(() =>
            {
                pubProp.InsertArrayElementAtIndex(index);
                so.ApplyModifiedProperties();
                so.Update();
                // InsertArrayElementAtIndex duplicates the element at 'index' into 'index+1' — rename the
                // copy so two profiles don't silently share a display name.
                config.publishers[index + 1].displayName += " (Copy)";
                EditorUtility.SetDirty(config);
                onListChanged();
            })
            { text = "⧉" };
            dupBtn.tooltip = "Duplicate";
            dupBtn.AddToClassList("bp-btn");
            dupBtn.style.width = 24;
            dupBtn.style.marginLeft = 4;
            actions.Add(dupBtn);

            var removeBtn = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Remove Publisher", $"Remove '{profile.displayName}'?", "Remove", "Cancel"))
                    return;
                pubProp.DeleteArrayElementAtIndex(index);
                so.ApplyModifiedProperties();
                onListChanged();
            })
            { text = "✕" };
            removeBtn.tooltip = "Remove";
            removeBtn.AddToClassList("bp-btn");
            removeBtn.AddToClassList("bp-btn--danger");
            removeBtn.style.width = 24;
            removeBtn.style.marginLeft = 4;
            actions.Add(removeBtn);

            header.Add(actions);
            card.Add(header);

            foreach (var fieldName in new[]
            {
                "id", "publisher", "displayName", "platform", "bundleIdentifier", "requiresSplash", "isFullGame",
                "isDemo", "scriptingBackend", "macArchitecture", "macAppStoreValidation", "scriptingDefines",
                "outputSubfolder", "zipAfterBuild", "zipDestinationTemplate"
            })
            {
                var fieldProp = elementProp.FindPropertyRelative(fieldName);
                if (fieldProp != null) body.Add(new PropertyField(fieldProp));
            }
            card.Add(body);

            return card;
        }

        /// <summary>Best-effort absolute preview of where a Steam build for this profile would land — same
        /// combine logic as <c>BuildContext.ResolveOutputPaths</c>'s Windows/Linux branch, minus the
        /// per-build filename folder (version/date/language), since this is shown before any build ran.</summary>
        private static string ResolveSteamOutputPreview(ProjectBuildConfig config, PublisherProfile steamProfile)
        {
            var root = config.GetEffectiveBuildOutputRoot(steamProfile.platform);
            var sub = string.IsNullOrEmpty(steamProfile.outputSubfolder) ? DefaultSteamOutputSubfolder : steamProfile.outputSubfolder;
            try { return Path.GetFullPath(Path.Combine(root, sub)); }
            catch { return Path.Combine(root, sub); }
        }

        /// <summary>Best-effort absolute preview of a profile's resolved output folder for a given platform —
        /// used by both the publisher cards and Quick Build's live status readout.</summary>
        public static string ResolveQuickBuildOutputPreview(ProjectBuildConfig config, PublisherProfile prof, PlatformType platform)
        {
            var root = config.GetEffectiveBuildOutputRoot(platform);
            var sub = (prof.outputSubfolder ?? "Builds/").Replace("{Publisher}", prof.publisher.ToString());
            try { return Path.GetFullPath(Path.Combine(root, sub)); }
            catch { return Path.Combine(root, sub); }
        }
    }
}
