using RimWorld;
using Verse;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>
    /// The newspaper's single advertisement — one quirky, RimWorld-flavored gag ad per issue, the
    /// same one all week and rotating to the next each in-game week. Every issue published in a given
    /// week carries the same ad; the pick advances through the pool one entry per week.
    ///
    /// <para>Ads are entirely house-owned: the model no longer asks the LLM for ads, and both the live
    /// path and the fixture set exactly one ad from here. Content lives at model-assembly time (not in
    /// a renderer), so the IMGUI broadsheet and the HTML export show the same ad.</para>
    /// </summary>
    public static class HouseAds
    {
        private static readonly NewspaperAd[] Pool =
        {
            new NewspaperAd { Advertiser = "Randy's Discount Cryptosleep", ImageFile = "cryptosleep.png",
                Copy = "You'll wake up eventually. Probably. No refunds on the caskets." },
            new NewspaperAd { Advertiser = "Muffalo Wool Emporium", ImageFile = "muffalo.png",
                Copy = "Warm, hardy, hypoallergenic — unlike its former owner. Fifteen silver the bolt." },
            new NewspaperAd { Advertiser = "Glitterworld Medicine (Definitely Not Expired)", ImageFile = "medicine.png",
                Copy = "Cures what ails ye, and three things that don't. Ask the man in black." },
            new NewspaperAd { Advertiser = "Boomalope Dairy Co.", ImageFile = "boomalope.png",
                Copy = "Milk 'em gentle. We are not liable for the crater." },
            new NewspaperAd { Advertiser = "Joywire & Sons, Neuro-Fitters", ImageFile = "joywire.png",
                Copy = "Why feel anything at all? Installation while-u-wait." },
            new NewspaperAd { Advertiser = "Thrumbo Insurance Group", ImageFile = "thrumbo.png",
                Copy = "One horn. One premium. No survivors to file a claim." },
            new NewspaperAd { Advertiser = "Aunt Cassandra's Preserves",
                Copy = "Put up for the long dark. Tight schedule, tighter lids." },
            new NewspaperAd { Advertiser = "Rex's Peg Legs & Bionics",
                Copy = "Lost a limb to a scyther? Walk it off. Financing available." },
            new NewspaperAd { Advertiser = "The Toxic Fallout Umbrella Co.",
                Copy = "Now in seventeen shades of caution yellow." },
            new NewspaperAd { Advertiser = "Nutrient Paste — Family Recipe",
                Copy = "It's food. It's grey. It's ready. Stop asking what's in it." },
            new NewspaperAd { Advertiser = "The Cannibal's Cookbook, 3rd Ed.", ImageFile = "skull.png",
                Copy = "Now with a chapter on presentation. Guests optional." },
            new NewspaperAd { Advertiser = "Yayo? We Barely Know Yo",
                Copy = "Recreational chemistry for the discerning colonist. Discretion assured." },
            new NewspaperAd { Advertiser = "Deep Drill Timeshares", ImageFile = "drill.png",
                Copy = "Strike ore, or strike bugs. Either way, you'll strike something." },
            new NewspaperAd { Advertiser = "Empire Surplus Outlet",
                Copy = "Genuine cataphract plate, only lightly haunted. Titles sold separately." },
            new NewspaperAd { Advertiser = "Second-Hand Prosthetics ('Previously Loved')",
                Copy = "One careful owner. Mostly." },
            new NewspaperAd { Advertiser = "Pemmican Pete's Trail Rations",
                Copy = "Lasts longer than your colony will." },
            new NewspaperAd { Advertiser = "Mad Mick's Animal Taming",
                Copy = "If it has teeth, we'll make it a friend. Waivers at the door." },
            new NewspaperAd { Advertiser = "Cryptosleep Singles",
                Copy = "Meet someone special. They'll still be here in a decade. Guaranteed." },
            new NewspaperAd { Advertiser = "Sun Lamp Tanning Salon", ImageFile = "sunlamp.png",
                Copy = "That healthy hydroponic glow, without the potatoes." },
            new NewspaperAd { Advertiser = "Steel & Silver Pawnbrokers", ImageFile = "pawnbroker.png",
                Copy = "We buy anything not currently on fire." },
        };

        /// <summary>Number of ads in the rotation.</summary>
        public static int Count => Pool.Length;

        /// <summary>A fresh copy of the pool entry at <paramref name="index"/> (wrapped).</summary>
        public static NewspaperAd Get(int index)
        {
            int i = ((index % Pool.Length) + Pool.Length) % Pool.Length;
            NewspaperAd t = Pool[i];
            return new NewspaperAd { Advertiser = t.Advertiser, Copy = t.Copy, ImageFile = t.ImageFile };
        }

        /// <summary>Absolute path to an ad's bundled brand mark, or null if it has none / the mod root
        /// is unknown. Resolved off the loaded WorldNews folder like the sample illustrations.</summary>
        public static string BrandImagePath(NewspaperAd ad)
        {
            if (ad == null || string.IsNullOrEmpty(ad.ImageFile)) return null;
            string root = RimSynapseWorldNewsMod.ContentRootDir;
            if (string.IsNullOrEmpty(root)) return null;
            return System.IO.Path.Combine(root, "Textures", "WorldNews", "Ads", ad.ImageFile);
        }

        /// <summary>The in-game week number since game start (7 days per week).</summary>
        public static int CurrentWeek()
        {
            long ticks = Find.TickManager?.TicksGame ?? 0L;
            return (int)(ticks / (GenDate.TicksPerDay * 7L));
        }

        /// <summary>This week's ad — stable within the week, advancing by one each week.</summary>
        public static NewspaperAd ForCurrentWeek() => Get(CurrentWeek());
    }
}
