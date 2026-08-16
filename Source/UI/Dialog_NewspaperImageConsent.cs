using RimWorld;
using UnityEngine;
using Verse;

namespace RimSynapse.WorldNews.UI
{
    /// <summary>
    /// First-run consent window for external newspaper illustrations (WorldNews#24). Shown once — the
    /// first time an issue is published, or when the player tries to enable illustrations in settings.
    /// Nothing is fetched from the internet until this has been answered with "Enable". Both buttons
    /// record that the choice was made, so it never nags again.
    /// </summary>
    public class Dialog_NewspaperImageConsent : Window
    {
        public override Vector2 InitialSize => new Vector2(560f, 340f);

        public Dialog_NewspaperImageConsent()
        {
            forcePause = true;
            doCloseX = false;              // force an explicit choice
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float btnH = 36f;
            Rect body = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - btnH - 12f);

            var ls = new Listing_Standard();
            ls.Begin(body);
            Text.Font = GameFont.Medium;
            ls.Label("Illustrated newspapers?");
            Text.Font = GameFont.Small;
            ls.Gap(6f);
            ls.Label(
                "RimSynapse can turn each issue's AI-written scene descriptions into pictures using "
                + "pollinations.ai — a free online image generator (no account, no key).\n\n"
                + "What is sent: only the scene description text (e.g. \"a dust-caked caravan at the "
                + "gates\"). No personal information, colony data, or save files.\n\n"
                + "Pictures are downloaded once and cached on your disk. You can change this any time in "
                + "the mod settings. With illustrations off, the newspaper stays text-only.");
            ls.End();

            float bw = (inRect.width - 16f) / 2f;
            float by = inRect.yMax - btnH;
            if (Widgets.ButtonText(new Rect(inRect.x, by, bw, btnH), "Enable illustrations"))
            {
                Decide(true);
            }
            if (Widgets.ButtonText(new Rect(inRect.x + bw + 16f, by, bw, btnH), "Keep text-only"))
            {
                Decide(false);
            }
        }

        private void Decide(bool enable)
        {
            var s = RimSynapseWorldNewsMod.Settings;
            if (s != null)
            {
                s.enableNewspaperImages = enable;
                s.imageConsentDecided = true;
                RimSynapseWorldNewsMod.Instance?.WriteSettings();
            }
            Messages.Message(
                enable ? "Newspaper illustrations enabled." : "Newspaper will stay text-only.",
                MessageTypeDefOf.TaskCompletion, historical: false);
            Close();
        }
    }
}
