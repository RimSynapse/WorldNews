# DESIGN: RimSynapse WorldNews & Storyteller Sunset

## 1. The Sunset of RimSynapse-StoryTeller
The `RimSynapse-StoryTeller` module is being officially deprecated. Its responsibilities are being strictly divided and migrated to their logically pure architectural homes:

### A. Core Hooks (Migrating to `RimSynapse-Core`)
The fundamental hooks that intercept the vanilla RimWorld Storyteller (e.g., `StorytellerComp_Synapse.cs`, `StorytellerCompProperties_Synapse.cs`, and the `TriggerPacingAdjustment` logic) act as an intelligence proxy. Because `RimSynapse-Core` is the master context tracker, these base-level intercepts belong natively in the Core module.

### B. Faction Lore (Migrating to `RimSynapse-Factions`)
The generation of dynamic faction backstories (`SynapseFactionEvaluator.cs` and `FactionStoryTracker.cs`) will be moved directly into the `RimSynapse-Factions` module. Factions handles base generation, faction cloning, and leader backstories, making it the definitive source of truth for faction lore.

---

## 2. The Vision for RimSynapse-WorldNews
With the storyteller intercepts moved to Core, this new module—**RimSynapse-WorldNews**—exists to make the RimWorld feel alive, reactive, and historically grounded. It acts as the local journalist and gossip mill for the planet.

### Core Feature 1: The Event Intercept
`RimSynapse-WorldNews` will deploy a Harmony Patch on `Verse.LetterStack.ReceiveLetter`. In RimWorld, every significant player-facing event (Raids, Manhunter Packs, Toxic Fallout, Trade Ships, Quests) generates a "Letter." By intercepting these letters, we capture the exact narrative beats the player is experiencing without having to decipher abstract game ticks. These letters are cached in an `unpublishedEvents` queue.

### Core Feature 2: The Synapse Newspaper System
Once the `unpublishedEvents` queue reaches a threshold (e.g., 3 to 5 events), the module triggers the `SynapseNewspaperGenerator`. 
- **The Prompt:** The LLM is instructed to act as a frontier journalist. It takes the raw events and synthesizes them into a cohesive narrative issue.
- **The Output:** A JSON object containing an overarching dramatic `Headline`, the `Date`, and 3 distinct `Stories` featuring rich flavor text.

### Core Feature 3: Asymmetric Layout UI
When a Newspaper Issue is published, the player receives a special notification letter. Clicking it opens a custom `Dialog_Newspaper`.
- **The Design:** An asymmetric, modern layout. To maximize visual engagement while keeping content balanced, the UI allocates equal square footage to all stories using a dynamic grid.
  - *Example Layout:* A large, prominent vertical column on the left containing the main event (33% of screen space), juxtaposed with two smaller, horizontally stacked blocks on the right (each taking ~33% of space) for minor local gossip or secondary events.

### Core Feature 4: Knowledge & Rumor Propagation (Future Expansion)
Taking the old Storyteller's `FactionRelationshipTracker`, the WorldNews module will track how information travels across the planet. When the player raids an outpost, that knowledge spreads via trader caravans. These global political shifts will occasionally feature in the "World Affairs" section of the local Newspaper.

---

## 3. The 0.8 Newspaper: a publishable, shareable artifact

0.8 turns the newspaper from a text pop-up into a *broadsheet issue* — masthead, columns, a lead story with a photo, secondary stories, sidebars, ads — that the player can read in-game and **share with friends outside the game**. The target look is a classic evening broadsheet (masthead, `VOL. / NO. / date / price` line, a dominant lead + photo, stacked secondaries, section footers).

### 3.1 The constraint that sets the architecture
RimWorld's in-game UI is Unity **IMGUI**. There is no way to render HTML inside the game window without embedding a browser engine (CEF/Chromium) — hundreds of MB, native, per-platform, fragile, and a non-starter for a Workshop mod. **So HTML cannot be the in-game surface.** But HTML *is* the ideal format for a shareable artifact. The resolution is not to choose:

> **One structured data model → two renderers.**

The LLM produces a structured `NewspaperIssue` (below). That model is the single source of truth. Two renderers consume it:

- **In-game renderer (native IMGUI):** upgrades `Dialog_Newspaper` into the broadsheet — masthead, multi-column flow, headline, image slots (Pollinations PNG → `Texture2D.LoadImage` → drawn). This is what the player reads; it is a *flat approximation* of the shared page, not photoreal.
- **Share renderer (HTML+CSS):** fills a static HTML template from the same model and writes a **self-contained** `issue-NN.html` (inline CSS + base64 images = one file to send). A "Share / Open in browser" control in-game does `Application.OpenURL("file://…")`. The browser renders the beautiful version for free.

**Templating:** no Jinja in C#. Use dependency-free `{{token}}` substitution over a static template with a small loop over stories. Do **not** add a templating engine (Scriban/Handlebars.Net) or a separate rendering service/process unless templates genuinely outgrow substitution. **Performance:** the HTML build is a once-per-issue string write, entirely off the tick loop — negligible. The only real cost centers are the async LLM calls and image downloads, both off-main-thread and disk-cached.

### 3.2 The expanded NewspaperIssue model
The current model (`Headline`, `Date`, deltas, `Stories[{Title, Content}]`) grows into the source of truth both renderers read:

- **Masthead:** paper name, `VOL.` (roman), `NO.`, in-game date, price, an optional strapline.
- **Lead story:** headline, byline, body, an optional **image** (prompt + resolved asset), caption.
- **Secondary stories:** headline, optional byline, body, optional image/caption.
- **Sidebars / short items:** weather, "library event", small notices.
- **Ads:** one or two flavor advertisements (also LLM-authored).
- **Section footer / page markers.**
- Retains `PerceivedWealthDelta` / `PerceivedStrengthDelta` for the faction-perception broadcast.

Each illustrated story carries an **image prompt** (LLM-authored) and, once fetched, a **resolved asset reference** (cached PNG path / base64). Facts and prose are separate from assets so an issue can render text-only when assets are absent.

### 3.3 Images via Pollinations
Illustrations come from **Pollinations** (`https://image.pollinations.ai/prompt/<url-encoded-prompt>?width=…&height=…&seed=…&nologo=true`), a keyless (rate-limited) GET. **The LLM agentic framework writes the image prompt**; the pipeline only fetches and caches.

- Fetch off the main thread (mirror `SynapseClient`'s async pattern); create/load `Texture2D` on the main thread.
- **Cache by prompt hash** on disk so an image is fetched once and survives reload; the HTML export inlines it as base64.
- **Graceful degradation is first-class:** offline, disabled, rate-limited, or slow → render text-only. A missing image is never an error.

### 3.4 First-run onboarding / consent
External image generation is **opt-in**, surfaced as a **first-launch onboarding window** that introduces the mod and asks the player to enable (and thereby consent to) outbound calls to pollinations.ai — rather than a buried default-off toggle. Informed consent as UX. Until accepted, the paper is text-only; the setting remains changeable later in mod settings.

### 3.5 Publication cadence — event-driven, two in-game beats
Replaces the current "≥4 events + 2500-tick cooldown" trigger. An issue is a small job/state machine, and **not every day gets an issue** — it is gated on whether the event timeline (the #13 world-map change feed + colony-local letters/gossip) actually has material:

1. **10pm (event cut):** the LLM reviews the day's events and decides whether they are publish-worthy. Thin/uninteresting → **skip**, no issue. Otherwise it selects events, drafts stories, and emits image prompts.
2. **Assets requested:** image prompts go to Pollinations asynchronously; PNGs cache by hash.
3. **Noon (present):** the issue is presented with **whatever is ready** — text is the floor; missing images render text-only. This gives the pipeline the 10pm→noon window to settle.
4. **Roll-over queue:** assets that arrive after their issue is presented are **kept in the processing queue and reused on the next issue**, never discarded.

### 3.6 Relationship to existing 0.8 issues
- Content source is the world-map change feed (#13) and the R&T availability seam (#12, soft dependency) — orthogonal to rendering.
- The forward-looking rendering (#19) and comms-console delivery (#18) sit on top of this model and renderer split rather than beside them.
