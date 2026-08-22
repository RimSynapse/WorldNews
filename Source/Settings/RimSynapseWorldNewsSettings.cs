using Verse;

namespace RimSynapse.WorldNews
{
    /// <summary>
    /// WorldNews mod settings. Newspaper illustrations are an opt-in external feature (WorldNews#24):
    /// both flags default false, so nothing is fetched from the internet until the player has been
    /// shown the consent window and chosen to enable it.
    /// </summary>
    public class RimSynapseWorldNewsSettings : ModSettings
    {
        /// <summary>Master gate for external image generation. False → newspapers render text-only.</summary>
        public bool enableNewspaperImages = false;

        /// <summary>Whether the first-run consent window has been answered (either way).</summary>
        public bool imageConsentDecided = false;

        // --- World-map change feed (WorldNews#13) -------------------------------------------------
        //
        // A master gate plus one toggle per detector, so a misfiring link can be switched off and
        // observed in isolation ("make each link independently switchable"). Border/settlement/tension
        // default on; ideology-shift defaults OFF because it depends on the R&T regional belief
        // distribution (Regions-and-Territories#34), which does not exist yet — its detector is inert
        // regardless, but the toggle is off so its intent is explicit.
        public bool enableWorldMapFeed = true;
        public bool detectBorderChanges = true;
        public bool detectSettlementFounded = true;
        public bool detectBorderTension = true;
        public bool detectIdeologyShift = false;
        // Quest outcomes reacted to by the factions whose territory the quest sat in (WorldNews#14).
        public bool detectQuestOutcomes = true;

        /// <summary>Daily "settlement affairs": ~10% of non-player settlements generate an adjudicated
        /// event, and conflict outcomes nudge the two factions' short-term standing (bounded, temporary).
        /// Independent of the world-map feed — it needs no Regions and Territories.</summary>
        public bool enableSettlementAffairs = true;

        // --- Breaking-news banner placement (movable/resizable/collapsible; -1 = use default) ---
        public float bannerX = -1f;
        public float bannerY = -1f;
        public float bannerW = 640f;
        public float bannerH = 30f;
        public bool bannerCollapsed = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enableNewspaperImages, "enableNewspaperImages", false);
            Scribe_Values.Look(ref imageConsentDecided, "imageConsentDecided", false);
            Scribe_Values.Look(ref enableWorldMapFeed, "enableWorldMapFeed", true);
            Scribe_Values.Look(ref detectBorderChanges, "detectBorderChanges", true);
            Scribe_Values.Look(ref detectSettlementFounded, "detectSettlementFounded", true);
            Scribe_Values.Look(ref detectBorderTension, "detectBorderTension", true);
            Scribe_Values.Look(ref detectIdeologyShift, "detectIdeologyShift", false);
            Scribe_Values.Look(ref detectQuestOutcomes, "detectQuestOutcomes", true);
            Scribe_Values.Look(ref enableSettlementAffairs, "enableSettlementAffairs", true);
            Scribe_Values.Look(ref bannerX, "bannerX", -1f);
            Scribe_Values.Look(ref bannerY, "bannerY", -1f);
            Scribe_Values.Look(ref bannerW, "bannerW", 640f);
            Scribe_Values.Look(ref bannerH, "bannerH", 30f);
            Scribe_Values.Look(ref bannerCollapsed, "bannerCollapsed", false);
        }
    }
}
