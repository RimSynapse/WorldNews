using System.Collections.Generic;
using Newtonsoft.Json;

namespace RimSynapse.WorldNews.Models
{
    /// <summary>
    /// The structured single source of truth for one newspaper issue (WorldNews#21). Both renderers
    /// — the in-game IMGUI broadsheet and the shareable HTML export (WorldNews#22) — read this and
    /// only this; neither re-derives content from world state.
    ///
    /// <para><b>Prose and assets are separate.</b> The LLM authors the text and, per illustrated
    /// story, an <see cref="NewspaperStory.ImagePrompt"/>. The image *asset* is resolved later by the
    /// Pollinations pipeline (WorldNews#23) into <see cref="NewspaperStory.ResolvedImagePath"/>. An
    /// issue with no resolved assets is fully valid and renders text-only, so a slow or disabled
    /// image path never blocks publication.</para>
    ///
    /// <para><b>Additive shape.</b> The original fields (<see cref="Headline"/>, <see cref="Date"/>,
    /// <see cref="Stories"/>, the two perception deltas) are retained so the existing generator and
    /// dialog keep working while #22 reworks rendering. New fields default to empty and are optional
    /// in the LLM contract.</para>
    /// </summary>
    public class NewspaperIssue
    {
        // --- Masthead ---------------------------------------------------------------------------
        /// <summary>The paper's name, e.g. "The Evening Gazette". LLM-chosen or defaulted in code.</summary>
        public string PaperName { get; set; }
        /// <summary>Volume, conventionally roman, e.g. "VOL. CXXXIV". Free text — the LLM may format it.</summary>
        public string Volume { get; set; }
        /// <summary>Issue number within the volume, e.g. "NO. 248".</summary>
        public string IssueNumber { get; set; }
        /// <summary>In-game date string for the issue.</summary>
        public string Date { get; set; }
        /// <summary>Cover price flavor, e.g. "$1.50".</summary>
        public string Price { get; set; }
        /// <summary>Optional strapline under the masthead.</summary>
        public string Strapline { get; set; }

        // --- Lead + secondary stories -----------------------------------------------------------
        /// <summary>
        /// The issue's overarching headline (retained). Distinct from the lead story's own title.
        /// </summary>
        public string Headline { get; set; }

        /// <summary>
        /// The stories, lead first: <c>Stories[0]</c> is the lead (large column + photo), the rest are
        /// secondaries. Retained field name so the current generator and dialog keep compiling.
        /// </summary>
        public List<NewspaperStory> Stories { get; set; } = new List<NewspaperStory>();

        // --- Fillers ----------------------------------------------------------------------------
        /// <summary>Short side items — weather, notices, "library event"-style fillers.</summary>
        public List<NewspaperSidebar> Sidebars { get; set; } = new List<NewspaperSidebar>();

        /// <summary>One or two LLM-authored flavor advertisements.</summary>
        public List<NewspaperAd> Ads { get; set; } = new List<NewspaperAd>();

        /// <summary>Optional footer / section marker text (e.g. "SECTION A").</summary>
        public string SectionFooter { get; set; }

        // --- Faction-perception broadcast (retained) --------------------------------------------
        public float PerceivedWealthDelta { get; set; }
        public float PerceivedStrengthDelta { get; set; }
    }

    public class NewspaperStory
    {
        /// <summary>The story's headline. Retained name (was the only text field besides Content).</summary>
        public string Title { get; set; }

        /// <summary>Optional byline, e.g. "By David Chen, City Hall Correspondent".</summary>
        public string Byline { get; set; }

        /// <summary>The story body. Retained.</summary>
        public string Content { get; set; }

        /// <summary>
        /// LLM-authored prompt for an illustration, or null/empty for a text-only story. This is the
        /// *prompt*, not the image — the Pollinations pipeline (WorldNews#23) resolves it to an asset.
        /// </summary>
        public string ImagePrompt { get; set; }

        /// <summary>Optional caption shown under the illustration.</summary>
        public string ImageCaption { get; set; }

        /// <summary>
        /// Runtime-only: the on-disk path of the resolved illustration once the image pipeline has
        /// fetched and cached it. Not part of the LLM JSON contract, so it is excluded from
        /// (de)serialization — a freshly deserialized issue has no assets yet, by design.
        /// </summary>
        [JsonIgnore]
        public string ResolvedImagePath { get; set; }

        /// <summary>True when an illustration has been requested (a prompt exists).</summary>
        [JsonIgnore]
        public bool WantsImage => !string.IsNullOrEmpty(ImagePrompt);

        /// <summary>True when the illustration has been resolved to a cached asset.</summary>
        [JsonIgnore]
        public bool HasImage => !string.IsNullOrEmpty(ResolvedImagePath);
    }

    public class NewspaperSidebar
    {
        public string Title { get; set; }
        public string Content { get; set; }
    }

    public class NewspaperAd
    {
        public string Advertiser { get; set; }
        public string Copy { get; set; }
    }
}
