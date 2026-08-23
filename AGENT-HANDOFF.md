# AGENT-HANDOFF — `feature/prompt-lab-composer`

## What this branch adds

The **WorldNews contribution** to the universal Prompt Lab (the central console/registry lives in
rimworld-claude-dev-tools): the mod-owned pure composer the lab links to build the exact newspaper prompt
without launching RimWorld. Branched off `development`.

- **NEW** `Source/Newspaper/NewspaperPromptComposer.cs` — pure, Verse-free `Compose(IReadOnlyList<string>
  events) → NewspaperPrompt {system, user}`. The static broadsheet system prompt + the `Raw Events:` user
  message move here verbatim.
- **CHANGED** `Source/Newspaper/SynapseNewspaperGenerator.cs` — `Generate` now calls
  `NewspaperPromptComposer.Compose(unpublishedEvents)` instead of building the strings inline. Pure behaviour
  preserved (same system + user text).

The dev-tools PromptLab console `<Compile>`-links `NewspaperPromptComposer.cs` from the workspace as the
`newspaper` family, so the lab can't drift from the game.

## Verify
- `dotnet build Source/RimSynapseWorldNews.csproj -c Release` → succeeds (needs Core + Regions-and-Territories
  built; an isolated worktree needs those junctioned at `..\..\Core` and `..\..\Regions-and-Territories`).
- Exercised end-to-end by the dev-tools lab against the live local model — a full issue generated from the
  `windfall` event batch (headline + positive PerceivedWealthDelta).
