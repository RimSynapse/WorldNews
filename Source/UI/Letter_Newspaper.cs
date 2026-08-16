using System.Collections.Generic;
using RimWorld;
using Verse;
using RimSynapse.WorldNews.Models;
using RimSynapse.WorldNews.Newspaper;

namespace RimSynapse.WorldNews.UI
{
    /// <summary>
    /// The "Newspaper Published" letter. Clicking it offers to read the issue, which opens the
    /// broadsheet <see cref="Dialog_Newspaper"/> — the wire that makes the renderer reachable in
    /// normal play (previously the letter only logged).
    ///
    /// <para>The issue is persisted as its source JSON (<see cref="issueJson"/>) so a letter still in
    /// the stack after a save/reload can still be read; the live instance also caches the already
    /// parsed issue so the first read needs no re-parse. Follows the same shape as Psychology's
    /// <c>ChoiceLetter_OpenPsychology</c>.</para>
    /// </summary>
    public class Letter_Newspaper : ChoiceLetter
    {
        /// <summary>The issue's source JSON — persisted, so a reloaded letter can be re-parsed and read.</summary>
        public string issueJson;

        /// <summary>Live-only parsed issue; rebuilt from <see cref="issueJson"/> after a reload.</summary>
        private NewspaperIssue cachedIssue;

        /// <summary>Attach the issue: the parsed object for an immediate read, plus its JSON for persistence.</summary>
        public void SetIssue(NewspaperIssue issue, string json)
        {
            cachedIssue = issue;
            issueJson = json;
        }

        private NewspaperIssue ResolveIssue()
        {
            if (cachedIssue != null) return cachedIssue;
            if (!string.IsNullOrEmpty(issueJson))
            {
                cachedIssue = SynapseNewspaperGenerator.ParseIssue(issueJson);
            }
            return cachedIssue;
        }

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly)
                {
                    yield return Option_Close;
                    yield break;
                }

                var read = new DiaOption("Read newspaper")
                {
                    action = () =>
                    {
                        NewspaperIssue issue = ResolveIssue();
                        if (issue != null)
                        {
                            Find.WindowStack.Add(new Dialog_Newspaper(issue));
                        }
                        else
                        {
                            Messages.Message("This issue could not be reconstructed.",
                                MessageTypeDefOf.RejectInput, historical: false);
                        }
                    },
                    resolveTree = true,
                };
                yield return read;
                yield return Option_Close;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref issueJson, "issueJson");
        }
    }
}
