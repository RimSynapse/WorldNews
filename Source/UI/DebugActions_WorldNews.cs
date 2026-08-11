using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using RimSynapse.WorldNews.Integration;
using RimSynapse.WorldNews.Models;
using RimSynapse.WorldNews.Newspaper;

namespace RimSynapse.WorldNews.UI
{
    /// <summary>
    /// Debug actions for WorldNews, under the shared "RimSynapse" dev-menu category.
    /// </summary>
    public static class DebugActions_WorldNews
    {
        /// <summary>
        /// A canned expanded-issue response covering every part of the model — masthead, a lead story
        /// with an image prompt + caption, a text-only secondary, a sidebar and an ad — so the debug
        /// action below can exercise the parse path (WorldNews#21) without a live LLM. Wrapped in prose
        /// to also prove JSON extraction, not just deserialization.
        /// </summary>
        private const string SampleIssueJson = @"Here is your issue:
{
  ""PaperName"": ""The Evening Gazette"",
  ""Volume"": ""VOL. CXXXIV"",
  ""IssueNumber"": ""NO. 248"",
  ""Date"": ""5th of Jugust, 5502"",
  ""Price"": ""$1.50"",
  ""Strapline"": ""All the news the frontier can stomach"",
  ""Headline"": ""COUNCIL PASSES STRICT NEW PARK RULES IN TENSE VOTE"",
  ""PerceivedWealthDelta"": 0,
  ""PerceivedStrengthDelta"": 0,
  ""Stories"": [
    { ""Title"": ""Council Passes Strict New Park Rules"", ""Byline"": ""By David Chen, City Hall Correspondent"", ""Content"": ""After hours of impassioned testimony, the council voted..."", ""ImagePrompt"": ""a packed frontier town-hall meeting, tense faces, dramatic lighting"", ""ImageCaption"": ""Opponents voice concerns at last night's hearing."" },
    { ""Title"": ""Main Street Resurfacing to Cause Delays"", ""Content"": ""The town's primary artery will undergo reconstruction..."", ""ImagePrompt"": """" }
  ],
  ""Sidebars"": [ { ""Title"": ""Heatwave Advisory"", ""Content"": ""An unrelenting high-pressure system settles over the region."" } ],
  ""Ads"": [ { ""Advertiser"": ""ACME Hardware"", ""Copy"": ""Everything the homesteader needs. Now with fresh stock of duct tape."" } ],
  ""SectionFooter"": ""SECTION A""
}";

        /// <summary>
        /// Exercises the expanded <see cref="NewspaperIssue"/> model and the single parse path
        /// (<see cref="SynapseNewspaperGenerator.ParseIssue"/>): deserializes the canned response,
        /// then logs the assembled structure. Debug-validation deliverable for WorldNews#21 — proves
        /// the model holds masthead + lead + secondaries + sidebars + ads, that image prompts are
        /// captured while unresolved assets stay absent, and that JSON survives extraction from prose.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: dump sample issue model",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DumpSampleIssueModel()
        {
            NewspaperIssue issue = SynapseNewspaperGenerator.ParseIssue(SampleIssueJson);
            if (issue == null)
            {
                RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] Sample issue FAILED to parse.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[RimSynapse-WorldNews] Parsed issue: {issue.PaperName} — {issue.Volume} {issue.IssueNumber} — {issue.Date} — {issue.Price}");
            sb.AppendLine($"  Headline: {issue.Headline}");
            sb.AppendLine($"  Stories: {issue.Stories.Count} (lead: \"{issue.Stories.FirstOrDefault()?.Title}\")");
            foreach (var s in issue.Stories)
            {
                sb.AppendLine($"    - {s.Title} | image wanted: {s.WantsImage} resolved: {s.HasImage}");
            }
            sb.AppendLine($"  Sidebars: {issue.Sidebars.Count}, Ads: {issue.Ads.Count}, Footer: {issue.SectionFooter}");
            RimSynapse.SynapseLogger.Message(sb.ToString());
        }
        /// <summary>
        /// Exercises <see cref="RegionsAndTerritoriesBridge"/> end to end: reports whether R&amp;T is
        /// detected and, when it is, performs a real read of R&amp;T's world state through the seam.
        /// This is the debug-validation deliverable for the optional-dependency foundation — running
        /// it proves both branches of the seam (present / absent) behave, and that the direct R&amp;T
        /// assembly reference actually binds at runtime rather than silently dropping WorldNews.
        /// </summary>
        /// <summary>
        /// Open the broadsheet renderer (<see cref="Dialog_Newspaper"/>) on the fully-populated sample
        /// issue (<see cref="NewspaperFixtures"/>). This is the layout-iteration hook for WorldNews#22:
        /// it exercises the whole render path — masthead, banner, lead-with-illustration, secondaries,
        /// side items and ads, plus the disk→texture image load — with no live LLM and no image
        /// pipeline. Running it headlessly still constructs and registers the window; the visual check
        /// is a screenshot of the running game.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: open sample newspaper (layout)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OpenSampleNewspaper()
        {
            NewspaperIssue issue = NewspaperFixtures.SampleIssue();
            Verse.Find.WindowStack.Add(new Dialog_Newspaper(issue));
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse-WorldNews] Opened sample newspaper: {issue.PaperName} — " +
                $"{issue.Stories.Count} stories, {issue.Sidebars.Count} sidebars, {issue.Ads.Count} ads.");
        }

        /// <summary>
        /// As above, but with the right rail pre-scrolled to its foot, so a screenshot catches the
        /// side-item ("IN BRIEF") panel and the advertisements that otherwise sit below the fold.
        /// Verification-only companion to the layout action.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: open sample newspaper (scrolled to foot)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OpenSampleNewspaperScrolledToFoot()
        {
            Verse.Find.WindowStack.Add(new Dialog_Newspaper(NewspaperFixtures.SampleIssue())
            {
                debugStartScrolledToBottom = true,
            });
        }

        /// <summary>
        /// Dump the ad rotation: the pool size, this week's index and pick, and the full 20-ad pool.
        /// Debug-validation for the "one ad, rotates weekly" mechanic — proves the pool is populated
        /// and that the weekly selector is deterministic for the current week.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: dump house ads (weekly rotation)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DumpHouseAds()
        {
            var sb = new StringBuilder();
            var now = HouseAds.ForCurrentWeek();
            sb.AppendLine($"[RimSynapse-WorldNews] House ads: {HouseAds.Count} in rotation. " +
                          $"Week {HouseAds.CurrentWeek()} → \"{now.Advertiser}\": {now.Copy}");
            for (int i = 0; i < HouseAds.Count; i++)
            {
                var ad = HouseAds.Get(i);
                sb.AppendLine($"  [{i,2}] {ad.Advertiser} — {ad.Copy}");
            }
            RimSynapse.SynapseLogger.Message(sb.ToString());
        }

        // ---- Breaking-news banner (WorldNews#19) -------------------------------------------------

        [DebugAction("RimSynapse", "WorldNews: toggle banner comms override (debug)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ToggleBannerCommsOverride()
        {
            NewspaperBanner.DebugForceComms = !NewspaperBanner.DebugForceComms;
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse-WorldNews] Banner comms override = {NewspaperBanner.DebugForceComms}");
        }

        // ---- Illustrations (WorldNews#23 / #24) --------------------------------------------------

        [DebugAction("RimSynapse", "WorldNews: show image-consent window",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ShowImageConsentWindow()
        {
            Verse.Find.WindowStack.Add(new Dialog_NewspaperImageConsent());
        }

        [DebugAction("RimSynapse", "WorldNews: newspaper image report",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void NewspaperImageReport()
        {
            var s = RimSynapseWorldNewsMod.Settings;
            string dir = NewspaperImagePipeline.CacheDir;
            int cached = System.IO.Directory.Exists(dir)
                ? System.IO.Directory.GetFiles(dir, "*.jpg").Length : 0;
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse-WorldNews] Images: enabled={s?.enableNewspaperImages}, " +
                $"consentDecided={s?.imageConsentDecided}, cached={cached} file(s) in {dir}");
        }

        /// <summary>
        /// Force-fetch one illustration from the live service, bypassing the consent gate — a network
        /// + pipeline smoke test. Check the log for "image ready" (success) or "fetch failed".
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: TEST fetch a newspaper image now",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void TestFetchNewspaperImage()
        {
            NewspaperImagePipeline.DebugForceFetch(
                "a dust-caked trade caravan reaching frontier town gates at dusk");
            RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] Test image fetch started; watch the log.");
        }

        /// <summary>
        /// Open the sample issue on the AI-image path: clears the bundled placeholder paths (keeping the
        /// prompts) and enables illustrations for this session, so the dialog resolves images through
        /// the pipeline. Pictures appear once fetched, or it stays text-only if the fetch fails.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: open sample newspaper (AI images)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OpenSampleNewspaperAiImages()
        {
            var s = RimSynapseWorldNewsMod.Settings;
            if (s != null) { s.enableNewspaperImages = true; s.imageConsentDecided = true; } // session-only

            NewspaperIssue issue = NewspaperFixtures.SampleIssue();
            foreach (NewspaperStory story in issue.Stories)
            {
                story.ResolvedImagePath = null; // drop bundled placeholders → force the prompt path
            }
            Verse.Find.WindowStack.Add(new Dialog_Newspaper(issue));
        }

        /// <summary>Build a "Newspaper Published" letter for the sample issue, matching the live path.</summary>
        private static Letter_Newspaper MakeSampleLetter()
        {
            NewspaperIssue issue = NewspaperFixtures.SampleIssue();
            var letter = new Letter_Newspaper
            {
                def = LetterDefOf.PositiveEvent,
                Label = "Newspaper Published: " + issue.Headline,
                Text = "A new issue of the local newspaper has been published. Read all about it.",
                ID = Find.UniqueIDsManager.GetNextLetterID(),
            };
            letter.SetIssue(issue, null); // cached issue only — no reparse, no double house ad
            return letter;
        }

        /// <summary>
        /// Publish the sample "Newspaper Published" letter into the stack — the live-play entry point.
        /// Clicking it (or the "open letter" debug action) offers "Read newspaper" → the broadsheet.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: publish sample newspaper letter",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void PublishSampleNewspaperLetter()
        {
            Find.LetterStack.ReceiveLetter(MakeSampleLetter());
            RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] Sample newspaper letter published.");
        }

        /// <summary>
        /// Open the sample newspaper letter's choice dialog directly — the same code path a click runs
        /// (<see cref="Verse.ChoiceLetter.OpenLetter"/> → Choices). Confirms the "Read newspaper" wire
        /// without needing a mouse click on the letter.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: open sample newspaper letter (choices)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OpenSampleNewspaperLetter()
        {
            // Receive first: a letter's "Read newspaper" option only shows while it is live in the
            // stack (ChoiceLetter.ArchivedOnly gates it), which is exactly the real-play state.
            Letter_Newspaper letter = MakeSampleLetter();
            Find.LetterStack.ReceiveLetter(letter);
            letter.OpenLetter();
        }

        /// <summary>
        /// Headless export of the sample issue to self-contained HTML (WorldNews#22), logging the path.
        /// The in-game path is the toolbar's Export button; both call <see cref="NewspaperHtmlExporter"/>,
        /// so this validates the exact code the button runs. Debug-validation deliverable for the export.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: export sample newspaper HTML",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ExportSampleNewspaperHtml()
        {
            string path = NewspaperHtmlExporter.Export(NewspaperFixtures.SampleIssue(), out string error);
            RimSynapse.SynapseLogger.Message(path != null
                ? $"[RimSynapse-WorldNews] Sample newspaper exported to: {path}"
                : $"[RimSynapse-WorldNews] Sample newspaper export FAILED: {error}");
        }

        /// <summary>
        /// Open the sample newspaper already in its "exported" toolbar state — the selectable path
        /// field plus the Copy path button populated — so that UI can be seen without clicking Export.
        /// </summary>
        [DebugAction("RimSynapse", "WorldNews: open sample newspaper (exported state)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void OpenSampleNewspaperExportedState()
        {
            NewspaperIssue issue = NewspaperFixtures.SampleIssue();
            string path = NewspaperHtmlExporter.Export(issue, out string error) ?? $"(export failed: {error})";
            var dlg = new Dialog_Newspaper(issue);
            dlg.DebugSetExportedPath(path);
            Verse.Find.WindowStack.Add(dlg);
        }

        [DebugAction("RimSynapse", "WorldNews: R&T seam status",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ReportRegionsBridgeStatus()
        {
            if (RegionsAndTerritoriesBridge.TryDescribeWorldState(out string summary))
            {
                RimSynapse.SynapseLogger.Message($"[RimSynapse-WorldNews] R&T seam ACTIVE — {summary}");
            }
            else
            {
                RimSynapse.SynapseLogger.Message(
                    "[RimSynapse-WorldNews] R&T seam INACTIVE — Regions and Territories is not loaded; " +
                    "world-map coverage stands down and only colony-local news is reported.");
            }
        }
    }
}
