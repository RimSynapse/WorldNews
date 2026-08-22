using System;
using Verse;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>The inputs a settlement affair is adjudicated from — who it happens to, an optional
    /// rival for conflict outcomes, and where. Plain data so adjudication stays pure and testable.</summary>
    public class AffairContext
    {
        public string settlementFactionId;
        public string settlementFactionName;
        public string rivalFactionId;      // may be null → only local (flavour) outcomes are eligible
        public string rivalFactionName;
        public string regionName;
        public int provinceId = -1;
        public int nowTicks;
    }

    /// <summary>The adjudicated result: the news event, plus a bounded short-term relation delta to
    /// apply between the two factions (0 for flavour-only outcomes).</summary>
    public struct AffairResult
    {
        public WorldNewsEvent evt;
        public int relationDelta;     // in [-MaxRelationDelta, +MaxRelationDelta]; 0 when no rival is involved
        public string aId;            // settlement faction (relation actor A)
        public string bId;            // rival faction (relation actor B), null when flavour-only
    }

    /// <summary>
    /// Random adjudication of a non-player settlement's daily event (the "settlement affairs" flavour
    /// layer). A settlement event is resolved to one of a weighted table of outcomes; conflict outcomes
    /// also carry a <b>bounded, temporary</b> nudge to the two factions' standing, while local outcomes
    /// are pure colour.
    ///
    /// <para>The relation nudge is capped at <see cref="MaxRelationDelta"/> and is applied as a
    /// short-term effect that decays back to zero (see <c>ShortTermRelationLedger</c>) — it moves how
    /// factions feel right now without rewriting their long-term baseline.</para>
    ///
    /// <para><see cref="Adjudicate"/> is pure (deterministic in its rolls) so every outcome and its
    /// delta are unit-testable; <see cref="Generate"/> is the live wrapper that supplies the rolls from
    /// <see cref="Rand"/>.</para>
    /// </summary>
    public static class SettlementAffairGenerator
    {
        /// <summary>Hard ceiling on a single adjudication's relation change, per the design: short-term
        /// standing moves by at most this much, and temporarily.</summary>
        public const int MaxRelationDelta = 10;

        private enum Tone { Local, Soured, Warmed }

        private struct Outcome
        {
            public string headline;   // {0} = settlement faction, {1} = rival faction
            public Tone tone;
            public Outcome(string headline, Tone tone) { this.headline = headline; this.tone = tone; }
            public bool InvolvesRival => tone != Tone.Local;
        }

        // Conflict outcomes (need a rival) come first; local/flavour outcomes follow. Conflict skews
        // toward souring — frontier neighbours fall out more easily than they befriend — but warming
        // outcomes exist so standings move both ways.
        private static readonly Outcome[] ConflictOutcomes =
        {
            new Outcome("{0}'s militia turned back a raid staged from {1}'s borderlands", Tone.Soured),
            new Outcome("a caravan bound for {0} was plundered, and the blame fell on {1}", Tone.Soured),
            new Outcome("a skirmish over a watering hole left {0} and {1} nursing grudges", Tone.Soured),
            new Outcome("{0} and {1} sealed a quiet trade accord at a frontier waystation", Tone.Warmed),
            new Outcome("{0} settled a long-running border dispute with {1} amicably", Tone.Warmed),
        };

        private static readonly Outcome[] LocalOutcomes =
        {
            new Outcome("{0}'s fields brought in a bountiful harvest", Tone.Local),
            new Outcome("a lantern festival lit {0}'s streets through the night", Tone.Local),
            new Outcome("an outbreak swept {0}'s quarter before its healers contained it", Tone.Local),
            new Outcome("{0} raised a new meeting hall and named a fresh council", Tone.Local),
            new Outcome("strange lights over {0} set the whole settlement to muttering", Tone.Local),
        };

        /// <summary>
        /// Adjudicate an affair from explicit rolls. <paramref name="conflictRoll"/> and the presence of
        /// a rival decide whether this is a conflict or a local story; <paramref name="outcomeRoll"/>
        /// picks the specific outcome; <paramref name="magnitude"/> (expected 1..<see cref="MaxRelationDelta"/>)
        /// sets how hard a conflict outcome moves the two factions' standing. Pure and deterministic.
        /// </summary>
        public static AffairResult Adjudicate(AffairContext ctx, float conflictRoll, float outcomeRoll, int magnitude)
        {
            bool rivalAvailable = ctx != null && !string.IsNullOrEmpty(ctx.rivalFactionId);
            // ~55% conflict when a rival exists; always local otherwise.
            bool conflict = rivalAvailable && conflictRoll < 0.55f;

            Outcome[] table = conflict ? ConflictOutcomes : LocalOutcomes;
            int idx = Clamp((int)(outcomeRoll * table.Length), 0, table.Length - 1);
            Outcome outcome = table[idx];

            string aName = Safe(ctx?.settlementFactionName, "a frontier settlement");
            string bName = Safe(ctx?.rivalFactionName, "a neighbour");
            string subject = string.Format(outcome.headline, aName, bName);

            int delta = 0;
            string bId = null, bNameForEvent = null, qualifier = null;
            if (outcome.InvolvesRival)
            {
                int mag = Clamp(magnitude, 1, MaxRelationDelta);
                delta = outcome.tone == Tone.Soured ? -mag : mag;
                bId = ctx.rivalFactionId;
                bNameForEvent = ctx.rivalFactionName;
                qualifier = outcome.tone == Tone.Soured ? "soured" : "warmed";
            }

            var evt = new WorldNewsEvent(
                WorldNewsEventKind.SettlementAffair, ctx?.nowTicks ?? 0, ctx?.provinceId ?? -1, ctx?.regionName,
                ctx?.settlementFactionId, ctx?.settlementFactionName, bId, bNameForEvent,
                qualifier: qualifier, subject: subject);

            return new AffairResult { evt = evt, relationDelta = delta, aId = ctx?.settlementFactionId, bId = bId };
        }

        /// <summary>Live adjudication: supplies the rolls from <see cref="Rand"/>. Call inside a fixed
        /// RNG state if determinism per settlement/day is wanted; otherwise it varies freely.</summary>
        public static AffairResult Generate(AffairContext ctx)
        {
            return Adjudicate(ctx, Rand.Value, Rand.Value, Rand.RangeInclusive(1, MaxRelationDelta));
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        private static string Safe(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
    }
}
