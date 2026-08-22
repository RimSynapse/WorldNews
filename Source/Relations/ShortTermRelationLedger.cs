using System;
using System.Collections.Generic;
using Verse;

namespace RimSynapse.WorldNews.Relations
{
    /// <summary>One faction pair's currently-applied short-term goodwill offset.</summary>
    public class RelationNudge : IExposable
    {
        public string aId;
        public string bId;
        /// <summary>How much goodwill this pair currently carries from short-term affairs, in
        /// [-MaxOffset, +MaxOffset]. The ledger drives this back to zero over time.</summary>
        public int offset;

        public void ExposeData()
        {
            Scribe_Values.Look(ref aId, "aId");
            Scribe_Values.Look(ref bId, "bId");
            Scribe_Values.Look(ref offset, "offset", 0);
        }
    }

    /// <summary>
    /// Tracks and decays the <b>short-term</b> goodwill nudges that settlement affairs apply between
    /// factions. The design constraint is precise: a nudge moves a pair's standing by at most
    /// <see cref="MaxOffset"/>, and <b>temporarily</b> — the offset decays back to zero over a few
    /// days, so the long-term baseline is never rewritten. This ledger is what makes "affects
    /// short-term, not long-term" true.
    ///
    /// <para>The class holds only the policy (clamp to ±<see cref="MaxOffset"/>, decay toward zero) and
    /// the bookkeeping. The actual mechanism — changing faction goodwill — is injected as an
    /// <c>applier</c> delegate that applies a requested change and returns the amount it <i>actually</i>
    /// moved (goodwill saturates at ±100, so the requested and applied amounts can differ). Recording
    /// the applied amount is what lets the decay reverse <i>exactly</i> what was applied and land back
    /// on the original baseline even near a cap. Injecting the mechanism also keeps this unit-testable
    /// with no live factions, and lets the whole thing re-home to Core/Factions later without change.</para>
    /// </summary>
    public class ShortTermRelationLedger : IExposable
    {
        /// <summary>Cap on a pair's short-term offset. Matches the affair generator's per-adjudication cap.</summary>
        public const int MaxOffset = 10;

        /// <summary>How much of the offset is reversed per decay tick (one in-game day). A full ±10
        /// offset unwinds over ~4 days — long enough to matter, short enough to stay "short-term".</summary>
        public const int DecayStep = 3;

        private List<RelationNudge> nudges = new List<RelationNudge>();

        public int Count => nudges.Count;
        public IReadOnlyList<RelationNudge> Nudges => nudges;

        /// <summary>An applier that changes goodwill between two factions and returns the amount it
        /// actually moved (which may be smaller than requested near the ±100 cap, or 0).</summary>
        public delegate int GoodwillApplier(string aId, string bId, int requestedChange);

        /// <summary>
        /// Apply a short-term nudge to a pair, keeping the pair's total offset within ±<see cref="MaxOffset"/>.
        /// Only the headroom is actually requested, so repeated same-direction affairs on one day cannot
        /// push a pair past the cap. Returns the goodwill amount actually applied.
        /// </summary>
        public int ApplyNudge(string aId, string bId, int requestedDelta, GoodwillApplier applier)
        {
            if (string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId) || aId == bId || applier == null) return 0;
            if (requestedDelta == 0) return 0;

            RelationNudge entry = FindOrNull(aId, bId);
            int current = entry?.offset ?? 0;

            // Clamp the resulting offset to the cap; only request the difference.
            int target = Clamp(current + requestedDelta, -MaxOffset, MaxOffset);
            int toApply = target - current;
            if (toApply == 0) return 0;

            int actual = applier(aId, bId, toApply);
            if (actual == 0) return 0;

            if (entry == null)
            {
                entry = new RelationNudge { aId = aId, bId = bId, offset = 0 };
                nudges.Add(entry);
            }
            entry.offset += actual;
            if (entry.offset == 0) nudges.Remove(entry);
            return actual;
        }

        /// <summary>
        /// Decay every pair one step toward zero, reversing exactly what is still applied. Call once per
        /// in-game day. Entries that reach zero are dropped. Returns the number of pairs still decaying.
        /// </summary>
        public int TickDecay(GoodwillApplier applier)
        {
            if (applier == null) return nudges.Count;

            for (int i = nudges.Count - 1; i >= 0; i--)
            {
                RelationNudge n = nudges[i];
                if (n == null || n.offset == 0) { nudges.RemoveAt(i); continue; }

                // Reverse toward zero: the step is opposite in sign to the current offset.
                int reverse = Clamp(-n.offset, -DecayStep, DecayStep);
                int actual = applier(n.aId, n.bId, reverse);
                n.offset += actual;
                if (n.offset == 0) nudges.RemoveAt(i);
            }
            return nudges.Count;
        }

        private RelationNudge FindOrNull(string aId, string bId)
        {
            for (int i = 0; i < nudges.Count; i++)
            {
                RelationNudge n = nudges[i];
                if (n == null) continue;
                // Goodwill is symmetric, so the pair is unordered.
                if ((n.aId == aId && n.bId == bId) || (n.aId == bId && n.bId == aId)) return n;
            }
            return null;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        public void ExposeData()
        {
            Scribe_Collections.Look(ref nudges, "nudges", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && nudges == null) nudges = new List<RelationNudge>();
        }
    }
}
