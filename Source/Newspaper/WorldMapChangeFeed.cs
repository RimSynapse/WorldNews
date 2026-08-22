using System.Collections.Generic;
using Verse;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>
    /// The bounded rolling buffer of world-map facts the newspaper draws on (WorldNews#13).
    ///
    /// <para>Three rules, each with a reason the issue calls out explicitly:</para>
    /// <list type="bullet">
    ///   <item><description><b>Bounded.</b> Oldest entries are evicted past a hard cap so the buffer
    ///   never grows without limit — a colony that runs for years must not accumulate an unbounded
    ///   list of every border it ever saw move. (Unlike <c>Log.Messages</c>, which the issue names as
    ///   the pattern <i>not</i> to build on: things silently vanish from it, so an article written
    ///   from it could reference state already gone. Here eviction is explicit and only touches
    ///   entries old enough that no pending issue still needs them.)</description></item>
    ///   <item><description><b>Deduplicated.</b> An event whose <see cref="WorldNewsEvent.DedupKey"/>
    ///   was recorded within <see cref="DedupCooldownTicks"/> is dropped. A border oscillating across
    ///   the ownership threshold would otherwise produce an article a day.</description></item>
    ///   <item><description><b>Save-safe.</b> The whole buffer and the dedup ledger scribe, so a
    ///   reload continues rather than re-emitting.</description></item>
    /// </list>
    ///
    /// <para>This class is pure state + rules — it holds no R&amp;T type and does no world query, so
    /// its behaviour is unit-testable on a simulated clock (WorldNews#13 Tier-1).</para>
    /// </summary>
    public class WorldMapChangeFeed : IExposable
    {
        /// <summary>Hard ceiling on retained events. Chosen well above a plausible day's worth so an
        /// issue always has its day's material, but bounded so a long game cannot grow it forever.</summary>
        public const int MaxEvents = 60;

        /// <summary>Default dedup window: one in-game day (60000 ticks). The same border swing inside
        /// a day collapses to one event; the same change genuinely recurring a week later is news again.</summary>
        public const int DefaultDedupCooldownTicks = 60000;

        public int DedupCooldownTicks = DefaultDedupCooldownTicks;

        private List<WorldNewsEvent> events = new List<WorldNewsEvent>();

        // Dedup ledger: last tick each key was accepted. Kept separate from the events list so a key
        // still suppresses re-fires after its event has been evicted from the (capacity-bounded)
        // buffer — eviction must not silently re-open the floodgates on an oscillating border.
        private Dictionary<string, int> lastAcceptedTick = new Dictionary<string, int>();

        public int Count => events.Count;

        /// <summary>Read-only view for rendering/diagnostics. Never hand out the backing list.</summary>
        public IReadOnlyList<WorldNewsEvent> Events => events;

        /// <summary>
        /// Offer an event to the feed. Returns true if it was accepted (novel within the cooldown),
        /// false if suppressed as a duplicate. On acceptance the oldest events past <see cref="MaxEvents"/>
        /// are evicted. <paramref name="nowTicks"/> is passed rather than read from the clock so the
        /// rules are testable without a running game.
        /// </summary>
        public bool TryRecord(WorldNewsEvent e, int nowTicks)
        {
            if (e == null) return false;

            string key = e.DedupKey;
            if (lastAcceptedTick.TryGetValue(key, out int last) && nowTicks - last < DedupCooldownTicks)
            {
                return false; // within cooldown → same change flapping, not a new story
            }

            e.ticksGame = nowTicks;
            events.Add(e);
            lastAcceptedTick[key] = nowTicks;

            if (events.Count > MaxEvents)
            {
                int overflow = events.Count - MaxEvents;
                events.RemoveRange(0, overflow);
            }

            PruneLedger(nowTicks);
            return true;
        }

        /// <summary>
        /// Drop dedup-ledger entries older than one cooldown — they can no longer suppress anything, so
        /// keeping them would leak memory over a long game exactly the way an unbounded event list would.
        /// The events buffer itself is bounded by count; this bounds the ledger by age.
        /// </summary>
        private void PruneLedger(int nowTicks)
        {
            if (lastAcceptedTick.Count == 0) return;
            List<string> stale = null;
            foreach (var kv in lastAcceptedTick)
            {
                if (nowTicks - kv.Value >= DedupCooldownTicks)
                {
                    (stale ?? (stale = new List<string>())).Add(kv.Key);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++) lastAcceptedTick.Remove(stale[i]);
            }
        }

        /// <summary>Snapshot the current events as news lines for the generator's batch.</summary>
        public List<string> ToNewsLines()
        {
            var lines = new List<string>(events.Count);
            for (int i = 0; i < events.Count; i++) lines.Add(events[i].ToNewsLine());
            return lines;
        }

        /// <summary>Clear the events (e.g. once an issue has consumed them). The dedup ledger is kept,
        /// so a consumed change still cannot immediately re-fire.</summary>
        public void ClearEvents() => events.Clear();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref events, "events", LookMode.Deep);
            Scribe_Collections.Look(ref lastAcceptedTick, "lastAcceptedTick", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref DedupCooldownTicks, "dedupCooldownTicks", DefaultDedupCooldownTicks);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (events == null) events = new List<WorldNewsEvent>();
                if (lastAcceptedTick == null) lastAcceptedTick = new Dictionary<string, int>();
                if (DedupCooldownTicks <= 0) DedupCooldownTicks = DefaultDedupCooldownTicks;
            }
        }
    }
}
