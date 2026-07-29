using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimSynapse.WorldNews
{
    public class SynapseWorldNewsWorldComponent : WorldComponent
    {
        public List<string> unpublishedEvents = new List<string>();

        /// <summary>
        /// Label prefix on the letters this mod publishes. Recognised on the way back in so a
        /// newspaper announcement is never itself recorded as news — see RecordEventFromLetter.
        /// </summary>
        internal const string NewspaperLetterPrefix = "Newspaper Published: ";

        /// <summary>
        /// Hard ceiling on the queue. Every letter the game raises lands here, and a colony under
        /// pressure raises a great many; with no cap the list grows for as long as generation keeps
        /// failing, and every entry carries the full letter text.
        /// </summary>
        private const int MaxQueuedEvents = 40;

        /// <summary>
        /// Minimum ticks between issues (2500 = one in-game hour). The trigger was a queue-length
        /// threshold with no notion of time, so a burst of letters fired issue after issue as fast
        /// as the letters arrived.
        /// </summary>
        private const int MinTicksBetweenIssues = 2500;

        private int lastIssueTick = -99999;

        /// <summary>
        /// True while a generation request is in flight. Deliberately not saved: an issue still
        /// generating when the game was saved is never coming back, and persisting the flag would
        /// leave the mod permanently unable to publish.
        /// </summary>
        private bool generationInFlight;

        public SynapseWorldNewsWorldComponent(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref unpublishedEvents, "unpublishedEvents", LookMode.Value);
            Scribe_Values.Look(ref lastIssueTick, "lastIssueTick", -99999);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (unpublishedEvents == null) unpublishedEvents = new List<string>();
                generationInFlight = false;
            }
        }

        public void RecordEventFromLetter(Letter letter)
        {
            if (letter == null) return;

            string label = letter.Label;
            string text = "";
            if (letter is ChoiceLetter cl)
            {
                text = cl.Text;
            }

            // Avoid recording trivial letters or spam
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(text)) return;

            // Never record our own announcement. Publishing an issue sends a letter, the letter
            // patch records that letter as news, and it counts toward the threshold for the next
            // issue — the mod was feeding itself.
            if (label.StartsWith(NewspaperLetterPrefix)) return;

            RecordEvent($"{DateStamp()} {label}: {text}");
        }

        /// <summary>
        /// The single way an event enters the queue. Both entry points — the letter patch and the
        /// gossip patch — go through here, so the cap and the publish rules cannot be applied in
        /// one place and forgotten in the other. They were.
        /// </summary>
        internal void RecordEvent(string eventString)
        {
            if (string.IsNullOrEmpty(eventString)) return;

            unpublishedEvents.Add(eventString);

            if (unpublishedEvents.Count > MaxQueuedEvents)
            {
                unpublishedEvents.RemoveRange(0, unpublishedEvents.Count - MaxQueuedEvents);
            }

            TryPublish();
        }

        /// <summary>Publish only with enough news, nothing already generating, and enough time elapsed.</summary>
        internal void TryPublish()
        {
            if (unpublishedEvents.Count < 4) return;
            if (generationInFlight) return;

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastIssueTick < MinTicksBetweenIssues) return;

            TriggerNewspaperGeneration();
        }

        internal void TriggerNewspaperGeneration()
        {
            if (generationInFlight) return;
            if (unpublishedEvents.Count == 0) return;

            lastIssueTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            generationInFlight = true;

            // Hand the generator its own copy and clear immediately, so events arriving while the
            // request is in flight belong to the next issue instead of being published twice.
            var batch = new List<string>(unpublishedEvents);
            unpublishedEvents.Clear();

            RimSynapse.WorldNews.Newspaper.SynapseNewspaperGenerator.Generate(batch, OnGenerationFinished);
        }

        private void OnGenerationFinished()
        {
            generationInFlight = false;
        }

        private static string DateStamp()
        {
            var tm = Find.TickManager;
            if (tm == null) return "[unknown date]";
            return $"[{GenLocalDate.Twelfth(tm.TicksGame)}, {GenLocalDate.Year(tm.TicksGame)}]";
        }
    }
}
