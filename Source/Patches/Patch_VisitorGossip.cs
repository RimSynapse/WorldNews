using HarmonyLib;
using Verse;
using RimWorld;
using RimSynapse;
using RimSynapse.Models;

namespace RimSynapse.WorldNews.Patches
{
    [HarmonyPatch(typeof(SynapseCoreWorldComponent), nameof(SynapseCoreWorldComponent.EnqueuePastEvent))]
    public static class Patch_SynapseCoreWorldComponent_EnqueuePastEvent
    {
        public static void Postfix(PastEvent pastEvent)
        {
            if (pastEvent == null || pastEvent.category != "VisitorRumorSpreading") return;

            var newsComp = Find.World?.GetComponent<SynapseWorldNewsWorldComponent>();
            if (newsComp == null) return;

            var tm = Find.TickManager;
            // GenLocalDate.Twelfth/Year take a tile, not ticks — passing ticks reads an
            // out-of-range PlanetTile past ~day 5. Use GenDate at longitude 0 (planetary stamp).
            string stamp = tm != null
                ? $"[{GenDate.Twelfth(tm.TicksAbs, 0f)}, {GenDate.Year(tm.TicksAbs, 0f)}]"
                : "[unknown date]";

            // Through RecordEvent rather than touching the list and re-implementing the threshold:
            // this copy had its own "count >= 4 then generate" and so bypassed the queue cap, the
            // publish cooldown and the in-flight guard entirely.
            newsComp.RecordEvent($"{stamp} Gossip: {pastEvent.eventDescription}");
        }
    }
}
