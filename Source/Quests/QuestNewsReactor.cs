using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using RimSynapse.WorldNews.Integration;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Quests
{
    /// <summary>
    /// Turns a resolved vanilla quest into a world-map news event about the factions whose ground it
    /// happened on (WorldNews#14). The reaction is the story, not the quest: "player cleared a bandit
    /// camp" is a log line; "the faction whose border the camp sat on is relieved, and the one that
    /// funded it is not" is an article.
    ///
    /// <para>The affected-faction set is derived from <b>territory</b> (the region the quest sat in),
    /// not from the quest giver alone — a quest in unclaimed territory simply produces nothing. The
    /// pure <see cref="BuildQuestEvent"/> core is unit-tested against synthetic snapshots; the
    /// <see cref="OnQuestEnded"/> orchestration resolves the live quest's location defensively and
    /// records through the same feed as the world-map detectors.</para>
    /// </summary>
    public static class QuestNewsReactor
    {
        /// <summary>
        /// Build a quest-outcome event from a resolved quest's region snapshot, or null when there is no
        /// story: an outcome we do not report (only success/failure count), a region with no territorial
        /// owner (unclaimed ground — nobody's frontier to react), or a missing snapshot. Pure and
        /// side-effect free so the "emits" and "stays quiet" rules are testable without a world.
        /// </summary>
        public static WorldNewsEvent BuildQuestEvent(ProvinceSnapshot snap, string questName, QuestEndOutcome outcome, int nowTicks)
        {
            if (snap == null) return null;
            if (string.IsNullOrEmpty(snap.primaryOwnerId)) return null; // unclaimed territory → quiet

            string qualifier;
            if (outcome == QuestEndOutcome.Success) qualifier = "succeeded";
            else if (outcome == QuestEndOutcome.Fail) qualifier = "failed";
            else return null; // Unknown / InvalidPreAcceptance are not news

            // Second actor: the strongest rival contender that is NOT the holder, when the region is
            // contested — the neighbour most likely to read the outcome differently. Absent otherwise.
            string rivalId = null, rivalName = null;
            if (snap.contested && snap.contenderIds != null)
            {
                for (int i = 0; i < snap.contenderIds.Count; i++)
                {
                    string id = snap.contenderIds[i];
                    if (string.IsNullOrEmpty(id) || string.Equals(id, snap.primaryOwnerId, StringComparison.Ordinal)) continue;
                    rivalId = id;
                    rivalName = (snap.contenderNames != null && i < snap.contenderNames.Count) ? snap.contenderNames[i] : null;
                    break;
                }
            }

            return new WorldNewsEvent(
                WorldNewsEventKind.QuestOutcome, nowTicks, snap.provinceId, snap.regionName,
                snap.primaryOwnerId, snap.primaryOwnerName, rivalId, rivalName,
                qualifier: qualifier, subject: string.IsNullOrEmpty(questName) ? null : questName);
        }

        /// <summary>
        /// Live entry point from the Harmony hook. Resolves the quest's location, looks up the region's
        /// owners through the R&amp;T seam, and records a quest-outcome event if the ground is claimed.
        /// Every failure mode is a quiet return, never an exception into vanilla's quest teardown.
        /// </summary>
        public static void OnQuestEnded(Quest quest, QuestEndOutcome outcome)
        {
            if (quest == null) return;
            if (outcome != QuestEndOutcome.Success && outcome != QuestEndOutcome.Fail) return;

            var settings = RimSynapseWorldNewsMod.Settings;
            if (settings == null || !settings.enableWorldMapFeed || !settings.detectQuestOutcomes) return;

            if (!TryResolveQuestTile(quest, out int tile)) return;              // no world location → nothing
            if (!RegionsAndTerritoriesBridge.TryDescribeRegionAtTile(tile, out ProvinceSnapshot snap)) return; // R&T absent / no region

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            WorldNewsEvent e = BuildQuestEvent(snap, quest.name, outcome, now);
            if (e == null) return;                                             // unclaimed / not-news

            Find.World?.GetComponent<SynapseWorldNewsWorldComponent>()?.RecordWorldEvent(e);
        }

        // One-time breadcrumb: many quests legitimately have no world tile (pure in-colony quests), so a
        // per-quest miss is normal and silent. This logs only the first time so the seam's behaviour is
        // observable once without spamming the log.
        private static bool loggedNoTileOnce;

        /// <summary>
        /// Find the world tile a quest points at, using the game's own look-targets rather than any
        /// concrete quest-part type. Returns false when nothing resolves — the caller reads that as
        /// "not sited in a known place", which for #14 means produce nothing. Defensive throughout: this
        /// runs inside a patch on vanilla's teardown, so it must never throw.
        /// </summary>
        public static bool TryResolveQuestTile(Quest quest, out int tile)
        {
            tile = -1;
            if (quest == null) return false;

            try
            {
                List<QuestPart> parts = quest.PartsListForReading;
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        QuestPart part = parts[i];
                        if (part == null) continue;

                        IEnumerable<GlobalTargetInfo> targets = part.QuestLookTargets;
                        if (targets == null) continue;
                        foreach (GlobalTargetInfo t in targets)
                        {
                            int candidate = t.Tile;
                            if (candidate >= 0) { tile = candidate; return true; }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!loggedNoTileOnce)
                {
                    loggedNoTileOnce = true;
                    RimSynapse.SynapseLogger.Message(
                        $"[RimSynapse-WorldNews] Quest location could not be resolved from look-targets " +
                        $"(reported once): {ex.GetType().Name}. Quest-outcome news will stand down for such quests.");
                }
                return false;
            }

            return false;
        }
    }
}
