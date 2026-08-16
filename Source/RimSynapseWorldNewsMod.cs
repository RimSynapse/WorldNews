using Verse;
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

            ls.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}
