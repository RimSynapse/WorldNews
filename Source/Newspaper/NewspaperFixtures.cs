using System.IO;
using System.Collections.Generic;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>
    /// A hand-authored, fully-populated <see cref="NewspaperIssue"/> used to iterate the broadsheet
    /// layout without a live LLM or the image pipeline. It deliberately covers every branch the
    /// renderer must handle: a masthead with all fields, a lead story with a byline and a large
    /// illustration, one secondary with a smaller illustration, one text-only secondary, two
    /// sidebars and two ads, and a section footer.
    ///
    /// <para>The two illustrations are the bundled sample PNGs under
    /// <c>Textures/WorldNews/Samples/</c>, resolved to absolute paths off the loaded mod folder and
    /// fed through the same <see cref="NewspaperImageCache"/> the real pipeline will use — so the
    /// layout is tuned against genuine on-disk assets at their real aspect ratios.</para>
    /// </summary>
    public static class NewspaperFixtures
    {
        /// <summary>Absolute path to a bundled sample illustration, or null if the mod root is unknown.</summary>
        private static string SamplePath(string fileName)
        {
            string root = RimSynapseWorldNewsMod.ContentRootDir;
            if (string.IsNullOrEmpty(root)) return null;
            return Path.Combine(root, "Textures", "WorldNews", "Samples", fileName);
        }

        /// <summary>
        /// Build the sample issue. Fresh instance per call so debug re-opens always reflect the
        /// current asset files (the texture cache still dedupes the actual decode by path).
        /// </summary>
        public static NewspaperIssue SampleIssue()
        {
            var issue = new NewspaperIssue
            {
                PaperName = "The Rimworld Gazette",
                Volume = "VOL. CXXXIV",
                IssueNumber = "NO. 248",
                Date = "5th of Jugust, 5502",
                Price = "$1.50",
                Strapline = "All the news the frontier can stomach",
                Headline = "COUNCIL PASSES STRICT NEW PARK RULES IN TENSE MIDNIGHT VOTE",
                SectionFooter = "SECTION A",
                Stories = new List<NewspaperStory>
                {
                    new NewspaperStory
                    {
                        Title = "Council Passes Strict New Park Rules",
                        Byline = "By David Chen, City Hall Correspondent",
                        Content =
                            "After six hours of impassioned testimony that spilled well past midnight, the " +
                            "settlement council voted four to three to close the commons after dark and bar " +
                            "open flame within its bounds. Opponents packed the gallery, some waving hastily " +
                            "lettered signs, while the Warden's deputies looked on from the doors.\n\n" +
                            "\"We built that green with our own hands,\" one homesteader shouted as the tally " +
                            "was read. Supporters countered that three fires in as many quadrums had left them " +
                            "no choice. The ordinance takes effect at first light.\n\n" +
                            "The measure had simmered since the dry lightning of last Septober, when a stray " +
                            "ember from a midnight cook-fire leapt the herb beds and took the roof off the old " +
                            "seed-store before the bucket line could form. No one was killed, but the loss of " +
                            "the winter seed stock is still felt in every thin bowl of paste this quadrum.\n\n" +
                            "Councillor Vane, who cast the deciding vote, defended the closure from the steps " +
                            "afterward. \"A park is a fine thing,\" she said, pulling her coat against the wind, " +
                            "\"but a granary is a living thing, and the two cannot share a spark. I will not " +
                            "preside over another hungry Decembary.\"\n\n" +
                            "Her opponents were unmoved. A petition to overturn the ruling was circulating " +
                            "before the lamps were even dimmed, and organizers claim upward of forty marks " +
                            "already — no small number in a settlement that counts its adults in the low " +
                            "hundreds. The Warden's office has declined to say whether it will enforce the " +
                            "curfew with patrols or merely with fines.\n\n" +
                            "For now the commons stands quiet under the double moonlight, its fire-pits raked " +
                            "cold and its gates newly hung. Whether the ordinance holds through the thaw, or " +
                            "burns away with the first warm night and the first stubborn ember, is a question " +
                            "the frontier has answered both ways before.",
                        ImagePrompt = "a packed frontier town-hall meeting, tense faces, dramatic lamp light",
                        ImageCaption = "Opponents voice their concerns at last night's hearing.",
                        ResolvedImagePath = SamplePath("townhall.png"),
                    },
                    new NewspaperStory
                    {
                        Title = "Dust-Caked Caravan Limps In From the East",
                        Byline = "By Mara Okonkwo",
                        Content =
                            "A trade caravan three days overdue reached the east gate at dusk, its pack-beasts " +
                            "lathered and its guards short two hands. The traders spoke of a washed-out pass and " +
                            "a night spent circling the wagons, but their crates of steel and medicine drew a " +
                            "grateful crowd all the same.",
                        ImagePrompt = "a dust-caked trade caravan reaching frontier town gates at dusk",
                        ImageCaption = "The caravan reaches the gates at last light.",
                        ResolvedImagePath = SamplePath("caravan.png"),
                    },
                    new NewspaperStory
                    {
                        Title = "Main Street Resurfacing to Snarl Traffic for a Quadrum",
                        Content =
                            "The settlement's primary artery will be torn up and relaid beginning next week, the " +
                            "works office confirmed. Haulers are urged to route through the mill road, and to " +
                            "expect delays at the market crossing until the work is done.",
                        // No ImagePrompt: this secondary runs text-only, on purpose.
                    },
                },
                Sidebars = new List<NewspaperSidebar>
                {
                    new NewspaperSidebar
                    {
                        Title = "Heatwave Advisory",
                        Content = "An unrelenting high-pressure system settles over the region. Keep livestock " +
                                  "watered and check on elderly neighbours.",
                    },
                    new NewspaperSidebar
                    {
                        Title = "Notices",
                        Content = "Lost: one prosthetic hand, left. Reward offered. Enquire at the tavern.",
                    },
                },
                // Exactly one ad, matching the live design. Fixed index here so the layout fixture is
                // deterministic (live issues rotate this weekly via HouseAds.ForCurrentWeek()).
                Ads = new List<NewspaperAd> { HouseAds.Get(0) },
                PerceivedWealthDelta = 0,
                PerceivedStrengthDelta = 0,
            };

            return issue;
        }
    }
}
