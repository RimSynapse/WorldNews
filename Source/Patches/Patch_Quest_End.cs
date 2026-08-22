using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.WorldNews.Quests;

namespace RimSynapse.WorldNews.Patches
{
    /// <summary>
    /// Hooks vanilla's quest resolution (<see cref="Quest.End"/>) so a resolved quest can become news
    /// the surrounding factions react to (WorldNews#14). We hook the real resolution point rather than
    /// reimplementing quest outcomes — vanilla already generates the input.
    ///
    /// <para>Resolved by reflection with a single log line if the method cannot be found, the standing
    /// rule for patching anything outside our own assemblies: a signature change in a future RimWorld
    /// version degrades to "no quest news", never a load-time crash that takes the whole mod down.
    /// <see cref="Prepare"/> returning false skips the patch cleanly.</para>
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Quest_End
    {
        // The resolution point. Resolved once via AccessTools so a rename/resignature is a graceful
        // stand-down (Prepare logs and returns false) instead of a PatchAll exception.
        private static MethodBase ResolveTarget()
            => AccessTools.Method(typeof(Quest), nameof(Quest.End));

        static bool Prepare()
        {
            if (ResolveTarget() != null) return true;
            RimSynapse.SynapseLogger.Message(
                "[RimSynapse-WorldNews] Could not resolve Quest.End — quest-outcome news is disabled for this " +
                "game version (reported once). Everything else in WorldNews is unaffected.");
            return false;
        }

        static MethodBase TargetMethod() => ResolveTarget();

        static void Postfix(Quest __instance, QuestEndOutcome outcome)
        {
            // Never let our reaction throw into vanilla's quest teardown.
            try
            {
                QuestNewsReactor.OnQuestEnded(__instance, outcome);
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Message(
                    $"[RimSynapse-WorldNews] Quest-outcome reaction threw and was swallowed: {ex}");
            }
        }
    }
}
