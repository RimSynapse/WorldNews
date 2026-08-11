using HarmonyLib;
using RimSynapse.WorldNews.UI;
using RimWorld;
using Verse;

namespace RimSynapse.WorldNews.Patches
{
    /// <summary>
    /// Draws the breaking-news banner (<see cref="NewspaperBanner"/>) each frame, on top of the play
    /// UI, on both the colony map and the world view. Postfix so it never disturbs the game's own UI;
    /// the banner self-gates on pending news + comms availability, so this is a cheap no-op otherwise.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class Patch_UIRoot_Play_Banner
    {
        public static void Postfix()
        {
            try { NewspaperBanner.Draw(); }
            catch (System.Exception ex)
            {
                RimSynapse.SynapseLogger.Warn("worldnews", $"[RimSynapse-WorldNews] Banner draw failed: {ex.Message}");
            }
        }
    }
}
