using Verse;
using RimWorld;
using HarmonyLib;

namespace RimSynapse.WorldNews
{
    public class RimSynapseWorldNewsMod : Mod
    {
        public static SynapseModHandle ModHandle;
        public static RimSynapseWorldNewsMod Instance;
        public static RimSynapseWorldNewsSettings Settings;

        /// <summary>
        /// The on-disk root of the loaded WorldNews mod folder (whatever folder RimWorld loaded us
        /// from — local, Workshop, or symlink). Used to resolve bundled assets, e.g. the sample
        /// newspaper illustrations, without hard-coding a path. Set once at construction.
        /// </summary>
        public static string ContentRootDir;

        public RimSynapseWorldNewsMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimSynapseWorldNewsSettings>();
            ContentRootDir = content.RootDir;

            var harmony = new Harmony("rimsynapse.worldnews");
            harmony.PatchAll();

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                ModHandle = SynapseCore.Register(
                    "rimsynapse.worldnews",
                    "RimSynapse WorldNews"
                );

                // Announce the optional-dependency decision once, after every mod has loaded. This
                // is the headless confirmation that the R&T seam resolves correctly: a launch log
                // showing "world-map coverage enabled" proves detection works and that the direct
                // R&T assembly reference bound (a silent load failure would have taken this whole
                // constructor down instead). Reads only the JIT-safe Active guard — no R&T type and
                // no World required, so it is correct with R&T absent and before a game exists.
                RimSynapse.SynapseLogger.Message(Integration.RegionsAndTerritoriesBridge.Active
                    ? "[RimSynapse-WorldNews] Regions and Territories detected — world-map coverage enabled."
                    : "[RimSynapse-WorldNews] Regions and Territories not detected — colony-local news only.");
            });
        }

        public override string SettingsCategory() => "RimSynapse - WorldNews";

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            UnityEngine.GUI.color = new UnityEngine.Color(0.85f, 0.85f, 0.85f);
            ls.Label("Newspaper illustrations");
            UnityEngine.GUI.color = UnityEngine.Color.white;
            ls.GapLine(6f);

            bool before = Settings.enableNewspaperImages;
            ls.CheckboxLabeled(
                "Illustrate issues via an external service (pollinations.ai)",
                ref Settings.enableNewspaperImages,
                "When on, each issue's AI-written scene descriptions are sent to pollinations.ai — a free "
                + "image generator, no account or key — and the returned pictures are cached on disk. Off by "
                + "default; text-only otherwise. Only the scene prompt is sent, never personal or save data.");

            // First time it's switched on, route through the consent window rather than enabling silently.
            if (Settings.enableNewspaperImages && !before && !Settings.imageConsentDecided)
            {
                Settings.enableNewspaperImages = false;
                Find.WindowStack.Add(new UI.Dialog_NewspaperImageConsent());
            }

            ls.Gap(8f);
            string status = !Settings.imageConsentDecided
                ? "Status: not chosen yet — the first published issue will ask."
                : (Settings.enableNewspaperImages ? "Status: illustrations ON." : "Status: text-only.");
            UnityEngine.GUI.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);
            ls.Label(status);
            UnityEngine.GUI.color = UnityEngine.Color.white;

            // --- World-map news (WorldNews#13) ----------------------------------------------------
            ls.Gap(14f);
            UnityEngine.GUI.color = new UnityEngine.Color(0.85f, 0.85f, 0.85f);
            ls.Label("World-map news");
            UnityEngine.GUI.color = UnityEngine.Color.white;
            ls.GapLine(6f);

            bool rtActive = Integration.RegionsAndTerritoriesBridge.Active;
            if (!rtActive)
            {
                UnityEngine.GUI.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);
                ls.Label("Regions and Territories is not loaded — world-map coverage stands down; only "
                         + "colony-local news is reported. The toggles below take effect when it is present.");
                UnityEngine.GUI.color = UnityEngine.Color.white;
            }

            ls.CheckboxLabeled("Report world-map changes as news",
                ref Settings.enableWorldMapFeed,
                "Master switch. When on (and Regions and Territories is loaded), the newspaper draws on "
                + "changes across the world map — not just events in your colony.");

            if (Settings.enableWorldMapFeed)
            {
                ls.CheckboxLabeled("  Border changes",
                    ref Settings.detectBorderChanges,
                    "A region changing hands, or a contested frontier settling in someone's favour.");
                ls.CheckboxLabeled("  New settlements",
                    ref Settings.detectSettlementFounded,
                    "A new settlement or outpost appearing in a region.");
                ls.CheckboxLabeled("  Border tension",
                    ref Settings.detectBorderTension,
                    "Two hostile factions pressing competing claims on the same frontier — a forward-looking "
                    + "'what might happen next' story rather than a report of something already done.");
                ls.CheckboxLabeled("  Quest outcomes",
                    ref Settings.detectQuestOutcomes,
                    "When a quest resolves in a claimed region, the factions who hold that ground react to how "
                    + "it turned out — the neighbours have opinions, not just the quest giver.");

                bool ideologyDisabled = true; // gated: no regional belief distribution yet (R&T#34)
                bool prevIdeology = Settings.detectIdeologyShift;
                ls.CheckboxLabeled("  Regional ideology shifts (coming soon)",
                    ref Settings.detectIdeologyShift,
                    "A region's dominant belief changing. Requires a regional belief distribution that is "
                    + "not yet available, so this produces nothing today.");
                if (ideologyDisabled && Settings.detectIdeologyShift && !prevIdeology)
                {
                    // Let the player express intent, but it stays inert until R&T#34 lands.
                    Messages.Message("Regional ideology shifts are not yet available — no articles will be "
                        + "generated for them until a future update.", MessageTypeDefOf.RejectInput, false);
                }
            }

            // --- Settlement affairs (day-in-the-life NPC events) ----------------------------------
            ls.Gap(14f);
            UnityEngine.GUI.color = new UnityEngine.Color(0.85f, 0.85f, 0.85f);
            ls.Label("Settlement affairs");
            UnityEngine.GUI.color = UnityEngine.Color.white;
            ls.GapLine(6f);
            ls.CheckboxLabeled("Non-player settlements have eventful days",
                ref Settings.enableSettlementAffairs,
                "Each day, a fraction of non-player settlements generate a small adjudicated event — a "
                + "raid, a caravan, a festival, an outbreak. Conflict outcomes nudge the two factions' "
                + "standing by a bounded amount that fades over a few days; it never rewrites their "
                + "long-term relationship. Pure flavour for the newspaper; needs no other mods.");

            ls.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}
