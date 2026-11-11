<div align="center">

![Icon](Thunderstore/icon.png)

# Ullr's Peak: Mythic Ski & Snowboard (`UllrSkiAndSnowboard`)

A multiplayer Valheim mod (`BepInEx 5` + `Jötunn`) that lets you and your friends carve down Valheim's mountains on mythic Norse skis and snowboards — complete with buildable jump ramps, timed slalom race gates, and chained uphill rope tows.

</div>

## Features

### Gear (Workbench Level 1)
- **Ullr's Frost-Rune Skis** (`6 Wood` + `2 LeatherScraps`) — Twin carved ash & silver-edged skis with glowing cyan frost runes and **Skadi's Runic Ski Poles**.
- **Moder's Drakkar Snowboard** (`6 Wood` + `2 LeatherScraps`) — Wide twin-tip snowboard with sideways carve stance, tighter turn radius, and punchy bunny-hop pop.
- **Ullr's Frost Ward** — Equipping either item in your **Utility** slot grants **Frost Resistance** so fresh characters can head straight into the Mountains without freezing, and automatically marks **`Ullr's Summit`** (the tallest nearby mountain peak) on your minimap.

### Physics & Controls
| Input | Action |
| :--- | :--- |
| **`[X]`** *(configurable)* | Click into / unclip from your Skis or Snowboard (or sprint onto any snowy slope `> 8.5°`). Equipping your **Hammer** auto-unclips for safe slope building. |
| **`[A]` / `[D]` + Mouse** | Carve turns — edge bite converts lateral slip into curved downhill speed. |
| **`[W]`** | **Aero Downhill Tuck** at speed, or **Pole-Push / Skate-Push** on flats & gentle uphills. |
| **`[S]`** | **Snowplow / Hockey-Stop Brake** with powder spray. |
| **`[Space]`** | Hold briefly & release for a crouch-charged **Bunny-Hop** (or hop off a Rope Tow). |
| **`[A]` / `[D]` + `[Right-Click / C]` (Mid-Air)** | **180° / 360° / 540°+ Spins** and **Frost Grabs** — clean slope landings absorb fall damage and award a stomp speed boost. |

### Buildable Mountain Park (`Hammer` -> `Ullr's Peak` tab, no Workbench needed)
1. **Ullr's Frost-Rune Kicker Ramp** (`8 Wood`) — Curved parabolic launch ramp with frost boost runes.
2. **Ullr's Slalom Race Gate** (`4 Wood`) — Press **`[E]`** to cycle **Start**, **Checkpoint**, and **Finish**. Crossing a Finish Gate broadcasts your official run time and peak `km/h` to everyone on the server.
3. **Ullr's Runic Rope Tow** (`10 Wood` + `2 LeatherScraps`) — Place two or more pylons uphill (up to `85m` per span, chainable up entire 400m+ peaks) and press **`[E]`** on a pylon or frost cable to ride back to the summit.

## Building from Source

Requires the **.NET SDK** (`net48` target via `Microsoft.NETFramework.ReferenceAssemblies`) and a local Valheim install with **BepInEx 5.4.2333+** and **Jötunn 2.28.0+**:

```bash
./build_and_deploy.sh
```

Produces `dist/JimmyLab-UllrSkiAndSnowboard-1.0.0.zip` ready for Thunderstore / r2modman / Gale. Override `VALHEIM_DIR=/path/to/Valheim` if Steam is installed in a non-default path.

---

> This mod was built by [Google Gemini 4 Carbon](https://deepmind.google/models/model-cards/gemini-4-carbon/) (High) via [Zed Editor](https://zed.dev/) + [Antigravity ACP](https://zed.dev/acp/agent/antigravity-acp) on my MacBook Pro during work meetings. I will do my best to keep up to date but make no promises :)
