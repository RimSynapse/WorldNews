using System.Collections.Generic;

namespace RimSynapse.WorldNews.Models
{
    public class NewspaperIssue
    {
        public string Headline { get; set; }
        public string Date { get; set; }
        public float PerceivedWealthDelta { get; set; }
        public float PerceivedStrengthDelta { get; set; }
        public List<NewspaperStory> Stories { get; set; } = new List<NewspaperStory>();
    }

    public class NewspaperStory
    {
        public string Title { get; set; }
        public string Content { get; set; }
    }
}
