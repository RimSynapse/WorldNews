using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimSynapse.WorldNews.API
{
    /// <summary>
    /// Read-only agent tools that let the LLM pull world state on demand instead of having it
    /// pushed into every prompt:
    ///   - get_planetary_news_feed        (WorldNews#1) — recent recorded world/colony events
    ///   - get_local_settlements_status   (WorldNews#2) — nearby settlements, distance and relation
    /// Registered from <see cref="RimSynapseWorldNewsMod"/> startup, after Core is up. Neither tool
    /// mutates game state, so both stay non-mutating in the registry (autonomous runs may call them).
    /// </summary>
    public static class WorldNewsMcpTools
    {
        public static void RegisterTools()
        {
            SynapseToolRegistry.RegisterTool(
                "get_planetary_news_feed",
                "Pull recent recorded planetary/world news events (faction wars, disasters, frontier "
                + "happenings, colony events) newest-first. Read-only — call it when you want current "
                + "events rather than relying on them being in the prompt.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        maxEvents = new
                        {
                            type = "integer",
                            minimum = 0,
                            description = "Maximum number of events to return, newest first. Default 20."
                        }
                    }
                },
                GetPlanetaryNewsFeedHandler
            );
            RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] Registered tool get_planetary_news_feed");

            SynapseToolRegistry.RegisterTool(
                "get_local_settlements_status",
                "List non-player world settlements with their faction, approximate distance in tiles "
                + "from your colony, relation to you (Hostile/Neutral/Ally) and goodwill. Read-only.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        maxTiles = new
                        {
                            type = "integer",
                            minimum = 0,
                            description = "Only include settlements within this many tiles of the colony. Omit for all."
                        }
                    }
                },
                GetLocalSettlementsStatusHandler
            );
            RimSynapse.SynapseLogger.Message("[RimSynapse-WorldNews] Registered tool get_local_settlements_status");
        }

        // Argument DTOs: fields are populated by JSON deserialization via reflection, which the
        // compiler cannot see — hence CS0649 ("never assigned"). Suppressed for this region only.
#pragma warning disable CS0649

        // ---- get_planetary_news_feed (WorldNews#1) ----------------------------------------------

        private class FeedArgs { public int? maxEvents; }

        /// <summary>
        /// Returns recorded events newest-first as a JSON array of {date, label, summary}. Read-only:
        /// it enumerates <see cref="SynapseWorldNewsWorldComponent.unpublishedEvents"/> without touching
        /// it and never triggers generation. Published NewspaperIssue headlines are not yet retained
        /// anywhere (that store arrives with the newspaper archive, WorldNews#39); until then the
        /// recorded-event queue — the same source the generator consumes — IS the feed.
        /// </summary>
        private static string GetPlanetaryNewsFeedHandler(string argsJson)
        {
            try
            {
                int maxEvents = 20;
                FeedArgs args = ParseArgs<FeedArgs>(argsJson);
                if (args?.maxEvents != null) maxEvents = args.maxEvents.Value;
                if (maxEvents < 0) maxEvents = 0;

                var result = new List<object>();
                List<string> events = Find.World?.GetComponent<SynapseWorldNewsWorldComponent>()?.unpublishedEvents;
                if (events != null && maxEvents > 0)
                {
                    for (int i = events.Count - 1; i >= 0 && result.Count < maxEvents; i--)
                    {
                        result.Add(ParseEventLine(events[i]));
                    }
                }
                return JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                return ErrorJson("get_planetary_news_feed", ex);
            }
        }

        /// <summary>
        /// Split a stored event line — "[Twelfth, Year] Label: summary" (letters/gossip) or
        /// "[Twelfth, Year] free-form world line" (world-map events, no colon) — into its parts.
        /// A line missing either the bracketed date or the "Label: " prefix still parses: the missing
        /// piece comes back empty and the remainder lands in summary.
        /// </summary>
        private static object ParseEventLine(string raw)
        {
            string date = "";
            string rest = raw ?? "";
            if (rest.StartsWith("["))
            {
                int end = rest.IndexOf(']');
                if (end > 0)
                {
                    date = rest.Substring(0, end + 1);
                    rest = rest.Substring(end + 1).TrimStart();
                }
            }

            string label = "";
            string summary = rest;
            int colon = rest.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0)
            {
                label = rest.Substring(0, colon);
                summary = rest.Substring(colon + 2);
            }
            return new { date, label, summary };
        }

        // ---- get_local_settlements_status (WorldNews#2) -----------------------------------------

        private class SettlementsArgs { public int? maxTiles; }
#pragma warning restore CS0649

        private class SettlementRow
        {
            public string name;
            public string factionName;
            public string factionDefName;
            public int distanceTiles;
            public string relationKind;
            public int goodwill;
        }

        /// <summary>
        /// Returns non-player settlements as a JSON array sorted nearest-first. Distance is the minimum
        /// over the player's home-map tiles (nearest colony wins) on the settlement's own planet layer —
        /// cross-layer pairs have no defined tile distance and are skipped. No home map (caravan-only) →
        /// empty array. Read-only: reads world objects and faction relations, mutates nothing.
        /// </summary>
        private static string GetLocalSettlementsStatusHandler(string argsJson)
        {
            try
            {
                SettlementsArgs args = ParseArgs<SettlementsArgs>(argsJson);
                int maxTiles = args?.maxTiles ?? int.MaxValue;
                if (maxTiles < 0) maxTiles = 0;

                var rows = new List<SettlementRow>();
                List<Settlement> settlements = Find.WorldObjects?.Settlements;
                WorldGrid grid = Find.WorldGrid;
                List<PlanetTile> homeTiles = Find.Maps?
                    .Where(m => m != null && m.IsPlayerHome)
                    .Select(m => m.Tile)
                    .ToList() ?? new List<PlanetTile>();

                if (settlements != null && grid != null && homeTiles.Count > 0)
                {
                    foreach (Settlement s in settlements)
                    {
                        Faction f = s?.Faction;
                        if (f == null || f.IsPlayer) continue;
                        if (!s.Tile.Valid) continue;

                        float dist = float.MaxValue;
                        foreach (PlanetTile home in homeTiles)
                        {
                            if (!home.Valid || home.Layer != s.Tile.Layer) continue;
                            try
                            {
                                float d = grid.ApproxDistanceInTiles(home, s.Tile);
                                if (d < dist) dist = d;
                            }
                            catch { /* unmeasurable pair — skip this home tile */ }
                        }
                        if (dist == float.MaxValue) continue; // nothing measurable on the settlement's layer

                        int distanceTiles = Mathf.RoundToInt(dist);
                        if (distanceTiles > maxTiles) continue;

                        rows.Add(new SettlementRow
                        {
                            name = !string.IsNullOrEmpty(s.Name) ? s.Name : s.Label,
                            factionName = f.Name,
                            factionDefName = f.def?.defName,
                            distanceTiles = distanceTiles,
                            relationKind = f.PlayerRelationKind.ToString(),
                            goodwill = f.PlayerGoodwill
                        });
                    }
                }

                rows.Sort((a, b) => a.distanceTiles.CompareTo(b.distanceTiles));
                return JsonConvert.SerializeObject(rows);
            }
            catch (Exception ex)
            {
                return ErrorJson("get_local_settlements_status", ex);
            }
        }

        // ---- shared -----------------------------------------------------------------------------

        /// <summary>Parse a tool's argument JSON, treating null/blank/"{}" as "no args" (null result).
        /// Malformed JSON throws, which the handlers turn into a structured error payload.</summary>
        private static T ParseArgs<T>(string argsJson) where T : class
        {
            if (string.IsNullOrWhiteSpace(argsJson)) return null;
            string trimmed = argsJson.Trim();
            if (trimmed.Length == 0 || trimmed == "{}") return null;
            return JsonConvert.DeserializeObject<T>(trimmed);
        }

        private static string ErrorJson(string tool, Exception ex)
        {
            RimSynapse.SynapseLogger.Warn("worldnews", $"[RimSynapse-WorldNews] {tool} error: {ex.Message}");
            return JsonConvert.SerializeObject(new { error = tool + " failed", detail = ex.Message });
        }
    }
}
