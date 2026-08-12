# The Newspaper: Reading, Sharing and Illustrating

WorldNews turns the events of your game into a broadsheet you can read, illustrate and share. This page covers how an issue reaches you, what it contains, and the options around it.

> Status: this describes the 0.8 newspaper, in development. The current release (v0.7.0) is Regions and Territories compatibility.

## Receiving and reading an issue
When enough news has accumulated, a **"Newspaper Published"** letter arrives. Click it and choose **Read newspaper** to open the broadsheet — a full front page laid out on a single, linked-scroll page:

- **Masthead** — paper name, volume/issue/date/price, and a strapline.
- **Banner headline** — the issue's overarching story.
- **Lead story** — a byline, an illustration, and a drop-cap opening; the body flows down the main column.
- **Side rail** — secondary stories, an **In Brief** panel of short notices, and a single advertisement.
- **Footer** — section marker and paper name.

An issue never publishes empty: it needs a headline and at least one story with real body text, so a thin moment never prints a blank page.

## Sharing: Export to HTML
The broadsheet has an **Export HTML** button. It writes the current issue to a single **self-contained web page** — images are embedded, so the one file carries its own pictures and opens in any browser. After exporting, the toolbar shows the saved file path in a selectable field with a **Copy path** button. Nothing is written until you click Export; the game never saves a copy on its own.

## Illustrations (optional, off by default)
The newspaper can illustrate its stories using **pollinations.ai**, a free online image generator (no account, no key). This is **opt-in**:

- The first published issue shows a one-time consent window; you can also toggle it any time under **Mod Settings → RimSynapse - WorldNews**.
- Only the AI-written **scene description** is sent (e.g. "a dust-caked caravan at the gates") — never personal information, colony data, or save files.
- Pictures are downloaded once and **cached on disk**; with illustrations off, the paper stays text-only.
- Publication **waits** for the pictures to arrive so an issue isn't printed half-illustrated. If the service declines a request, that story simply runs text-only rather than holding the paper up.

## Advertisements
Every issue carries one quirky, RimWorld-flavoured house advertisement, drawn from a pool of twenty and **rotating weekly** — the same ad all week, a new one next week. Half of them ship with a small black-and-white brand mark.

## Breaking news: the comms telegraph
News travels slowly on the rim. Events are **held for a short delay** before they arrive (the deferred-news pipeline, configured under **RimSynapse - Core** settings — a default of two days, per-category, adjustable).

- With a **powered comms console**, held events appear immediately as an advance **BREAKING NEWS** ticker across the top of the screen — your early warning. The banner is movable, resizable and collapsible.
- **Without comms**, there is no early bulletin: you learn of an event only when it is released, and the newspaper reports it then. No comms means you lose that lead time.

## Requires Regions and Territories?
No. WorldNews reports colony-local news on its own. When **Regions and Territories** is present, the paper additionally draws on territory, borders and regional beliefs to cover the wider world.
