using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimSynapse.WorldNews
{
    public class SynapseWorldNewsWorldComponent : WorldComponent
    {
        public List<string> unpublishedEvents = new List<string>();

        public SynapseWorldNewsWorldComponent(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref unpublishedEvents, "unpublishedEvents", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (unpublishedEvents == null) unpublishedEvents = new List<string>();
            }
        }

        public void RecordEventFromLetter(Letter letter)
        {
            string label = letter.Label;
            string text = "";
            if (letter is ChoiceLetter cl)
            {
                text = cl.Text;
            }
            
            // Avoid recording trivial letters or spam
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(text)) return;

            string eventString = $"[{GenLocalDate.Twelfth(Find.TickManager.TicksGame)}, {GenLocalDate.Year(Find.TickManager.TicksGame)}] {label}: {text}";
            unpublishedEvents.Add(eventString);

            // Trigger newspaper generation if threshold reached
            if (unpublishedEvents.Count >= 4)
            {
                TriggerNewspaperGeneration();
            }
        }

        internal void TriggerNewspaperGeneration()
        {
            // Call the newspaper generator and clear the queue (or clear it after successful generation)
            RimSynapse.WorldNews.Newspaper.SynapseNewspaperGenerator.Generate(unpublishedEvents);
            unpublishedEvents.Clear();
        }
    }
}
