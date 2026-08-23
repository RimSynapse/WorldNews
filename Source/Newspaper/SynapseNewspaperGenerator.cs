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

            // Prompt authored once in the pure NewspaperPromptComposer so the game-free Prompt Lab builds the
            // exact same system+user pair without launching RimWorld.
            var prompt = NewspaperPromptComposer.Compose(unpublishedEvents);

            SynapseClient.PromptAsync(
                RimSynapseWorldNewsMod.ModHandle,
                prompt.system,
                prompt.user,
                result =>
                {
                    if (!result.success)
                    {
                        RimSynapse.SynapseLogger.Warn("worldnews", $"[RimSynapse-WorldNews] Newspaper request failed: {result.content}");
                        onFinished?.Invoke();
                        return;
                    }

                    NewspaperIssue issue = null;
                    try { issue = ParseIssue(result.content); }
                    catch (Exception ex)
                    {
                        RimSynapse.SynapseLogger.Warn("worldnews", $"[RimSynapse-WorldNews] Failed to parse newspaper: {ex.Message}");
                    }

                    // Never publish an empty paper — no headline, or no story with any body text. This is
                    // how a stray/mocked/thin response stops producing blank issues on load and in tests.
                    if (!HasPublishableContent(issue))
                    {
                        RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] Newspaper skipped — no publishable content this cycle.");
                        onFinished?.Invoke();
                        return;
                    }

                    string issueJson = RimSynapse.Utils.JsonHelper.ExtractJson(result.content);

                    // Hold publication until illustrations arrive (WorldNews#23): when images are enabled
                    // and the issue wants pictures, fetch them first and publish once they land. A declined
                    // fetch (4xx) or a timeout publishes text-only rather than waiting forever.
                    if (NewspaperImagePipeline.WillFetch(issue))
                    {
                        NewspaperImagePipeline.ResolveAndAwait(issue, () =>
                        {
                            PublishIssue(issue, issueJson);
                            onFinished?.Invoke();
                        });
                    }
                    else
                    {
                        PublishIssue(issue, issueJson);
                        onFinished?.Invoke();
                    }
                },
                new RimSynapse.ChatOptions { priority = 5, requestName = "Newspaper Generation" }
            );
        }

        /// <summary>An issue is publishable only with a headline and at least one story that has body text.</summary>
        private static bool HasPublishableContent(NewspaperIssue issue)
        {
            if (issue == null || string.IsNullOrWhiteSpace(issue.Headline) || issue.Stories == null) return false;
            foreach (NewspaperStory s in issue.Stories)
            {
                if (s != null && !string.IsNullOrWhiteSpace(s.Content)) return true;
            }
            return false;
        }

        /// <summary>Send the "Newspaper Published" letter and broadcast its perception deltas.</summary>
        private static void PublishIssue(NewspaperIssue issue, string issueJson)
        {
            // The source JSON travels on the letter so it survives a save/reload; store the extracted
            // JSON (pre house-ad), since ParseIssue re-adds the house ad on read.
            var letter = new UI.Letter_Newspaper
            {
                def = LetterDefOf.PositiveEvent,
                Label = "Newspaper Published: " + issue.Headline,
                Text = "A new issue of the local newspaper has been published. Read all about it.",
                ID = Find.UniqueIDsManager.GetNextLetterID(),
            };
            letter.SetIssue(issue, issueJson);
            Find.LetterStack.ReceiveLetter(letter);

            // First-run onboarding: the first published issue asks, once, about external illustrations.
            if (RimSynapseWorldNewsMod.Settings != null && !RimSynapseWorldNewsMod.Settings.imageConsentDecided)
            {
                Find.WindowStack.Add(new RimSynapse.WorldNews.UI.Dialog_NewspaperImageConsent());
            }

            RimSynapse.SynapseLogger.Message($"[RimSynapse-WorldNews] Newspaper published: {issue.Headline} | WealthDelta: {issue.PerceivedWealthDelta} | StrengthDelta: {issue.PerceivedStrengthDelta}");

            if (issue.PerceivedWealthDelta != 0 || issue.PerceivedStrengthDelta != 0)
            {
                RimSynapse.SynapseCoreContext.BroadcastGlobalKnowledge(issue.PerceivedWealthDelta, issue.PerceivedStrengthDelta);
            }
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
