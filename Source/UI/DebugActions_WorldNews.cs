using System.Collections.Generic;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using RimWorld.Planet;
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

        // ---- World-map change feed (WorldNews#13) ------------------------------------------------
        //
        // Validation deliverables for the world-map feed. "Dump" and "run a pass" observe the live
        // mechanic; the three "force" actions inject a synthetic diff so each event kind can be walked
        // end to end — detector → feed dedup → flatten into the newspaper queue — without waiting for
        // the world map to actually change. All are headlessly runnable via run_debug_action.

        private static SynapseWorldNewsWorldComponent Component()
            => Find.World?.GetComponent<SynapseWorldNewsWorldComponent>();

        [DebugAction("RimSynapse", "WorldNews: dump world-map feed",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DumpWorldMapFeed()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }

            var sb = new StringBuilder();
            bool rt = RegionsAndTerritoriesBridge.Active;
            sb.AppendLine($"[RimSynapse-WorldNews] World-map feed: {comp.worldFeed.Count} event(s); R&T active={rt}; " +
                          $"queued news lines={comp.unpublishedEvents.Count}.");
            IReadOnlyList<WorldNewsEvent> events = comp.worldFeed.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                sb.AppendLine($"  [{i,2}] {e.kind} @tick {e.ticksGame} region=\"{e.regionName}\" : {e.ToNewsLine()}");
            }
            RimSynapse.SynapseLogger.Message(sb.ToString());
        }

        [DebugAction("RimSynapse", "WorldNews: run world-map detection pass now",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void RunWorldMapDetectionPass()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }

            int accepted = comp.SampleWorldMap();
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse-WorldNews] Detection pass complete: {accepted} new event(s) accepted; " +
                $"feed now holds {comp.worldFeed.Count}. (First pass of a session only baselines and emits 0.)");
        }

        [DebugAction("RimSynapse", "WorldNews: force synthetic border-change event",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceBorderChangeEvent()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }

            var before = new List<ProvinceSnapshot> { new ProvinceSnapshot {
                provinceId = -9001, regionName = "Redwater Basin",
                primaryOwnerId = "SynthFactionA", primaryOwnerName = "the Kanou Tribe" } };
            var after = new List<ProvinceSnapshot> { new ProvinceSnapshot {
                provinceId = -9001, regionName = "Redwater Basin",
                primaryOwnerId = "SynthFactionB", primaryOwnerName = "the New Arbor Confederacy" } };

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            var events = WorldMapChangeDetector.DetectBorderChanges(before, after, now);
            ReportForced("border-change", comp, events);
        }

        [DebugAction("RimSynapse", "WorldNews: force synthetic border-tension event",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceBorderTensionEvent()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }

            var snap = new List<ProvinceSnapshot> { new ProvinceSnapshot {
                provinceId = -9002, regionName = "Ashfall Marches", contested = true,
                contenderIds = { }, contenderNames = { } } };
            snap[0].contenderIds.AddRange(new[] { "SynthFactionX", "SynthFactionY" });
            snap[0].contenderNames.AddRange(new[] { "the Iron Compact", "the Verdant League" });

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            // Force hostility so the synthetic pair reads as tense regardless of real relations.
            var events = WorldMapChangeDetector.DetectBorderTension(snap, now, (a, b) => true);
            ReportForced("border-tension", comp, events);
        }

        [DebugAction("RimSynapse", "WorldNews: force synthetic new-settlement event",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceSettlementFoundedEvent()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }

            var curr = new List<SettlementSnapshot> { new SettlementSnapshot {
                key = "SynthSettlement-" + (Find.TickManager != null ? Find.TickManager.TicksGame : 0),
                provinceId = -9003, regionName = "Glasswind Reach",
                factionId = "SynthFounder", factionName = "the Pilgrims of Ossa" } };

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            var events = WorldMapChangeDetector.DetectSettlementsFounded(new HashSet<string>(), curr, now);
            ReportForced("new-settlement", comp, events);
        }

        [DebugAction("RimSynapse", "WorldNews: force synthetic quest-outcome event",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceQuestOutcomeEvent()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }

            // A contested region so the news line names both the holder and a rival, and a failed
            // outcome so the "came to nothing" branch is exercised.
            var snap = new ProvinceSnapshot
            {
                provinceId = -9004, regionName = "Thornmarch", contested = true,
                primaryOwnerId = "SynthHolder", primaryOwnerName = "the Marsh Wardens",
            };
            snap.contenderIds.AddRange(new[] { "SynthHolder", "SynthRival" });
            snap.contenderNames.AddRange(new[] { "the Marsh Wardens", "the Dune Reavers" });

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            var e = RimSynapse.WorldNews.Quests.QuestNewsReactor.BuildQuestEvent(snap, "Break the Siege of Thornmarch", RimWorld.QuestEndOutcome.Fail, now);
            ReportForced("quest-outcome", comp, e != null ? new List<WorldNewsEvent> { e } : new List<WorldNewsEvent>());
        }

        // ---- Settlement affairs + short-term relations -------------------------------------------

        /// <summary>Distinct non-player factions that actually own a settlement — the real population
        /// affairs happen to. Sourced from world objects so the debug path uses the same faction set the
        /// live beat does.</summary>
        private static List<Faction> SettlementOwningFactions()
        {
            var result = new List<Faction>();
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return result;
            foreach (var s in settlements)
            {
                Faction f = s?.Faction;
                if (f != null && !f.IsPlayer && !result.Contains(f)) result.Add(f);
            }
            return result;
        }

        [DebugAction("RimSynapse", "WorldNews: force settlement affair (real factions, conflict)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void ForceSettlementAffair()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }

            var owners = SettlementOwningFactions();
            var allNpc = Find.FactionManager?.AllFactionsListForReading?.FindAll(f => f != null && !f.IsPlayer);
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse-WorldNews] Faction census: {owners.Count} settlement-owning NPC faction(s); " +
                $"{allNpc?.Count ?? 0} non-player faction(s) total.");

            Faction a = owners.Count > 0 ? owners[0] : null;
            Faction b = owners.Count > 1 ? owners[1] : null;
            if (a == null || b == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] Need two settlement-owning NPC factions."); return; }

            var ctx = new AffairContext
            {
                settlementFactionId = a.GetUniqueLoadID(), settlementFactionName = a.Name,
                rivalFactionId = b.GetUniqueLoadID(), rivalFactionName = b.Name,
                regionName = "the Frontier",
                nowTicks = Find.TickManager != null ? Find.TickManager.TicksGame : 0,
            };

            int before = a.GoodwillWith(b);
            // Forced rolls: conflict (0 < 0.55), first conflict outcome (soured), magnitude 10 → delta -10.
            AffairResult result = SettlementAffairGenerator.Adjudicate(ctx, 0f, 0f, 10);
            comp.DebugApplyAffairResult(result);
            int after = a.GoodwillWith(b);

            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse-WorldNews] Forced settlement affair: {a.Name} vs {b.Name}. " +
                $"Requested delta {result.relationDelta}, goodwill {before}→{after} (moved {after - before}). " +
                $"Ledger now {comp.relationLedger.Count} pair(s).\n    news: {result.evt.ToNewsLine()}");
        }

        [DebugAction("RimSynapse", "WorldNews: dump short-term relation ledger",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DumpRelationLedger()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"[RimSynapse-WorldNews] Short-term relation ledger: {comp.relationLedger.Count} active nudge(s) " +
                          $"(cap ±{RimSynapse.WorldNews.Relations.ShortTermRelationLedger.MaxOffset}, " +
                          $"decays {RimSynapse.WorldNews.Relations.ShortTermRelationLedger.DecayStep}/day).");
            var nudges = comp.relationLedger.Nudges;
            for (int i = 0; i < nudges.Count; i++)
            {
                var n = nudges[i];
                sb.AppendLine($"  [{i,2}] {FactionName(n.aId)} ↔ {FactionName(n.bId)} : offset {n.offset:+0;-0;0}");
            }
            RimSynapse.SynapseLogger.Message(sb.ToString());
        }

        [DebugAction("RimSynapse", "WorldNews: decay relations one day",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap | AllowedGameStates.PlayingOnWorld)]
        private static void DecayRelationsOneDay()
        {
            var comp = Component();
            if (comp == null) { RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] No world component."); return; }
            int remaining = comp.DebugDecayRelationsOneDay();
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse-WorldNews] Decayed short-term relations one day; {remaining} pair(s) still unwinding.");
        }

        private static string FactionName(string uniqueLoadId)
        {
            var all = Find.FactionManager?.AllFactionsListForReading;
            if (all != null)
                foreach (var f in all)
                    if (f != null && f.GetUniqueLoadID() == uniqueLoadId) return f.Name;
            return uniqueLoadId;
        }

        private static void ReportForced(string label, SynapseWorldNewsWorldComponent comp, List<WorldNewsEvent> events)
        {
            int recorded = 0;
            var sb = new StringBuilder();
            foreach (var e in events)
            {
                bool ok = comp.RecordWorldEvent(e);
                if (ok) recorded++;
                sb.AppendLine($"    {(ok ? "recorded" : "suppressed (dedup)")}: {e.ToNewsLine()}");
            }
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse-WorldNews] Forced {label}: detector produced {events.Count}, {recorded} recorded " +
                $"(feed now {comp.worldFeed.Count}, news queue {comp.unpublishedEvents.Count}).\n{sb}");
        }
    }
}
