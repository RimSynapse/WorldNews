using System;
using System.Collections.Generic;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>
    /// The rules that turn a pair of world-map snapshots into <see cref="WorldNewsEvent"/> facts
    /// (WorldNews#13). Pure functions over plain DTOs — no R&amp;T type, no world query, no clock —
    /// so every rule (threshold crossing, tension, foundation, ideology shift) is unit-testable on
    /// synthetic input (WorldNews#13 Tier-1). The caller supplies the "now" tick and, for tension,
    /// a hostility predicate, so the detector never reaches into live state.
    ///
    /// <para>Each of the four kinds is produced by its own method and gated by its own settings
    /// toggle at the call site, satisfying the issue's "make each link independently switchable":
    /// a misfiring link can be disabled and observed in isolation.</para>
    /// </summary>
    public static class WorldMapChangeDetector
    {
        /// <summary>
        /// Border changes between two ownership snapshots: a region whose primary owner changed, or a
        /// contested region that resolved to a single owner. Keyed by province id; provinces absent
        /// from either side are ignored (generation/removal is not a border story). A change to
        /// <i>unowned</i> is skipped — losing a region to no one is not "changing hands".
        /// </summary>
        public static List<WorldNewsEvent> DetectBorderChanges(
            IReadOnlyList<ProvinceSnapshot> prev, IReadOnlyList<ProvinceSnapshot> curr, int nowTicks)
        {
            var results = new List<WorldNewsEvent>();
            if (prev == null || curr == null) return results;

            Dictionary<int, ProvinceSnapshot> before = Index(prev);

            foreach (var now in curr)
            {
                if (now == null) continue;
                if (!before.TryGetValue(now.provinceId, out ProvinceSnapshot was)) continue;

                bool ownerChanged = !string.Equals(was.primaryOwnerId, now.primaryOwnerId, StringComparison.Ordinal);

                if (ownerChanged && !string.IsNullOrEmpty(now.primaryOwnerId))
                {
                    // Changed hands to a real owner. B (the previous owner) may be null → "established control".
                    results.Add(new WorldNewsEvent(
                        WorldNewsEventKind.BorderChange, nowTicks, now.provinceId, now.regionName,
                        now.primaryOwnerId, now.primaryOwnerName,
                        was.primaryOwnerId, was.primaryOwnerName,
                        qualifier: "seized"));
                }
                else if (!ownerChanged && was.contested && !now.contested && !string.IsNullOrEmpty(now.primaryOwnerId))
                {
                    // Same holder, but the contest around it resolved in their favour — a settling of the frontier.
                    results.Add(new WorldNewsEvent(
                        WorldNewsEventKind.BorderChange, nowTicks, now.provinceId, now.regionName,
                        now.primaryOwnerId, now.primaryOwnerName,
                        qualifier: "resolved"));
                }
            }
            return results;
        }

        /// <summary>
        /// Border tension: a region contested by two factions whose relations are poor. This reports a
        /// <i>condition</i>, not an event — the forward-looking "what might happen next". Hostility is
        /// supplied by <paramref name="areHostile"/> (faction-id pair → bool) so the rule stays pure;
        /// production passes a predicate backed by the faction manager, tests pass a stub.
        /// </summary>
        public static List<WorldNewsEvent> DetectBorderTension(
            IReadOnlyList<ProvinceSnapshot> curr, int nowTicks, Func<string, string, bool> areHostile)
        {
            var results = new List<WorldNewsEvent>();
            if (curr == null) return results;

            foreach (var now in curr)
            {
                if (now == null || !now.contested) continue;
                if (now.contenderIds == null || now.contenderIds.Count < 2) continue;

                string aId = now.contenderIds[0];
                string bId = now.contenderIds[1];
                if (string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId)) continue;
                if (areHostile != null && !areHostile(aId, bId)) continue; // contested but friendly → no tension story

                string aName = NameAt(now.contenderNames, 0);
                string bName = NameAt(now.contenderNames, 1);
                results.Add(new WorldNewsEvent(
                    WorldNewsEventKind.BorderTension, nowTicks, now.provinceId, now.regionName,
                    aId, aName, bId, bName, qualifier: "hostile"));
            }
            return results;
        }

        /// <summary>
        /// New foundations: settlement keys present now but not in the previous pass. A settlement
        /// whose region is unknown still emits (region reads as "an outlying region"); the caller may
        /// choose to prefer known regions.
        /// </summary>
        public static List<WorldNewsEvent> DetectSettlementsFounded(
            HashSet<string> prevKeys, IReadOnlyList<SettlementSnapshot> curr, int nowTicks)
        {
            var results = new List<WorldNewsEvent>();
            if (curr == null) return results;

            foreach (var s in curr)
            {
                if (s == null || string.IsNullOrEmpty(s.key)) continue;
                if (prevKeys != null && prevKeys.Contains(s.key)) continue;

                results.Add(new WorldNewsEvent(
                    WorldNewsEventKind.SettlementFounded, nowTicks, s.provinceId, s.regionName,
                    s.factionId, s.factionName));
            }
            return results;
        }

        /// <summary>
        /// Ideology shift: a region whose dominant belief changed between passes. Inert in production
        /// today because the bridge leaves <see cref="ProvinceSnapshot.primaryBeliefId"/> null until
        /// the R&amp;T distribution model (Regions-and-Territories#34) exists — a null-to-null compare
        /// never fires. The path is here so #34/#16 need no new plumbing, and so the rule can be tested
        /// against a synthetic snapshot that does carry a belief.
        /// </summary>
        public static List<WorldNewsEvent> DetectIdeologyShifts(
            IReadOnlyList<ProvinceSnapshot> prev, IReadOnlyList<ProvinceSnapshot> curr, int nowTicks)
        {
            var results = new List<WorldNewsEvent>();
            if (prev == null || curr == null) return results;

            Dictionary<int, ProvinceSnapshot> before = Index(prev);

            foreach (var now in curr)
            {
                if (now == null || string.IsNullOrEmpty(now.primaryBeliefId)) continue;
                if (!before.TryGetValue(now.provinceId, out ProvinceSnapshot was)) continue;
                if (string.IsNullOrEmpty(was.primaryBeliefId)) continue; // first time we know the belief is not a "shift"
                if (string.Equals(was.primaryBeliefId, now.primaryBeliefId, StringComparison.Ordinal)) continue;

                results.Add(new WorldNewsEvent(
                    WorldNewsEventKind.IdeologyShift, nowTicks, now.provinceId, now.regionName,
                    now.primaryBeliefId, now.primaryBeliefName, was.primaryBeliefId, was.primaryBeliefName));
            }
            return results;
        }

        private static Dictionary<int, ProvinceSnapshot> Index(IReadOnlyList<ProvinceSnapshot> list)
        {
            var map = new Dictionary<int, ProvinceSnapshot>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p != null) map[p.provinceId] = p;
            }
            return map;
        }

        private static string NameAt(List<string> names, int i)
            => (names != null && i < names.Count) ? names[i] : null;
    }
}
