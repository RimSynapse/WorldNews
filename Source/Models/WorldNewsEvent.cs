using System.Collections.Generic;
using Verse;

namespace RimSynapse.WorldNews.Models
{
    /// <summary>
    /// The four kinds of world-map change WorldNews watches for (WorldNews#13). The newspaper draws
    /// on these, but generating the article is a separate, agentic step — an event is a <b>fact</b>,
    /// never prose. Keeping the two apart is what makes the feed testable in isolation.
    /// </summary>
    public enum WorldNewsEventKind
    {
        /// <summary>A region changed its primary owner, or a contested region resolved to one.</summary>
        BorderChange,

        /// <summary>A new settlement or outpost appeared inside a known region.</summary>
        SettlementFounded,

        /// <summary>The largest belief share in a region changed. Gated on the R&amp;T distribution
        /// model (Regions-and-Territories#34); until that lands nothing emits this kind.</summary>
        IdeologyShift,

        /// <summary>Two factions with poor relations press competing claims on the same frontier.
        /// The forward-looking kind: it reports a <i>condition</i> ("what might happen next"), not an
        /// event that has already happened.</summary>
        BorderTension,

        /// <summary>A quest resolved in a known region, and the factions who hold that ground react to
        /// its outcome (WorldNews#14). The reaction is the story, not the quest itself — the affected
        /// factions are derived from whose territory it happened in, not from the quest giver alone.</summary>
        QuestOutcome,

        /// <summary>A day-in-the-life event at a non-player settlement — a raid, a caravan, a festival,
        /// an outbreak — adjudicated to a random outcome for flavour. Conflict outcomes also nudge the
        /// two factions' short-term standing (bounded and temporary); local outcomes are pure colour.</summary>
        SettlementAffair,
    }

    /// <summary>
    /// One captured world-map fact, carrying enough snapshotted state to write an article later
    /// without re-deriving anything from the live world (WorldNews#13). Faction <b>ids</b> are held
    /// for identity and dedup; faction <b>names</b> are held for prose, because a faction can be
    /// defeated between capture and publication and its live object would then be gone.
    /// </summary>
    public class WorldNewsEvent : IExposable
    {
        public WorldNewsEventKind kind;

        /// <summary>The in-game tick the change was observed. Drives the date stamp and the dedup cooldown.</summary>
        public int ticksGame;

        /// <summary>R&amp;T province id the change occurred in, for dedup and diagnostics. -1 when not region-bound.</summary>
        public int provinceId = -1;

        /// <summary>Human-readable region name at capture time.</summary>
        public string regionName;

        // Primary actor (the new owner / founder / rising belief / first contender).
        public string factionAId;
        public string factionAName;

        // Secondary actor (the previous owner / rival contender). May be absent.
        public string factionBId;
        public string factionBName;

        /// <summary>A short structured qualifier ("resolved", "hostile", "seized", "succeeded",
        /// "failed") kept out of the names so dedup and prose both stay clean. Optional.</summary>
        public string qualifier;

        /// <summary>What the event is about, when a name is not enough — e.g. the quest's title for a
        /// <see cref="WorldNewsEventKind.QuestOutcome"/>. Optional.</summary>
        public string subject;

        public WorldNewsEvent() { }

        public WorldNewsEvent(WorldNewsEventKind kind, int ticksGame, int provinceId, string regionName,
            string factionAId, string factionAName, string factionBId = null, string factionBName = null,
            string qualifier = null, string subject = null)
        {
            this.kind = kind;
            this.ticksGame = ticksGame;
            this.provinceId = provinceId;
            this.regionName = regionName;
            this.factionAId = factionAId;
            this.factionAName = factionAName;
            this.factionBId = factionBId;
            this.factionBName = factionBName;
            this.qualifier = qualifier;
            this.subject = subject;
        }

        /// <summary>
        /// Identity for dedup: kind + region + the two actors' <b>ids</b> (not names — a rename must not
        /// re-fire the same event). A border oscillating across the ownership threshold produces the
        /// same key each swing, so <see cref="WorldMapChangeFeed"/>'s cooldown collapses the flapping
        /// into at most one article per cooldown.
        /// </summary>
        public string DedupKey => $"{kind}|{provinceId}|{factionAId}|{factionBId}|{subject}";

        private static string Name(string name, string fallback = "an unknown faction")
            => string.IsNullOrEmpty(name) ? fallback : name;

        private string RegionPhrase => string.IsNullOrEmpty(regionName) ? "an outlying region" : $"the province of {regionName}";

        /// <summary>
        /// Render the fact as a compact news line for the generator's event batch. This is deliberately
        /// factual, not dramatic — the LLM editor turns it into a story. The date stamp matches the
        /// bracketed form the letter/gossip pipeline already uses, so world-map lines sit uniformly in
        /// the same batch.
        /// </summary>
        public string ToNewsLine()
        {
            switch (kind)
            {
                case WorldNewsEventKind.BorderChange:
                    if (!string.IsNullOrEmpty(factionBId))
                        return $"World map: {RegionPhrase} changed hands — {Name(factionAName)} took it from {Name(factionBName)}.";
                    return $"World map: {Name(factionAName)} established control over {RegionPhrase}.";

                case WorldNewsEventKind.SettlementFounded:
                    return $"World map: {Name(factionAName)} founded a new settlement in {RegionPhrase}.";

                case WorldNewsEventKind.IdeologyShift:
                    return $"World map: the dominant belief in {RegionPhrase} has shifted toward that of {Name(factionAName)}.";

                case WorldNewsEventKind.BorderTension:
                    return $"World map: tension on the {(!string.IsNullOrEmpty(regionName) ? regionName : "outland")} frontier — " +
                           $"{Name(factionAName)} and {Name(factionBName)} both press their claim, and relations are strained.";

                case WorldNewsEventKind.SettlementAffair:
                {
                    string body = string.IsNullOrEmpty(subject) ? "an unremarkable day passed" : subject;
                    if (!string.IsNullOrEmpty(factionBId))
                    {
                        string rel = qualifier == "soured" ? $"relations with {Name(factionBName)} have soured"
                                   : qualifier == "warmed" ? $"relations with {Name(factionBName)} have warmed"
                                   : $"{Name(factionBName)} is watching closely";
                        return $"World map: in {RegionPhrase}, {body} — {rel}.";
                    }
                    return $"World map: in {RegionPhrase}, {body}.";
                }

                case WorldNewsEventKind.QuestOutcome:
                {
                    string venture = string.IsNullOrEmpty(subject) ? "a contract" : $"the venture \"{subject}\"";
                    string outcome = qualifier == "succeeded" ? "was carried off"
                                   : qualifier == "failed" ? "came to nothing"
                                   : "was concluded";
                    if (!string.IsNullOrEmpty(factionBId))
                        return $"World map: in {RegionPhrase}, {venture} {outcome} — {Name(factionAName)} and {Name(factionBName)}, who hold ground there, are watching the fallout.";
                    return $"World map: in {RegionPhrase}, {venture} {outcome} on {Name(factionAName)}'s territory, and the neighbours have opinions.";
                }

                default:
                    return $"World map: an unspecified change in {RegionPhrase}.";
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind", WorldNewsEventKind.BorderChange);
            Scribe_Values.Look(ref ticksGame, "ticksGame", 0);
            Scribe_Values.Look(ref provinceId, "provinceId", -1);
            Scribe_Values.Look(ref regionName, "regionName");
            Scribe_Values.Look(ref factionAId, "factionAId");
            Scribe_Values.Look(ref factionAName, "factionAName");
            Scribe_Values.Look(ref factionBId, "factionBId");
            Scribe_Values.Look(ref factionBName, "factionBName");
            Scribe_Values.Look(ref qualifier, "qualifier");
            Scribe_Values.Look(ref subject, "subject");
        }
    }

    /// <summary>
    /// A plain, R&amp;T-free snapshot of one region's ownership at a moment in time. WorldNews owns
    /// this type so it never appears in a method signature that would drag an R&amp;T type across the
    /// availability seam — <see cref="Integration.RegionsAndTerritoriesBridge"/> fills these in from
    /// R&amp;T state and hands back plain data, keeping every R&amp;T reference inside a guarded worker.
    /// </summary>
    public class ProvinceSnapshot
    {
        public int provinceId;
        public string regionName;

        /// <summary>The dominant owner's faction id, or null when the region is unowned.</summary>
        public string primaryOwnerId;
        public string primaryOwnerName;

        /// <summary>True when two factions contest the region within the ownership margin.</summary>
        public bool contested;

        /// <summary>Owning/contending faction ids, strongest first. Feeds tension analysis.</summary>
        public List<string> contenderIds = new List<string>();
        public List<string> contenderNames = new List<string>();

        /// <summary>The dominant belief in the region, or null.
        ///
        /// <para><b>Left null in production today.</b> A regional belief distribution is
        /// Regions-and-Territories#34; until it exists the bridge cannot fill this, so the
        /// ideology-shift detector — which compares this field across passes — naturally emits nothing.
        /// The field and its detector path exist so #34/#16 wire in without new plumbing, and so the
        /// path is exercisable in tests with a synthetic snapshot.</para></summary>
        public string primaryBeliefId;
        public string primaryBeliefName;
    }

    /// <summary>
    /// A plain, R&amp;T-free snapshot of one settlement/outpost, used to detect new foundations by
    /// diffing the set of keys between passes. <see cref="key"/> is a stable identity (the world
    /// object's load id) so a settlement moving between passes is not read as a new one.
    /// </summary>
    public class SettlementSnapshot
    {
        public string key;
        public int provinceId = -1;
        public string regionName;
        public string factionId;
        public string factionName;
    }
}
