using System;
using System.Collections.Generic;
using Verse;
using RimWorld.Planet;
using RimSynapse.RegionsAndTerritories;

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
    }
}
