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
            if (newsComp != null)
            {
                string eventString = $"[{GenLocalDate.Twelfth(Find.TickManager.TicksGame)}, {GenLocalDate.Year(Find.TickManager.TicksGame)}] Gossip: {pastEvent.eventDescription}";
                newsComp.unpublishedEvents.Add(eventString);

                if (newsComp.unpublishedEvents.Count >= 4)
                {
                    newsComp.TriggerNewspaperGeneration();
                }
            }
        }
    }
}
