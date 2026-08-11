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
            Scribe_Values.Look(ref bannerX, "bannerX", -1f);
            Scribe_Values.Look(ref bannerY, "bannerY", -1f);
            Scribe_Values.Look(ref bannerW, "bannerW", 640f);
            Scribe_Values.Look(ref bannerH, "bannerH", 30f);
            Scribe_Values.Look(ref bannerCollapsed, "bannerCollapsed", false);
        }
    }
}
