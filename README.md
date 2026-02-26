# FFXIV Venues Directory

If you want to browse FFXIV venues without alt-tabbing to a website, this plugin is for you.

It pulls approved venues from FFXIV Venues and shows them in a fast in-game directory with filters, sorting, and a detail panel.

## What You Get

- A live venue list you can refresh anytime.
- Search by name, description, or tags.
- Filters for:
  - region, data center, world
  - open now
  - SFW / NSFW
  - favorites / visited
  - house size and apartments
- Sortable table columns (Venue, Address, Size, Status).
- Detail panel with:
  - venue banner
  - formatted address
  - schedule converted to your local time
  - clickable external links (website, Discord, links in descriptions)
  - copy address
  - optional visit button (visible only when a compatible third-party plugin is installed and enabled)
- Local preference tracking (favorite / visited).

## Command

- `/ffxivvenues` - open the main window.

## Data Source

The plugin uses the official FFXIV Venues API at runtime:

- `https://api.ffxivvenues.com/v1/venue?approved=true`
- `https://api.ffxivvenues.com/v1/venue/{venueId}/media` (fallback banner endpoint)

No venue database is stored permanently by this plugin. Data is fetched live and cached in memory for the session.

## Notes About Content Labels

The plugin follows API data for `sfw` status.

Warnings are shown in the detail panel using the same intent as the website:

- openly NSFW
- adult services (Courtesans tag)
- both, when both conditions apply

## Build (Development)

From repository root:

```bash
dotnet build FFXIV.Venues.Directory.sln -c Release -p:Platform=x64
```

Output DLL:

- `FFXIV.Venues.Directory/bin/x64/Release/FFXIV.Venues.Directory.dll`

## Project Layout

- `FFXIV.Venues.Directory/Core` - plugin entry and composition
- `FFXIV.Venues.Directory/Infrastructure` - command/window/service wiring
- `FFXIV.Venues.Directory/Features/Directory` - UI, filtering, domain models, media cache
- `FFXIV.Venues.Directory/Integrations` - optional IPC integrations

## Why This Exists

The goal is simple: keep venue browsing inside the game, keep it fast, and keep it readable.
