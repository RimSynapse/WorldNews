using System.Collections.Generic;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>A system+user message pair for the newspaper generation call.</summary>
    public struct NewspaperPrompt
    {
        public string system;
        public string user;
    }

    /// <summary>
    /// The PURE, Verse-free composer for the WorldNews newspaper prompt. Given the raw event strings it
    /// produces the exact system+user pair <see cref="SynapseNewspaperGenerator.Generate"/> sends. Zero
    /// <c>Verse</c> dependencies, so it runs outside RimWorld — which lets the game-free Prompt Lab
    /// (rimworld-claude-dev-tools) build the SAME prompt the game does without launching the game.
    ///
    /// Authored ONCE here: the generator calls <see cref="Compose"/>; the lab links this file. A prompt
    /// change here changes both the game and the lab — no reimplementation to drift.
    /// </summary>
    public static class NewspaperPromptComposer
    {
        public const string SystemPrompt = @"You are the editor of a frontier newspaper on a lawless rimworld.
You have collected raw events and must lay them out as one cohesive broadsheet issue — a masthead,
a lead story with an illustration, a few secondary stories, some short side items, and an
advertisement or two. Your writing should be dramatic, colorful, and fit a harsh sci-fi frontier.

You MUST respond in valid JSON matching this schema:
{
  ""PaperName"": ""The paper's name, e.g. The Evening Gazette"",
  ""Volume"": ""VOL. in roman numerals, e.g. VOL. CXXXIV"",
  ""IssueNumber"": ""NO. and a number, e.g. NO. 248"",
  ""Date"": ""The current in-game date"",
  ""Price"": ""A cover price, e.g. $1.50"",
  ""Strapline"": ""An optional one-line masthead strapline"",
  ""Headline"": ""An overarching dramatic headline for the whole issue"",
  ""PerceivedWealthDelta"": 0,
  ""PerceivedStrengthDelta"": 0,
  ""Stories"": [
    {
      ""Title"": ""Headline of the LEAD story (this first entry is the front-page lead)"",
      ""Byline"": ""An optional reporter byline, e.g. By David Chen, City Hall Correspondent"",
      ""Content"": ""Rich, multi-paragraph flavor text for the lead story — several full paragraphs, separated by blank lines"",
      ""ImagePrompt"": ""A vivid visual description for an illustration of this story, suitable for an image generator. Omit or leave empty for a text-only story."",
      ""ImageCaption"": ""An optional caption for the illustration""
    },
    {
      ""Title"": ""Headline of a secondary story"",
      ""Content"": ""Rich flavor text"",
      ""ImagePrompt"": """"
    }
  ],
  ""Sidebars"": [
    { ""Title"": ""A short side item, e.g. weather or a notice"", ""Content"": ""One or two sentences"" }
  ],
  ""SectionFooter"": ""An optional footer/section marker, e.g. SECTION A""
}

Guidance:
- The FIRST entry in Stories is the front-page lead and should be the most significant event; give it a byline and an ImagePrompt.
- Write the lead as several full paragraphs (aim for five to eight), enough to fill a broadsheet column — quotes, background, and consequences. Secondary stories can be a paragraph or two. A front page should read as FULL, not sparse.
- Only add an ImagePrompt to a story that genuinely warrants a picture. An empty or omitted ImagePrompt means the story runs text-only — that is fine.
- ImagePrompt describes the SCENE, not the newspaper; write it as you would prompt an image generator (subject, setting, mood, style).
- Note on Deltas: analyze the raw events. If the colony completed a legendary artwork, found gold, or grew significantly, output a positive PerceivedWealthDelta (e.g. 5000 or 15000). If they suffered massive damage or lost weapons, output a negative PerceivedStrengthDelta. These values represent how global factions will alter their perception of the colony based on this news. If the news is neutral, output 0.";

        /// <param name="events">The raw unpublished event summaries this issue is built from.</param>
        public static NewspaperPrompt Compose(IReadOnlyList<string> events)
        {
            string eventsText = string.Join("\n- ", events ?? new List<string>());
            string user = $@"Raw Events:
- {eventsText}

Write the newspaper issue based on these events.";
            return new NewspaperPrompt { system = SystemPrompt, user = user };
        }
    }
}
