using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using RimSynapse.WorldNews.Integration;
using RimSynapse.WorldNews.Models;
using RimSynapse.WorldNews.Newspaper;
using RimSynapse.WorldNews.Relations;

namespace RimSynapse.WorldNews
{
    public class SynapseWorldNewsWorldComponent : WorldComponent
    {
        public List<string> unpublishedEvents = new List<string>();

        /// <summary>
        /// The durable, structured record of world-map facts (WorldNews#13). Kept alongside the string
        /// queue the generator consumes: the feed is the fact of record (deduplicated, bounded,
        /// save-safe), while accepted events are also flattened into <see cref="unpublishedEvents"/> so
        /// the existing generator surfaces them with no change to its input contract.
        /// </summary>
        public WorldMapChangeFeed worldFeed = new WorldMapChangeFeed();

        // Baselines the sampler diffs against. Deliberately NOT scribed: after a load the first sample
        // re-establishes them and emits nothing, so a change that straddled a save/load is not
        // re-reported (and the feed's dedup ledger, which IS scribed, would suppress it anyway).
        private List<ProvinceSnapshot> lastProvinceSnapshot;
        private HashSet<string> lastSettlementKeys;

        /// <summary>How often the world map is sampled for changes. Four times an in-game day — often
        /// enough to catch a change the day it happens, rare enough to be free. The underlying R&amp;T
        /// ownership recompute is self-gated, so a sample with nothing changed costs almost nothing.</summary>
        private const int WorldSampleInterval = 15000;
        private int nextWorldSampleTick;

        /// <summary>
        /// Short-term standing changes from settlement affairs, kept bounded and decayed back to zero so
        /// they never rewrite a faction pair's long-term baseline. Scribed, so a temporary nudge survives
        /// a save and still unwinds on schedule.
        /// </summary>
        public ShortTermRelationLedger relationLedger = new ShortTermRelationLedger();

        /// <summary>One in-game day. Settlement affairs roll once per day, and the relation ledger decays
        /// one step per day.</summary>
        private const int DayTicks = 60000;
        private int nextAffairsTick;

        /// <summary>Per-settlement daily chance of an affair, and a per-day cap so a huge world cannot
        /// flood the feed or churn goodwill. The cap is logged when it bites (no silent truncation).</summary>
        private const float AffairChancePerSettlement = 0.10f;
        private const int MaxAffairsPerDay = 8;

        /// <summary>
        /// Label prefix on the letters this mod publishes. Recognised on the way back in so a
        /// newspaper announcement is never itself recorded as news — see RecordEventFromLetter.
        /// </summary>
        internal const string NewspaperLetterPrefix = "Newspaper Published: ";

        /// <summary>
        /// Hard ceiling on the queue. Every letter the game raises lands here, and a colony under
        /// pressure raises a great many; with no cap the list grows for as long as generation keeps
        /// failing, and every entry carries the full letter text.
        /// </summary>
        private const int MaxQueuedEvents = 40;

        /// <summary>
        /// Minimum ticks between issues (2500 = one in-game hour). The trigger was a queue-length
        /// threshold with no notion of time, so a burst of letters fired issue after issue as fast
        /// as the letters arrived.
        /// </summary>
        private const int MinTicksBetweenIssues = 2500;

        private int lastIssueTick = -99999;

        /// <summary>
        /// True while a generation request is in flight. Deliberately not saved: an issue still
        /// generating when the game was saved is never coming back, and persisting the flag would
        /// leave the mod permanently unable to publish.
        /// </summary>
        private bool generationInFlight;

        public SynapseWorldNewsWorldComponent(World world) : base(world)
        {
        }

        /// <summary>Not saved: forces a publish-timer settle on the first tick after every load / new game.</summary>
        private bool settled;

        /// <summary>
        /// Settle the publish timer to "now" on the first tick of a session, so an issue can never fire
        /// the instant the map loads — a paper on colony load (often empty, from mocked/thin responses in
        /// tests) is never wanted. The first real issue then waits at least <see cref="MinTicksBetweenIssues"/>.
        /// </summary>
        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (!settled)
            {
                settled = true;
                int nowTick = Find.TickManager?.TicksGame ?? 0;
                lastIssueTick = nowTick;
                generationInFlight = false;
                // First sample one interval out, so a freshly-loaded world isn't diffed on tick one.
                nextWorldSampleTick = nowTick + WorldSampleInterval;
                nextAffairsTick = nowTick + DayTicks;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now >= nextWorldSampleTick)
            {
                nextWorldSampleTick = now + WorldSampleInterval;
                SampleWorldMap();
            }
            if (now >= nextAffairsTick)
            {
                nextAffairsTick = now + DayTicks;
                RunSettlementAffairsDay();
            }
        }

        // ---- Settlement affairs (daily NPC settlement events + short-term standing) ----------------

        /// <summary>
        /// The once-a-day beat: decay yesterday's short-term nudges one step, then roll a fresh batch of
        /// settlement affairs. Decay runs first and unconditionally, so an outstanding nudge keeps
        /// unwinding even if the feature is toggled off mid-game.
        /// </summary>
        internal void RunSettlementAffairsDay()
        {
            relationLedger.TickDecay(ApplyGoodwillActual);

            var settings = RimSynapseWorldNewsMod.Settings;
            if (settings == null || !settings.enableSettlementAffairs) return;
            if (Find.WorldObjects == null || Find.FactionManager == null) return;

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            if (settlements == null || settlements.Count == 0) return;

            int made = 0, skippedByCap = 0;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction == null || s.Faction.IsPlayer || s.Faction.Hidden) continue;
                if (Rand.Value >= AffairChancePerSettlement) continue;

                if (made >= MaxAffairsPerDay) { skippedByCap++; continue; }
                if (GenerateAffairFor(s)) made++;
            }

            if (skippedByCap > 0)
            {
                RimSynapse.SynapseLogger.Message(
                    $"[RimSynapse-WorldNews] Settlement affairs: capped at {MaxAffairsPerDay}/day; " +
                    $"{skippedByCap} further settlement(s) rolled an event that was not generated today.");
            }
        }

        private bool GenerateAffairFor(Settlement settlement)
        {
            Faction owner = settlement.Faction;
            Faction rival = PickRivalFaction(owner);

            var ctx = new AffairContext
            {
                settlementFactionId = owner.GetUniqueLoadID(),
                settlementFactionName = owner.Name,
                rivalFactionId = rival?.GetUniqueLoadID(),
                rivalFactionName = rival?.Name,
                regionName = ResolveRegionName(settlement.Tile),
                nowTicks = Find.TickManager != null ? Find.TickManager.TicksGame : 0,
            };

            AffairResult result = SettlementAffairGenerator.Generate(ctx);
            bool recorded = RecordWorldEvent(result.evt);

            // Apply the short-term standing change (bounded + decaying) only for conflict outcomes.
            if (result.relationDelta != 0 && !string.IsNullOrEmpty(result.bId))
            {
                relationLedger.ApplyNudge(result.aId, result.bId, result.relationDelta, ApplyGoodwillActual);
            }
            return recorded;
        }

        /// <summary>Pick a plausible other NPC faction for a conflict/interaction outcome — a random
        /// non-player, non-hidden faction other than the settlement's own. Null when there is none.</summary>
        private static Faction PickRivalFaction(Faction owner)
        {
            var all = Find.FactionManager.AllFactionsListForReading;
            Faction pick = null;
            int seen = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                if (f == null || f == owner || f.IsPlayer || f.Hidden || f.temporary) continue;
                // Reservoir sample so no full list allocation is needed.
                seen++;
                if (Rand.Range(0, seen) == 0) pick = f;
            }
            return pick;
        }

        private static string ResolveRegionName(int tile)
        {
            return RegionsAndTerritoriesBridge.TryDescribeRegionAtTile(tile, out ProvinceSnapshot snap)
                ? snap?.regionName
                : null;
        }

        /// <summary>
        /// The injected goodwill mechanism for the ledger: apply a change between two factions and return
        /// the amount it actually moved (read before/after, since goodwill saturates at ±100). Messages
        /// and hostility letters are suppressed — these are quiet, temporary frontier ripples, not events
        /// the player is notified of. NPC-only: the player's standing is never touched here.
        /// </summary>
        private int ApplyGoodwillActual(string aId, string bId, int requestedChange)
        {
            if (requestedChange == 0) return 0;
            Faction a = FactionById(aId), b = FactionById(bId);
            if (a == null || b == null || a == b || a.IsPlayer || b.IsPlayer) return 0;

            // Vanilla's Faction.TryAffectGoodwillWith is player-centric and no-ops for NPC↔NPC pairs
            // (the modern goodwill is managed per-player by GoodwillSituationManager). So move the
            // FactionRelation's baseGoodwill directly — a and b each hold their own relation object, so
            // both must be kept in sync — and let CheckKindThresholds re-evaluate hostile/neutral/ally.
            // Letters are suppressed: these are quiet frontier ripples, reversed a few days later by
            // decay. Wrapped defensively because it touches vanilla relation internals.
            try
            {
                FactionRelation relA = a.RelationWith(b, true);
                FactionRelation relB = b.RelationWith(a, true);
                if (relA == null || relB == null) return 0;

                int before = relA.baseGoodwill;
                int after = before + requestedChange;
                if (after > 100) after = 100;
                if (after < -100) after = -100;
                int applied = after - before;
                if (applied == 0) return 0;

                relA.baseGoodwill = after;
                relB.baseGoodwill = after;
                relA.CheckKindThresholds(a, false, null, GlobalTargetInfo.Invalid, out bool _);
                relB.CheckKindThresholds(b, false, null, GlobalTargetInfo.Invalid, out bool _);
                return applied;
            }
            catch (System.Exception ex)
            {
                RimSynapse.SynapseLogger.Message($"[RimSynapse-WorldNews] Relation nudge failed, skipped: {ex.GetType().Name}");
                return 0;
            }
        }

        // Debug hooks (used by DebugActions_WorldNews to exercise the affair path headlessly).
        internal bool DebugApplyAffairResult(AffairResult result)
        {
            bool recorded = RecordWorldEvent(result.evt);
            if (result.relationDelta != 0 && !string.IsNullOrEmpty(result.bId))
                relationLedger.ApplyNudge(result.aId, result.bId, result.relationDelta, ApplyGoodwillActual);
            return recorded;
        }

        internal int DebugDecayRelationsOneDay() => relationLedger.TickDecay(ApplyGoodwillActual);

        // ---- World-map change feed (WorldNews#13) ------------------------------------------------

        /// <summary>
        /// Sample the world map through the R&amp;T seam, diff against the last sample, and record any
        /// changes as structured events. Returns the number of new events accepted (past dedup). Does
        /// nothing — and produces no events — when the feed is disabled or R&amp;T is absent, which is
        /// the "colony-local news only" behaviour by design.
        /// </summary>
        internal int SampleWorldMap()
        {
            var settings = RimSynapseWorldNewsMod.Settings;
            if (settings == null || !settings.enableWorldMapFeed) return 0;

            // Guard names no R&T type; the snapshot workers behind it do, and only run once Active.
            if (!RegionsAndTerritoriesBridge.TrySnapshotWorld(out List<ProvinceSnapshot> provinces))
            {
                return 0; // R&T absent → feed stays empty
            }
            RegionsAndTerritoriesBridge.TrySnapshotSettlements(out List<SettlementSnapshot> settlements);

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            int accepted = 0;

            // First sample of the session establishes the baseline with no emission — there is nothing
            // to diff against, and a load must not re-report the whole map as "new".
            if (lastProvinceSnapshot != null)
            {
                if (settings.detectBorderChanges)
                {
                    foreach (var e in WorldMapChangeDetector.DetectBorderChanges(lastProvinceSnapshot, provinces, now))
                        accepted += RecordWorldEvent(e) ? 1 : 0;
                }
                if (settings.detectBorderTension)
                {
                    foreach (var e in WorldMapChangeDetector.DetectBorderTension(provinces, now, AreFactionsHostile))
                        accepted += RecordWorldEvent(e) ? 1 : 0;
                }
                if (settings.detectIdeologyShift)
                {
                    foreach (var e in WorldMapChangeDetector.DetectIdeologyShifts(lastProvinceSnapshot, provinces, now))
                        accepted += RecordWorldEvent(e) ? 1 : 0;
                }
                if (settings.detectSettlementFounded && settlements != null && lastSettlementKeys != null)
                {
                    foreach (var e in WorldMapChangeDetector.DetectSettlementsFounded(lastSettlementKeys, settlements, now))
                        accepted += RecordWorldEvent(e) ? 1 : 0;
                }
            }

            lastProvinceSnapshot = provinces;
            lastSettlementKeys = KeysOf(settlements);
            return accepted;
        }

        private static HashSet<string> KeysOf(List<SettlementSnapshot> settlements)
        {
            var keys = new HashSet<string>();
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    if (settlements[i] != null && !string.IsNullOrEmpty(settlements[i].key)) keys.Add(settlements[i].key);
                }
            }
            return keys;
        }

        /// <summary>
        /// Record a world-map event into the feed and, if it survives dedup, flatten it into the
        /// generator's event queue as a dated news line. Shared by the sampler and the debug actions,
        /// so both apply dedup identically. Returns true if the event was newly accepted.
        /// </summary>
        internal bool RecordWorldEvent(WorldNewsEvent e)
        {
            if (e == null) return false;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (!worldFeed.TryRecord(e, now)) return false;

            RecordEvent($"{DateStamp()} {e.ToNewsLine()}");
            return true;
        }

        /// <summary>Hostility predicate for the tension detector, resolving faction ids to live factions.
        /// Kept here (not in the detector) so the rule stays pure and testable; production supplies this.</summary>
        private static bool AreFactionsHostile(string aId, string bId)
        {
            Faction a = FactionById(aId);
            Faction b = FactionById(bId);
            return a != null && b != null && a != b && a.HostileTo(b);
        }

        private static Faction FactionById(string uniqueLoadId)
        {
            if (string.IsNullOrEmpty(uniqueLoadId) || Find.FactionManager == null) return null;
            var all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && string.Equals(all[i].GetUniqueLoadID(), uniqueLoadId, System.StringComparison.Ordinal))
                    return all[i];
            }
            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref unpublishedEvents, "unpublishedEvents", LookMode.Value);
            Scribe_Values.Look(ref lastIssueTick, "lastIssueTick", -99999);
            Scribe_Deep.Look(ref worldFeed, "worldFeed");
            Scribe_Deep.Look(ref relationLedger, "relationLedger");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (unpublishedEvents == null) unpublishedEvents = new List<string>();
                if (worldFeed == null) worldFeed = new WorldMapChangeFeed();
                if (relationLedger == null) relationLedger = new ShortTermRelationLedger();
                generationInFlight = false;
            }
        }

        public void RecordEventFromLetter(Letter letter)
        {
            if (letter == null) return;

            string label = letter.Label;
            string text = "";
            if (letter is ChoiceLetter cl)
            {
                text = cl.Text;
            }

            // Avoid recording trivial letters or spam
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(text)) return;

            // Never record our own announcement. Publishing an issue sends a letter, the letter
            // patch records that letter as news, and it counts toward the threshold for the next
            // issue — the mod was feeding itself.
            if (label.StartsWith(NewspaperLetterPrefix)) return;

            RecordEvent($"{DateStamp()} {label}: {text}");
        }

        /// <summary>
        /// The single way an event enters the queue. Both entry points — the letter patch and the
        /// gossip patch — go through here, so the cap and the publish rules cannot be applied in
        /// one place and forgotten in the other. They were.
        /// </summary>
        internal void RecordEvent(string eventString)
        {
            if (string.IsNullOrEmpty(eventString)) return;

            unpublishedEvents.Add(eventString);

            if (unpublishedEvents.Count > MaxQueuedEvents)
            {
                unpublishedEvents.RemoveRange(0, unpublishedEvents.Count - MaxQueuedEvents);
            }

            TryPublish();
        }

        /// <summary>Publish only with enough news, nothing already generating, and enough time elapsed.</summary>
        internal void TryPublish()
        {
            if (unpublishedEvents.Count < 4) return;
            if (generationInFlight) return;

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastIssueTick < MinTicksBetweenIssues) return;

            TriggerNewspaperGeneration();
        }

        internal void TriggerNewspaperGeneration()
        {
            if (generationInFlight) return;
            if (unpublishedEvents.Count == 0) return;

            lastIssueTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            generationInFlight = true;

            // Hand the generator its own copy and clear immediately, so events arriving while the
            // request is in flight belong to the next issue instead of being published twice.
            var batch = new List<string>(unpublishedEvents);
            unpublishedEvents.Clear();

            RimSynapse.WorldNews.Newspaper.SynapseNewspaperGenerator.Generate(batch, OnGenerationFinished);
        }

        private void OnGenerationFinished()
        {
            generationInFlight = false;
        }

        private static string DateStamp()
        {
            var tm = Find.TickManager;
            if (tm == null) return "[unknown date]";
            return $"[{GenLocalDate.Twelfth(tm.TicksGame)}, {GenLocalDate.Year(tm.TicksGame)}]";
        }
    }
}
