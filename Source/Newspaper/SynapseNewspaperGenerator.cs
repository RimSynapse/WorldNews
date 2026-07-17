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
        public static void Generate(List<string> unpublishedEvents)
        {
            if (unpublishedEvents == null || unpublishedEvents.Count == 0) return;

            string eventsText = string.Join("\n- ", unpublishedEvents);

            string systemPrompt = @"You are a frontier journalist on a lawless rimworld.
You have collected raw events and must synthesize them into a cohesive narrative newspaper issue.
Your writing should be dramatic, colorful, and fit the setting of a harsh sci-fi frontier.

You MUST respond in valid JSON matching this schema:
{
  ""Headline"": ""An overarching dramatic headline for the issue"",
  ""Date"": ""The current date"",
  ""PerceivedWealthDelta"": 0,
  ""PerceivedStrengthDelta"": 0,
  ""Stories"": [
    {
      ""Title"": ""Title of story 1"",
      ""Content"": ""Rich flavor text for story 1""
    }
  ]
}

Note on Deltas: Analyze the raw events. If the colony completed a legendary artwork, found gold, or grew significantly, output a positive PerceivedWealthDelta (e.g. 5000 or 15000). If they suffered massive damage or lost weapons, output a negative PerceivedStrengthDelta. These values represent how global factions will alter their perception of the colony based on this news! If the news is neutral, output 0.";

            string userMessage = $@"Raw Events:
- {eventsText}

Write the newspaper issue based on these events.";

            SynapseClient.PromptAsync(
                RimSynapseWorldNewsMod.ModHandle,
                systemPrompt,
                userMessage,
                result =>
                {
                    if (result.success)
                    {
                        try
                        {
                            string json = RimSynapse.Utils.JsonHelper.ExtractJson(result.content);
                            if (json != null)
                            {
                                var issue = JsonConvert.DeserializeObject<NewspaperIssue>(json);
                                if (issue != null)
                                {
                                    // Send a letter to the player
                                    Find.LetterStack.ReceiveLetter(
                                        "Newspaper Published: " + issue.Headline,
                                        "A new issue of the local newspaper has been published.\nClick to read.",
                                        LetterDefOf.PositiveEvent,
                                        null, // lookTargets
                                        null, // relatedFaction
                                        null, // quest
                                        null, // hyperlinkThingDefs
                                        issue.Headline // debugInfo
                                    );
                                    
                                    // Normally we would show the dialog immediately or via a custom letter action.
                                    // For now, let's just log it.
                                    RimSynapse.SynapseLogger.Message($"[RimSynapse-WorldNews] Newspaper generated: {issue.Headline} | WealthDelta: {issue.PerceivedWealthDelta} | StrengthDelta: {issue.PerceivedStrengthDelta}");

                                    // Broadcast the knowledge to all factions
                                    if (issue.PerceivedWealthDelta != 0 || issue.PerceivedStrengthDelta != 0)
                                    {
                                        RimSynapse.SynapseCoreContext.BroadcastGlobalKnowledge(issue.PerceivedWealthDelta, issue.PerceivedStrengthDelta);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("worldnews", $"[RimSynapse-WorldNews] Failed to parse newspaper: {ex.Message}");
                        }
                    }
                },
                new RimSynapse.ChatOptions { priority = 5, requestName = "Newspaper Generation" }
            );
        }
    }
}
