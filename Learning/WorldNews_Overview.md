# RimSynapse WorldNews Overview

`RimSynapse-WorldNews` creates a living, breathing planet by broadcasting events through dynamic in-game newspapers. 

## The World Event Ledger
The module constantly listens to events happening in your colony and across the globe. These events are logged into the World Event Ledger, a chronological database of historical narrative occurrences.

## Asymmetric Newspapers
Periodically, your colony will receive issues of a global newspaper. 
- **The Main Story**: Often driven by major events from your own colony (like repelling a huge raid, launching a ship, or suffering a catastrophic breakdown).
- **Secondary Stories**: The LLM fills the rest of the newspaper with stories from other factions, giving you insight into wars, trade deals, and rumors happening off-map.

These newspapers use the `Dialog_Newspaper` broadsheet UI — masthead, a drop-cap lead story with an illustration, a side rail of secondary stories and short notices, and a rotating advertisement. You open an issue from its "Newspaper Published" letter, and can **export it as a self-contained web page**, **illustrate it** with opt-in images, and — with a comms console — get an advance **breaking-news ticker**. See [The Newspaper: Reading, Sharing and Illustrating](The_Newspaper) for the full walkthrough.

## Intercepting Visitor Gossip and Rumors

The newspaper module subscribes to core rumor events natively:
*   When a visitor leaves the player colony, the core mod calculates if they witnessed significant events (e.g. major surgeries, marriages, or deaths) and logs a `VisitorRumorSpreading` event to the Core backlog ledger.
*   `RimSynapse-WorldNews` hooks `EnqueuePastEvent` to dynamically detect these rumors, formatting them into local gossip columns (e.g., "[Twelfth, Year] Gossip: (Visitor) spreads rumors of (Event)...").
*   Accumulating four unpublished gossip or letter events triggers dynamic newspaper generation, which spreads the news across the planet.
