using HarmonyLib;
using Verse;
using RimWorld;

namespace RimSynapse.WorldNews.Patches
{
    [HarmonyPatch(typeof(LetterStack), "ReceiveLetter")]
    [HarmonyPatch(new[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
    public static class LetterStack_ReceiveLetter_Patch
    {
        public static void Postfix(Letter let, bool __runOriginal)
        {
            if (let == null) return;

            // Postfixes run even when a prefix cancels the original. Core's deferred-news prefix
            // holds deferrable letters (returns false) and re-injects them on release; recording here
            // while the original was skipped would put the same letter in the paper twice — once at
            // intercept, once at release (WorldNews#35). Only record letters actually delivered.
            if (!__runOriginal) return;

            var worldComp = Find.World?.GetComponent<SynapseWorldNewsWorldComponent>();
            if (worldComp != null)
            {
                worldComp.RecordEventFromLetter(let);
            }
        }
    }
}
