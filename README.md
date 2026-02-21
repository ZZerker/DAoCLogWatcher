# DAoC Log Watcher
Currently in Beta Testing state. 
A real-time Realm Point tracker for **Dark Age of Camelot (Eden)**. Load your chat log and instantly see how many RPs you're earning, where they're coming from, and how fast they're rolling in.

---

## Features

- **Live tracking** — reads your chat log as the game writes it, no manual refreshing
- **RP breakdown** by source ⚠️ *parsing & categorization in development — values may be inaccurate*:
  - Player Kills
  - Campaign Quests
  - Battle Ticks
  - Siege (Tower & Keep Captures)
  - Assault Orders
  - Support Activity
  - Relic Captures
- **RP/h meter** — rolling realm points per hour, updated every 5 seconds
- **Per-character kill counter** ⚠️ *in development — counts may be incorrect* — automatically reads your character roster from the Eden profile folder and tracks kills per character
- **Charts** — cumulative RP over time and rolling RP/h graph
- **Time filters** — optionally show only the last 6 or 24 hours of your log
- **Dark & Light theme** — toggle any time
- **Windows & Linux** supported (Linux via Wine / Lutris)

---

## ⚠️ Known Issues (Beta)

The following features are still in active development and may produce incorrect results:

| Feature | Status |
|---|---|
| RP source categorization | Parsing of some log line formats is incomplete — sources may be misidentified |
| Percentage breakdown | Calculated from categorized RPs, so inaccuracies above carry through |
| Kill count per character | Character matching is unreliable — counts may be wrong or missing |

Please report unexpected behaviour on the [Issues](https://github.com/ZZerker/DAoCLogWatcher/issues) page.

---

## Getting Started

### 1. Enable chat logging in DAoC

In-game, type:

```
/chatlog
```

This creates (or resumes) `chat.log` in your DAoC documents folder. You only need to do this once per session.

### 2. Launch DAoC Log Watcher

Download the latest release and run `DAoCLogWatcher.UI.exe` (Windows) or the equivalent binary on Linux.

### 3. Open your log

- Click **Open DAoC Log** — the app will try to find your `chat.log` automatically
- Or click **Open Log File** and browse to it manually

The app starts reading immediately and updates the display as new lines arrive.

---

## Time Filters

| Filter | What it shows |
|---|---|
| *(none)* | Everything in the log file |
| **Last 24h only** | Only entries from the past 24 hours |
| **Last 6h only** | Only entries from the past 6 hours |

Filters apply from the moment you open a log — useful if your `chat.log` has many days of history and you only care about today.

---

## Character Kill Tracking

The app reads your Eden profile folder (`%AppData%\Electronic Arts\Dark Age of Camelot\eden`) to find your character names. When a player kill is awarded, it matches the killed player's name against your roster and increments that character's kill counter in the **MY CHARACTERS** panel.

If the panel is not visible, no character profiles were found in that folder.

---

## Log File Location

| Platform | Default path |
|---|---|
| Windows | `%USERPROFILE%\Documents\Electronic Arts\Dark Age of Camelot\chat.log` |
| Linux (Wine) | `~/.wine/drive_c/users/<user>/My Documents/Electronic Arts/Dark Age of Camelot/chat.log` |
| Linux (Lutris) | `~/Games/dark-age-of-camelot/drive_c/users/<user>/My Documents/Electronic Arts/Dark Age of Camelot/chat.log` |

---

## Requirements

- [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Dark Age of Camelot (Eden server) with chat logging enabled (`/chatlog`)

---

## Building from Source

```bash
git clone https://github.com/ZZerker/DAoCLogWatcher.git
cd DAoCLogWatcher
dotnet build
dotnet run --project DAoCLogWatcher.UI
```
