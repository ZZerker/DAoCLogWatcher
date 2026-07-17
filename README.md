# DAoC Log Watcher

![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/ZZerker/DAoCLogWatcher/total)
![GitHub Release](https://img.shields.io/github/v/release/ZZerker/DAoCLogWatcher)
![CI](https://img.shields.io/github/actions/workflow/status/ZZerker/DAoCLogWatcher/ci.yml?branch=main)
[![Discord](https://img.shields.io/badge/Discord-Join-5865F2?logo=discord&logoColor=white)](https://discord.gg/V7Z5y3Ke9v)

A real-time tracker for **Dark Age of Camelot (Eden)**. Load your `chat.log` and instantly see how many RPs you're earning, where they're coming from, how fast they're rolling in, live combat metrics, and a server-wide frontier kill heatmap with live keep ownership and fight locations pulled from the warmap — all updated as you play.

## TL;DR — Getting Started

1. In-game, run **`/chatlog`** to start writing your chat log to disk (only needed once per session)
2. Run **`/stats`** in-game so the app can detect your character name
3. Open DAoCLogWatcher — use **Browse Sessions** to pick a specific play session (recommended), or **Open DAoC Log** to tail the file from the current moment

![DAoC Log Watcher screenshot](DAoCLogWatcher.Core/TestFiles/DAoC_Log_Watcher.png)

---

## Features

### 🎯 Core RP Tracking
- **Live log tracking** — reads your chat log as the game writes it, no manual refreshing needed
- **RP breakdown** by source:
  - Player Kills (with victim name when available), Campaign Quests, Battle Ticks
  - Siege (Tower & Keep Captures), Assault Orders
  - Support Activity, Relic Captures, Timed Missions
- **RP/h meter** — rolling realm points per hour, updated every 5 seconds
- **Time filters** — start reading the log from a point in the past ("1h ago" up to "1 week ago", or a custom hours/minutes value)

### 📊 Dashboard & Visualization
- **Customizable dashboard** — show/hide each widget, drag tiles to rearrange them, and drag the corner grip to resize through five sizes (XS–XL); save the result as a named layout profile
  - Supported widgets: RP stats, Combat stats, Kill/death ratio, Best multi-kill, Hottest zone, RP sources, Damage output, Top opponents/spells/healers, Damage taken, Heals done, Zone activity, Logs, Minimap
  - Save multiple profiles for different characters or playstyles and switch between them from the profile dropdown
- **Charts** — cumulative RP over time and rolling RP/h graph, both collapsible and interactive
- **Session browser** — pick any past play session directly from your `chat.log` by date and time range, instead of guessing time filter windows; selecting the current session keeps the app tailing the file live as usual
- **Session history** — every watched session is recorded automatically; a history dialog shows total RP, best session, best RP/h, and average duration, plus a per-session list (duration, RP, RP/h, kills, deaths, best multi-kill, top zone) filterable by character

### 🖥️ In-Game Overlay (OSD)
- **Always-on-top overlay** while you play: character name with live indicator, total RP + RP/h, damage & heal totals (the bigger one shown first), and a feed of your last kills
- **Lock / unlock** — locked, the overlay is click-through so it never eats your mouse input; unlocked, drag it anywhere and set its background transparency with a slider. Toggle the lock from the main window toolbar
- Position and opacity are remembered; the overlay reopens automatically on the next start if it was active
- **Platform support:** full on Windows and Linux/X11. On **Wayland** the app runs via XWayland; on **KDE Plasma (KWin)** import the bundled window rule so the overlay stays above the game even in fullscreen — see [Linux Overlay on Wayland (KDE Plasma)](#linux-overlay-on-wayland-kde-plasma). Other Wayland compositors (GNOME, wlroots) offer no equivalent — use the X11 session there

### 👤 Character & Combat Tracking
- **Character detection** — type `/stats` in-game and the app identifies your character name and displays it in the sidebar with live session stats
- **Combat tracking** — a dedicated Combat tab shows stats parsed live from your combat log:
  - **Damage dealt** — total, hit count, average per hit, and crit count / crit rate
  - **Damage taken** — total received and breakdown by attacker
  - **Heals received** — total HP healed and breakdown by healer
  - **Outgoing heals** — total HP you healed and breakdown by target
  - **Miss & resist rates** — melee miss rate (misses + blocks) and spell resist rate, tracked separately
  - **Attack type breakdown** — avg damage per weapon / spell / melee style, shown as a bar chart
  - **Melee style attribution** — weapon hits are attributed to the style that triggered them; unexecuted styles produce plain unattributed swings
- **DoT tracking** — active DoTs on each target are shown as live-updating stack entries in the combat log; each tick updates the entry in place rather than flooding the log
- **Data-driven spell detection** — AoE nukes and DoT windows are resolved against a built-in spell database, improving attribution accuracy across all classes and specs
- **Combat & Heal logs** — scrollable per-hit event streams with timestamp, damage/HP, source, and target

### 🗺️ Frontier Map & Zone Tracking
- **Kill Heatmap** — a live frontier map showing server-wide kill density as a colour-intensity overlay; the log picks up every kill message on the server (not just your own), so it reflects where the realm-wide action is; keep and tower icons are coloured by current realm owner and update as control changes; burning keeps show a flame icon; active fights and group locations streamed from the warmap are displayed as realm-coloured markers on the map
- **Zone activity** — shows which frontier zones have had the most kills in the active window, ranked by percentage with a heat-colour indicator; a Global Activity chart below plots kill rate over the full session
- **Live warmap data** — the app connects to the Eden warmap WebSocket and pulls in real-time keep/tower realm ownership, keeps under attack, active fight locations, and roaming group positions; no in-game action needed

### 🛠️ UI & Accessibility
- **Log filters** — filter the Realm Points, Combat Log, and Heal Log tabs by player name, source, spell, or style; results update live as new entries arrive
- **Screenshot to clipboard** — capture the full window to your clipboard with one click
- **Dark & Light theme** — toggle any time
- **Secondary monitor support** — the app automatically opens on a secondary monitor if one is present, since DAoC typically runs full-screen on the primary
- **Auto-update** — the app checks for new releases on startup and prompts you to install them

### 💻 Cross-Platform
- **Windows & Linux** supported (Linux via Wine / Lutris / Flatpak)
- Native path detection for all platforms
- Flatpak sandbox support with full WebSocket networking

---

## Installation

### Windows

1. Go to the [Releases](https://github.com/ZZerker/DAoCLogWatcher/releases/latest) page
2. Download [`DAoCLogWatcher-win-Setup.exe`](https://github.com/ZZerker/DAoCLogWatcher/releases/latest/download/DAoCLogWatcher-win-Setup.exe)
3. Run the installer — the app installs and launches automatically
4. Future updates are applied from within the app (no re-downloading needed)

### Linux (Flatpak)

1. Go to the [Releases](https://github.com/ZZerker/DAoCLogWatcher/releases/latest) page
2. Download the `.flatpak` bundle
3. Install it:
   ```bash
   flatpak install --user DAoCLogWatcher.flatpak
   ```
4. Run it:
   ```bash
   flatpak run io.github.zzerker.DAoCLogWatcher
   ```

### Linux (manual / Wine)

1. Download the Linux archive from the [Releases](https://github.com/ZZerker/DAoCLogWatcher/releases/latest) page
2. Extract and run `DAoCLogWatcher.UI`
3. Make sure the [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) is installed

---

## Getting Started

### 1. Enable chat logging in DAoC

In-game, open the chat window and type:

```
/chatlog
```

This creates (or resumes) `chat.log` in your DAoC documents folder. You only need to do this **once per session** — the file persists between logins.

### 2. Open your log

- Click **Open DAoC Log** — the app auto-detects your `chat.log` based on the default install path
- Or click **Open Log File** to browse manually

The app starts reading immediately and updates the display as new lines arrive.

### 3. Identify your character

Type `/stats` in-game at any point. DAoC writes a line like:

```
Statistics for Caranthir this Session:
```

The app detects this and displays your character name in the sidebar with live session stats. Events that occurred earlier in the session are retroactively attributed once your name is known.

---

## Time Filters

For a specific past play session, use **Browse Sessions** — it lets you pick the exact session by date and time range straight from your `chat.log`. Time filters are the complement for live tailing: they set a starting point in the past ("1h ago" up to "1 week ago", or a custom hours/minutes value) and include everything from there on — useful when your `chat.log` spans days and you only care about recent activity. Changing the filter while watching automatically restarts the session with the new starting point.

---

## Character Detection

Type `/stats` in-game at any point — the app detects the `Statistics for <name> this Session:` line and shows your character in the sidebar. Events from earlier in the session are retroactively attributed once your name is known, and checking other players with `/stats player <name>` won't confuse the detection. Stats reset each time you open a log.

---

## Dashboard Profiles

Save and load custom widget layouts for different characters or playstyles:

1. On the Dashboard tab, enter Customize mode — drag tiles to rearrange them, drag the corner grip to resize (XS–XL), and show/hide widgets from the list
2. Click **Save Profile** and enter a name (e.g., "Solo Caster", "RvR Grind", "Heal Support")
3. Switch layouts with the profile dropdown — all widget state is preserved per profile
4. Click **Save As** to duplicate a profile, or **Delete** to remove it
5. New widgets added in app updates are appended to existing profiles automatically

Profiles are stored in `AppSettings.json` and persist across app restarts.

The dashboard is brand new, so feedback is very welcome — ideas for widgets, layout improvements, or just your opinion on how it works. Drop a note in [Discord](https://discord.gg/V7Z5y3Ke9v).

---

## Log File Location

| Platform | Default path |
|---|---|
| Windows | `%USERPROFILE%\Documents\Electronic Arts\Dark Age of Camelot\chat.log` |
| Linux (Wine default) | `~/.wine/drive_c/users/<user>/My Documents/Electronic Arts/Dark Age of Camelot/chat.log` |
| Linux (Lutris) | `~/Games/dark-age-of-camelot/drive_c/users/<user>/My Documents/Electronic Arts/Dark Age of Camelot/chat.log` |

If **Open DAoC Log** can't find the file automatically, use **Open Log File** to browse to it.

---

## Combat & Heal Tracking

Combat parsing covers weapon attacks, melee styles, spells, crits, heals, misses, blocks, and resists. All data is attributed using:

- **Spell data-driven detection** — DoT spells and AoE nukes are looked up in a bundled database of spell data, so ticks and multi-target hits are attributed to the right spell
- **Two-tier spell attribution** — pending spells (last 4.5s window) vs. confirmed hits ensure multi-hit abilities and DoT ticks are correctly assigned
- **Comprehensive log coverage** — results depend on log line formats; edge cases are continuously improved based on community log samples

**Report combat parsing issues on the [Issues](https://github.com/ZZerker/DAoCLogWatcher/issues) page** — log examples are always welcome and directly help improve coverage. Join the [Discord](https://discord.gg/V7Z5y3Ke9v) to share logs in real-time.

---

## Linux Overlay on Wayland (KDE Plasma)

The app has no native Wayland backend, so on a Wayland session it runs through **XWayland**. On Wayland, a *focused* fullscreen game is stacked above normal always-on-top windows, so the overlay disappears behind the game. On **KDE Plasma (KWin)** a one-time window rule fixes this by raising just the overlay into KWin's on-screen-display layer (above fullscreen) — the game itself is left untouched and still covers the panel normally.

**Import the bundled rule (quickest):**

1. Download [`linux/DAoC-Overlay-KDE-Wayland.kwinrule`](linux/DAoC-Overlay-KDE-Wayland.kwinrule)
2. **System Settings → Window Management → Window Rules → Import**, choose the file, then **Apply**
3. Restart the overlay (toggle it off/on from the toolbar) so KWin applies the rule to it

**Or create it by hand** — *System Settings → Window Management → Window Rules → Add New…*:

| Field | Value |
|---|---|
| Window class | `io.github.zzerker.DAoCLogWatcher` — Exact Match |
| Window title | `DAoC Overlay` — Exact Match |
| Layer | **Force** → `On Screen Display` |

The overlay then stays above the game (including fullscreen) while remaining draggable — unlock it from the toolbar to move it. The AppImage and manual builds even add this rule automatically on first launch when they detect a KDE Plasma Wayland session, so no manual step is needed there. (The Flatpak build is sandboxed and can't reach KWin's config, so Flatpak users should import the rule as shown above.)

### GNOME and other Wayland compositors

Only KDE Plasma (KWin) exposes a window-rule mechanism for this. **GNOME/Mutter** has no window rules, no layer-shell, and does not let applications stay above a fullscreen window, so there is no configuration that keeps the overlay on top there. **wlroots** compositors (Sway, Hyprland) are the same.

On those desktops, **log into an X11 session** instead — the overlay works fully under X11:

- **GNOME:** at the login screen (GDM), click your name, then the ⚙️ gear button and pick **GNOME on Xorg** before entering your password. (If it's missing, your distro may ship Wayland-only; installing the `gnome-session-xorg`/Xorg packages restores it.)
- Verify with `echo $XDG_SESSION_TYPE` — it should print `x11`.

---

## Known Quirks

| Feature | Status |
|---|---|
| Combat parsing | Combat and heal tracking depend on log line formats and edge cases may require updates for uncommon classes or abilities — [report issues](https://github.com/ZZerker/DAoCLogWatcher/issues) with log examples |
| Overlay on Linux/Wayland | The app runs via XWayland on Wayland, where a focused fullscreen game is stacked above normal always-on-top windows. On **KDE Plasma (KWin)** import the bundled window rule (see [Linux Overlay on Wayland](#linux-overlay-on-wayland-kde-plasma)) and the overlay stays above the game, even in fullscreen. GNOME/Mutter and wlroots compositors have no equivalent — use the X11 session there |

---

## Community

Join us on [Discord](https://discord.gg/V7Z5y3Ke9v) for bug reports, feature requests, and sharing log samples.

---

## Building from Source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

```bash
git clone https://github.com/ZZerker/DAoCLogWatcher.git
cd DAoCLogWatcher
dotnet build
dotnet run --project DAoCLogWatcher.UI
```

Run tests:

```bash
dotnet test DAoCLogWatcher.Tests/DAoCLogWatcher.Tests.csproj
```
