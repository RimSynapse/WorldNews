# Inspirations: Game MCP Architecture for RimSynapse World News

This document outlines the refactoring guidelines for **RimSynapse World News** using the Model Context Protocol (MCP).

---

## 1. What Stays the Same
- **World News Database**: System tracking global faction events, world news history records, and planetary tile structures.
- **Harmony Patches**: Hooks tracking changes on the global map (e.g. settlements destroyed, caravans travelling).

---

## 2. What Changes (The MCP Shift)
- **Dynamic Lore Integration**: Instead of dumping large summaries of world news into the prompt automatically, register a tool that allows the LLM to search or fetch news on demand.
- **Lore Context**: The LLM storyteller can query world events only when choosing a thematic event or generating narrative logs.

---

## 3. Proposed MCP Tools for World News
- `get_planetary_news_feed`: Returns a list of recent major planetary events (e.g. *"Faction X declared war on Y"*, *"A toxic fallout hit the southern continent"*).
- `get_local_settlements_status`: Returns a list of nearby settlements, their distance, and their trading/hostile status.

---

## 4. LLM Narrative Workflow
1. The Storyteller decides to trigger an event.
2. It queries `get_planetary_news_feed()` and reads that a nearby faction is experiencing a severe food shortage.
3. The LLM decides to trigger a quest where the hungry faction begs for food, connecting local gameplay directly to the broader world.
