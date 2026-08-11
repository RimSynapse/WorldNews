using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Newtonsoft.Json;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Newspaper
{
    public static class SynapseNewspaperGenerator
    {
        /// <summary>
        /// Generate an issue from a batch of events.
        /// </summary>
        /// <param name="onFinished">
        /// Invoked on the main thread when the request settles, however it settles. The caller uses
        /// it to clear its in-flight guard, so it must fire on failure and on a parse error too —
        /// otherwise one bad response stops the mod publishing for the rest of the game.
        /// </param>
        public static void Generate(List<string> unpublishedEvents, Action onFinished = null)
        {
            if (unpublishedEvents == null || unpublishedEvents.Count == 0)
            {
                onFinished?.Invoke();
                return;
            }

            string eventsText = string.Join("\n- ", unpublishedEvents);

            string systemPrompt = @"You are the editor of a frontier newspaper on a lawless rimworld.
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

            string userMessage = $@"Raw Events:
- {eventsText}

Write the newspaper issue based on these events.";

            SynapseClient.PromptAsync(
                RimSynapseWorldNewsMod.ModHandle,
                systemPrompt,
                userMessage,
                result =>
                {
                    try
                    {
                    if (result.success)
                    {
                        try
                        {
                            var issue = ParseIssue(result.content);
                            if (issue != null)
                            {
                                // Publish a letter whose "Read newspaper" choice opens the broadsheet.
                                // The source JSON travels on the letter so it survives a save/reload;
                                // store the extracted JSON (pre house-ad), since ParseIssue re-adds the
                                // house ad on read — storing the parsed issue would double it.
                                string issueJson = RimSynapse.Utils.JsonHelper.ExtractJson(result.content);
                                var letter = new UI.Letter_Newspaper
                                {
                                    def = LetterDefOf.PositiveEvent,
                                    Label = "Newspaper Published: " + issue.Headline,
                                    Text = "A new issue of the local newspaper has been published. "
                                         + "Read all about it.",
                                    ID = Find.UniqueIDsManager.GetNextLetterID(),
                                };
                                letter.SetIssue(issue, issueJson);
                                Find.LetterStack.ReceiveLetter(letter);

                                // First-run onboarding: the first published issue asks, once, whether to
                                // illustrate the paper via the external image service (WorldNews#24).
                                if (RimSynapseWorldNewsMod.Settings != null
                                    && !RimSynapseWorldNewsMod.Settings.imageConsentDecided)
                                {
                                    Find.WindowStack.Add(new RimSynapse.WorldNews.UI.Dialog_NewspaperImageConsent());
                                }

                                RimSynapse.SynapseLogger.Message($"[RimSynapse-WorldNews] Newspaper generated: {issue.Headline} | WealthDelta: {issue.PerceivedWealthDelta} | StrengthDelta: {issue.PerceivedStrengthDelta}");

                                // Broadcast the knowledge to all factions
                                if (issue.PerceivedWealthDelta != 0 || issue.PerceivedStrengthDelta != 0)
                                {
                                    RimSynapse.SynapseCoreContext.BroadcastGlobalKnowledge(issue.PerceivedWealthDelta, issue.PerceivedStrengthDelta);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("worldnews", $"[RimSynapse-WorldNews] Failed to parse newspaper: {ex.Message}");
                        }
                    }
                    else
                    {
                        RimSynapse.SynapseLogger.Warn("worldnews", $"[RimSynapse-WorldNews] Newspaper request failed: {result.content}");
                    }
                    }
                    finally
                    {
                        // Always, however this settled. The caller's in-flight guard hangs off this;
                        // skipping it on the failure path would stop the mod publishing for good.
                        onFinished?.Invoke();
                    }
                },
                new RimSynapse.ChatOptions { priority = 5, requestName = "Newspaper Generation" }
            );
        }

        /// <summary>
        /// Parse an LLM response (raw text or bare JSON) into a normalized <see cref="NewspaperIssue"/>,
        /// or null if no JSON could be extracted or it did not deserialize. The single parse path —
        /// the live generation callback, the debug action and the TestRunner case all go through here,
        /// so the schema they validate against and the schema the game consumes cannot drift apart.
        /// </summary>
        public static NewspaperIssue ParseIssue(string rawOrJson)
        {
            if (string.IsNullOrEmpty(rawOrJson)) return null;
            string json = RimSynapse.Utils.JsonHelper.ExtractJson(rawOrJson);
            if (json == null) return null;

            var issue = JsonConvert.DeserializeObject<NewspaperIssue>(json);
            if (issue == null) return null;

            ApplyMastheadDefaults(issue);
            return issue;
        }

        /// <summary>
        /// Fill masthead fields the model omitted so neither renderer draws a blank header. The LLM
        /// is asked for these, but a defaulted paper name and date are cheaper than a broadsheet with
        /// no title. Collections are guarded because a null list would throw in the renderers.
        /// </summary>
        private static void ApplyMastheadDefaults(NewspaperIssue issue)
        {
            if (issue == null) return;

            if (string.IsNullOrWhiteSpace(issue.PaperName)) issue.PaperName = DefaultPaperName;
            if (string.IsNullOrWhiteSpace(issue.Date))
            {
                // Same proven ticks-overloads the WorldComponent's DateStamp uses (GenLocalDate's
                // DayOfSeason/Season overloads need a Map/Thing/tile, not raw ticks). Only a rare
                // fallback — the model normally supplies Date.
                var tm = Verse.Find.TickManager;
                issue.Date = tm != null
                    ? $"Twelfth {RimWorld.GenLocalDate.Twelfth(tm.TicksGame)}, {RimWorld.GenLocalDate.Year(tm.TicksGame)}"
                    : "Unknown date";
            }

            if (issue.Stories == null) issue.Stories = new System.Collections.Generic.List<NewspaperStory>();
            if (issue.Sidebars == null) issue.Sidebars = new System.Collections.Generic.List<NewspaperSidebar>();

            // Exactly one ad, house-owned: the week's rotating gag, replacing anything else. The model
            // is not asked for ads, so this is the sole source.
            issue.Ads = new System.Collections.Generic.List<NewspaperAd> { HouseAds.ForCurrentWeek() };
        }

        /// <summary>Fallback masthead title when the model does not supply one.</summary>
        internal const string DefaultPaperName = "The Rimworld Gazette";
    }
}
