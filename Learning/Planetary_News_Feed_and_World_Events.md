# Planetary News Feed and World Events

RimSynapse - World News tracks global events across the planetary map and surfaces them to the AI Storyteller for dynamic lore integration.

---

## 1. Global Map Event Tracker

The mod implements hooks and trackers on the RimWorld world map to monitor developments:
*   **Caravans:** Tracks movement of other factions' caravans across planetary tiles.
*   **Settlement Changes:** Records settlements destroyed, captured, or newly founded on the world map.
*   **Planetwide Incidents:** Logs events like continent-wide toxic fallout, volcanic winters, or economic changes affecting faction bases.

---

## 2. MCP Tool Endpoints

The World News submod exposes tools for the LLM Storyteller to query on-demand:
*   `get_planetary_news_feed`: Returns a chronological log of recent major global events on the planet.
*   `get_local_settlements_status`: Returns nearby settlement names, their exact distance from your colony, and their current trading and faction status.

---

## 3. Storyteller Narrative Integration

Rather than firing quests in isolation, the Storyteller queries planetary news. If a nearby settlement is experiencing a famine, the Storyteller can trigger a quest where their emissaries arrive begging for crops, linking local colony gameplay directly to global lore events.
