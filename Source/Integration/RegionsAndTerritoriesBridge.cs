using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using RimWorld.Planet;
using RimSynapse.RegionsAndTerritories;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Integration
{
    /// <summary>
    /// The single seam between WorldNews and Regions-and-Territories.
    ///
    /// <para>R&amp;T is an <b>optional</b> dependency (About.xml declares <c>loadAfter</c>, not
    /// <c>modDependencies</c>): WorldNews must load and report colony-local news with R&amp;T
    /// absent, and light up world-map coverage only when it is present. That constraint dictates
    /// the shape of this class.</para>
    ///
    /// <para><b>The JIT rule this class exists to obey.</b> Mono compiles a method body in full the
    /// first time the method is <i>called</i>, and a body that names a type from a missing assembly
    /// throws at that point — the type does not have to be reached, only referenced. So an
    /// <c>if (Active)</c> guard sitting in the same method as an R&amp;T type reference does
    /// <b>not</b> protect it: entering the method is already too late. Every method here is therefore
    /// one of two kinds, and they are never merged:</para>
    /// <list type="bullet">
    ///   <item><description><b>Guards</b> (<see cref="Active"/>, <see cref="ComputeActive"/>, the
    ///   public <c>Try*</c> entry points) name no R&amp;T type, so they JIT safely whether or not
    ///   R&amp;T is loaded.</description></item>
    ///   <item><description><b>Workers</b> (the private <c>*Internal</c> methods) touch R&amp;T
    ///   types and are only ever invoked once a guard has confirmed <see cref="Active"/>, so their
    ///   JIT happens only when the R&amp;T assembly is present and its types resolve.</description></item>
    /// </list>
    /// Do not inline a worker into its guard. That single edit reintroduces the silent-load-failure
    /// class of bug the optional dependency is designed around.
    /// </summary>
    public static class RegionsAndTerritoriesBridge
    {
        /// <summary>R&amp;T's packageId as written in its About.xml. Compared case-insensitively
        /// because ModsConfig lowercases ids and a Steam copy carries a "_steam" suffix that
        /// <see cref="ModContentPack.PackageIdPlayerFacing"/> strips.</summary>
        private const string RtPackageId = "RimSynapse.RegionsAndTerritories";

        // Whether R&T is loaded does not change during a session, so the scan runs once. Nullable so
        // "not yet computed" is distinct from "computed false".
        private static bool? cachedActive;

        /// <summary>True when Regions-and-Territories is loaded and its types can be bound. Cheap
        /// after the first call. Names no R&amp;T type, so it is safe to call with R&amp;T absent.</summary>
        public static bool Active
        {
            get
            {
                if (cachedActive == null) cachedActive = ComputeActive();
                return cachedActive.Value;
            }
        }

        // GUARD — no R&T type referenced, JIT-safe when R&T is absent.
        private static bool ComputeActive()
        {
            var mods = LoadedModManager.RunningModsListForReading;
            if (mods == null) return false;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m != null && string.Equals(m.PackageIdPlayerFacing, RtPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A one-line summary of R&amp;T's current world state, for diagnostics and the seam's debug
        /// action. Returns false (and leaves <paramref name="summary"/> null) when R&amp;T is absent;
        /// callers use that to decide whether world-map coverage is available at all.
        /// </summary>
        // GUARD — dispatches to the worker only after the Active check. Names no R&T type itself.
        public static bool TryDescribeWorldState(out string summary)
        {
            summary = null;
            if (!Active) return false;
            summary = DescribeWorldStateInternal();
            return true;
        }

        // WORKER — touches R&T types. Only reached via TryDescribeWorldState once Active is true, so
        // its JIT never happens while R&T is missing. Never call directly; never inline into a guard.
        //
        // Reads ProvincesRaw, not the Provinces getter: the getter lazily GENERATES provinces on
        // first access, and a diagnostic must never have that side effect. A null raw list (a world
        // that genuinely has not generated provinces yet) reads as zero, not as an error.
        private static string DescribeWorldStateInternal()
        {
            SynapseRegionManager mgr = Find.World?.GetComponent<SynapseRegionManager>();
            List<GeographicProvince> provinces = mgr?.ProvincesRaw;
            if (provinces == null)
            {
                return "R&T active; no province data on this world yet.";
            }

            int owned = 0;
            for (int i = 0; i < provinces.Count; i++)
            {
                GeographicProvince p = provinces[i];
                if (p != null && p.owningFactionIds != null && p.owningFactionIds.Count > 0) owned++;
            }

            return $"R&T active; {provinces.Count} provinces, {owned} with a listed owner.";
        }

        // ---- World-map snapshots for the change feed (WorldNews#13) --------------------------------
        //
        // Both entry points below follow the same guard/worker split as the rest of this class: the
        // public Try* method names no R&T type and dispatches to a private *Internal worker only after
        // Active is confirmed. The out parameters are WorldNews-owned plain DTOs (ProvinceSnapshot,
        // SettlementSnapshot), so no R&T type ever crosses the seam and the detector/feed downstream
        // stay R&T-free and unit-testable.

        /// <summary>
        /// Snapshot every province's ownership as plain data the change detector can diff. Returns false
        /// (empty snapshot) when R&amp;T is absent or the world has no province data yet — the feed then
        /// simply produces nothing, which is the R&amp;T-absent behaviour by design.
        /// </summary>
        // GUARD — names no R&T type; dispatches to the worker only once Active.
        public static bool TrySnapshotWorld(out List<ProvinceSnapshot> snapshot)
        {
            snapshot = null;
            if (!Active) return false;
            snapshot = SnapshotWorldInternal();
            return snapshot != null;
        }

        // WORKER — touches R&T types. Reached only via TrySnapshotWorld once Active is true.
        //
        // RecalculateProvinceOwners is self-gated on an ownership epoch + faction count, so calling it
        // here is a no-op unless a holding actually changed since the last pass — cheap to call every
        // sample, and it guarantees ownershipData is current before we read it. ProvincesRaw (not the
        // generating Provinces getter) is read so a diagnostic snapshot never has the side effect of
        // generating provinces on a world that has none.
        private static List<ProvinceSnapshot> SnapshotWorldInternal()
        {
            var result = new List<ProvinceSnapshot>();
            SynapseRegionManager mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr == null) return result;

            mgr.RecalculateProvinceOwners();

            List<GeographicProvince> provinces = mgr.ProvincesRaw;
            if (provinces == null) return result;

            for (int i = 0; i < provinces.Count; i++)
            {
                GeographicProvince p = provinces[i];
                if (p == null) continue;
                result.Add(BuildProvinceSnapshot(p));
            }
            return result;
        }

        // WORKER helper — touches R&T types. Builds one province's plain snapshot from its ownership
        // data. Shared by the whole-world snapshot and the single-tile lookup so the two can never
        // disagree about how a region's owners are read.
        private static ProvinceSnapshot BuildProvinceSnapshot(GeographicProvince p)
        {
            var snap = new ProvinceSnapshot { provinceId = p.id, regionName = p.name };

            RegionalOwnershipData data = p.ownershipData;
            if (data != null)
            {
                Faction primary = data.PrimaryOwner;
                if (primary != null)
                {
                    snap.primaryOwnerId = primary.GetUniqueLoadID();
                    snap.primaryOwnerName = primary.Name;
                }
                snap.contested = data.IsContested();

                // Contenders are ordered strongest-first; the tension rule reads the top two.
                List<FactionOwnershipScore> contenders = data.Contenders();
                if (contenders != null)
                {
                    for (int c = 0; c < contenders.Count; c++)
                    {
                        Faction f = contenders[c]?.faction;
                        if (f == null) continue;
                        snap.contenderIds.Add(f.GetUniqueLoadID());
                        snap.contenderNames.Add(f.Name);
                    }
                }
            }

            // primaryBeliefId/Name deliberately left null: the regional belief distribution is
            // Regions-and-Territories#34 and does not exist yet, so the ideology-shift detector
            // stays inert until it does.
            return snap;
        }

        /// <summary>
        /// Resolve the region a world tile falls in, as a plain snapshot of its ownership. Used to turn
        /// a resolved quest's location into the set of factions whose ground it happened on (WorldNews#14).
        /// Returns false with R&amp;T absent, and false (null snapshot) when the tile is not in any known
        /// region — the caller treats "no region" as "unclaimed territory → produce nothing".
        /// </summary>
        // GUARD — names no R&T type.
        public static bool TryDescribeRegionAtTile(int tile, out ProvinceSnapshot snapshot)
        {
            snapshot = null;
            if (!Active) return false;
            snapshot = DescribeRegionAtTileInternal(tile);
            return snapshot != null;
        }

        // WORKER — touches R&T types. Only via the guard once Active.
        private static ProvinceSnapshot DescribeRegionAtTileInternal(int tile)
        {
            if (tile < 0) return null;
            SynapseRegionManager mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr == null) return null;

            mgr.RecalculateProvinceOwners();   // self-gated; ensures ownershipData is current
            GeographicProvince province = mgr.GetProvinceForTile(tile);
            return province != null ? BuildProvinceSnapshot(province) : null;
        }

        /// <summary>
        /// Snapshot the current permanent holdings (settlements, outposts, military) as plain data, each
        /// resolved to the region it stands in. The change detector diffs the key set between passes to
        /// find new foundations. Returns false with R&amp;T absent.
        /// </summary>
        // GUARD — names no R&T type.
        public static bool TrySnapshotSettlements(out List<SettlementSnapshot> snapshot)
        {
            snapshot = null;
            if (!Active) return false;
            snapshot = SnapshotSettlementsInternal();
            return snapshot != null;
        }

        // WORKER — touches R&T types (WorldObjectClassifier, SynapseRegionManager). Only via the guard.
        //
        // Classification is mod-agnostic: a VOE outpost, an Empire holding and a vanilla settlement all
        // resolve through WorldObjectClassifier without this code naming any of those mods. Transient
        // objects (camps, caravans, sites) are skipped — a founding is a permanent holding appearing.
        private static List<SettlementSnapshot> SnapshotSettlementsInternal()
        {
            var result = new List<SettlementSnapshot>();
            if (Find.WorldObjects == null) return result;
            SynapseRegionManager mgr = Find.World?.GetComponent<SynapseRegionManager>();

            List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                WorldObject obj = objects[i];
                if (obj == null) continue;

                WorldObjectKind kind = WorldObjectClassifier.Classify(obj);
                if (!kind.IsPermanentHolding()) continue;

                var snap = new SettlementSnapshot
                {
                    key = obj.GetUniqueLoadID(),
                    factionId = obj.Faction?.GetUniqueLoadID(),
                    factionName = obj.Faction?.Name,
                };

                GeographicProvince province = mgr?.GetProvinceForTile(obj.Tile);
                if (province != null)
                {
                    snap.provinceId = province.id;
                    snap.regionName = province.name;
                }
                result.Add(snap);
            }
            return result;
        }
    }
}
