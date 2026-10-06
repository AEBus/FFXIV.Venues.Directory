# FFXIV Venues Directory

Find player-run venues and community events without leaving the game.

FFXIV Venues Directory is a Dalamud plugin that brings the venue listings of [FFXIV Venues](https://ffxivvenues.com), the events posted on [Partake](https://www.partake.gg) and the venue ads in the in-game Party Finder into one window. You can see what is open right now, when everything else opens in your own time zone, and where to find it.

## Getting started

1. Install **FFXIV Venues Directory** from the Dalamud plugin installer (`/xlplugins`). While the plugin is in testing, first turn on **Get plugin testing builds** in `/xlsettings` → Experimental.
2. Open the directory with `/ffxivvenues`, or with the Open button in the plugin installer.
3. Narrow the list with the filters on the left and pick a venue to see its details.

## Finding a venue

- **Search** looks through venue names, descriptions and tags.
- **Show**: venues that are *Open now*, that open *Soon* (within three hours), or at *Any time*; all of them, only SFW or only NSFW.
- **Location**: pick a region, data center and world, or turn on **My data center** to always see the data center your character is on, even after data center travel. Each character remembers its own location.
- **House size**: apartments, small, medium and large houses, and **Elsewhere** for places outside the housing districts.
- **Tags**: click a tag once to require it, twice to exclude it, and a third time to clear it.
- **Saved**: favorites, visited or not yet visited venues, and the venues you have hidden.
- **Venues not on FFXIV Venues**: places that post events on Partake or advertise in the Party Finder but are not listed on FFXIV Venues appear in the list too; turn this off to see FFXIV Venues listings only.
- Click a column header to sort by venue, location, size or status.
- In a narrow window the filters move under the **Filters** button at the top.

## Venue details

- The status shows whether a venue is open and until when, when it opens next, or that it is open 24/7. It follows the clock: venues open and close in the list as their times pass.
- The schedule is shown in your local time, together with breaks, one-off openings and notices from the venue.
- Descriptions keep their formatting: headings, bold and italic text, lists, links and pictures, including animated GIF, WebP and APNG images.
- **Copy address**, **Website** and **Discord** are one click away. **Show on map** marks places outside the housing districts on the game map.
- Upcoming Partake events at the venue and its current Party Finder ads are listed in its details.
- Use the star to add a favorite, the check mark to mark a venue as visited, and the eye to hide it from the list (the top bar offers an undo). Right-click a venue in the list for the same actions.
- When you stand on a listed venue's plot or are inside it, the top bar tells you where you are and lets you mark the venue as visited.

## Events

The **Events** tab lists the FFXIV events posted on Partake for the next two weeks and the venue ads currently up in the Party Finder, with what is happening right now at the top.

- Filter by time (*Live*, next 24 hours, next 7 days, or all), by age rating and by Partake tags. Location and house size are shared with the venue filters.
- Mark an event with **I'm going** to keep it in your **Going** list. This works like Partake's "Attend" but stays in the plugin, so no Partake account is needed.
- Event details show the full description with times in your time zone, the address, and links to the venue and to the event on Partake.
- A Party Finder ad shows the ad's text and since when it has been up. Ads posted by several members of a venue's staff are shown as one, and an ad for an event the venue already has on Partake is left to that event.

## Notifications

Turn them on in the settings; they also work while the directory window is closed:

- when a favorite venue opens;
- when an event at a favorite venue starts;
- 15 minutes before an event you are going to.

Click a notification to open the venue or event.

## Settings

Open the settings with the gear in the top right corner of the window, or with the Settings button in the plugin installer. They have a page per topic:

- **Appearance**: the directory's own theme or your Dalamud theme; an interface size from 90% to 130%, on top of Dalamud's own UI scale; and the time format (*Auto* follows Windows, or always 24-hour or 12-hour).
- **Images**: whether pictures in descriptions are loaded (when off, each one is a link), and whether hovering a banner or a picture shows it in full.
- **Events**: events from Partake and venue ads from the Party Finder, each of which can be turned off.
- **Notifications**: the three notifications above.
- **About**: the version, where the data comes from, and the changelog.

## Privacy and data

- Venue listings come from the FFXIV Venues public API and are refreshed every 30 minutes while you use them.
- Events come from the Partake public API and are refreshed every 15 minutes while you use them; an event's description is only loaded when you open the event.
- Party Finder ads come from [xivpf.com](https://xivpf.com), which collects the listings players' game clients see. They are loaded every 10 minutes, and only while the directory window is open.
- Partake and Party Finder ads can each be turned off in the settings; when off, the plugin sends nothing to that service.
- Requests to Partake and xivpf.com name the plugin and its version in the User-Agent, as agreed with those services. Requests for pictures hosted elsewhere carry a generic User-Agent.
- Images are loaded only over https: banners from FFXIV Venues and Partake, and pictures in descriptions from wherever they are hosted (this can be turned off).
- The plugin sends no information about you or your character. Favorites, visited and hidden venues, events you are going to, and your settings are stored only in your local Dalamud plugin configuration.
- "You're here" and **My data center** read your current location from the game on your own computer.

## Feedback

Found a bug or have an idea? Open an [issue on GitHub](https://github.com/AEBus/FFXIV.Venues.Directory/issues), or use the feedback button in the Dalamud plugin installer. What changed in each version is in the [changelog](CHANGELOG.md).

## Building from source

You need the .NET 10 SDK and a Dalamud development environment (XIVLauncher installed and run at least once). Build the plugin project with:

```
dotnet build FFXIV.Venues.Directory/FFXIV.Venues.Directory.csproj -c Release -p:Platform=x64
```

The build goes to `FFXIV.Venues.Directory/bin/x64/Release/`, with the packaged `latest.zip` in its `FFXIV.Venues.Directory` subfolder. To try a local build, add the path of `FFXIV.Venues.Directory.dll` from that folder under **Dev Plugin Locations** in `/xlsettings` → Experimental.

## Credits and license

Venue listings are provided by [FFXIV Venues](https://ffxivvenues.com), events by [Partake](https://www.partake.gg) and Party Finder ads by [xivpf.com](https://xivpf.com); thanks to all three for their public APIs. This is an independent fan project, not affiliated with Square Enix.

The plugin started as a fork of the [original FFXIV Venues plugin](https://github.com/FFXIVVenues/ffxiv-venues-dalamud) and has since been rewritten; thanks to its authors for the start.

Licensed under the [GNU General Public License v3.0](LICENSE).
