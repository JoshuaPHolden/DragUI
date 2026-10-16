# VBloodHunter

A client-side V Rising mod for hunting V Bloods. You pick the bosses you want, and an on-screen tracker shows whether each one is up, how far away it is, and when it respawns. One click switches which V Blood is tracked.

Client only: nothing is needed on the server. It works on any server, vanilla or modded.

## Features

- **V Blood list.** Click the **V Blood Hunt** header on screen, or bind a hotkey, to open a list of all 64 V Bloods. Each row shows the region, act, live status and a **Track** button. Tick a row to add that boss to your hunt.
- **Hunt tracker.** A compact on-screen widget for the V Bloods you're hunting. Each row shows:
  - `612m NE`: alive, with distance and direction from you
  - `4:37`: dead, with an exact respawn countdown
  - `~4:37`: dead, with an estimated countdown
  - `Due`: the countdown has passed and the boss hasn't been reported alive yet
  - `Not listed`: the server didn't report it (dead before we saw it, or not trackable for your character yet)
- **Track buttons** in both the tracker and the list. A Track button sends the same request as the game's own V Blood menu, so the normal tracking trail/marker switches to that boss. Click **Tracked** to stop tracking.
- Drag the tracker by its header and the list by its title bar. Positions are saved. The **-** button collapses the tracker.
- Clicking the mod's buttons doesn't make your character attack.

## How respawn tracking works

A client can't simply read every boss's timer, so the mod combines what the game already sends a normal client:

1. **Kills near you.** When a V Blood near you dies, the mod sees it die and reads the game's own time of death, so the countdown starts the moment you kill it.
2. **Ghosts (exact timers).** A dead V Blood leaves a ghost whose buff carries the exact time it respawns. Near a ghost, the countdown becomes **exact** (no `~`).
3. **Kill announcements in chat.** If the server announces V Blood kills in chat (kill-feed or announcement mods), the countdown starts when the message arrives, even if the kill was across the map. A message counts when it names exactly one V Blood (full name, short name like "Sir Magnus", or a nickname like "Quincy") and contains a kill word such as *slain*, *killed* or *defeated*. By default only server **System** messages are read, so a player typing "anyone killed alpha?" doesn't start a timer. "Primal ..." bosses (Bloodcraft) don't count.
4. **The server's V Blood list.** Every few seconds the mod sends the same `GetVBloodPositionsEvent` the game's V Blood menu sends. If a boss drops off that list, it died somewhere else, and an estimated countdown starts. The first time you kill a boss, the mod checks whether the server keeps dead bosses on the list, and logs the answer (`Calibration: ...`).
5. **Learning.** When the mod sees a boss die and later come back, it remembers how long that took (`BepInEx/config/VBloodHunter/respawn-times.txt`). Until then it estimates from a per-boss default from community data, or from `FallbackRespawnSeconds`.

All countdowns run on one local clock; server times are converted once, and invalid values are ignored.

Limits:
- A boss that was already dead when you logged in shows **Not listed** (or alive, if the server lists dead bosses) until you get near its ghost or it respawns.
- If the server keeps dead bosses on its list and doesn't announce kills in chat, only bosses that die near you get timers.
- Custom world bosses from mods like BloodyBoss reuse V Blood names, so their kill messages also start that V Blood's timer. Turn off `ReadChatKills` if that's misleading on your server.

## Install

1. Install [BepInExPack V Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) on the **client**.
2. Copy `VBloodHunter.dll` into `VRising/BepInEx/plugins`.

## Settings

`BepInEx/config/nezoic.VBloodHunter.cfg`:

| Setting | Default | |
|---|---|---|
| `General.ToggleListKey` | `None` | Optional hotkey for the list (e.g. `F8`). Avoid keys you type in chat. |
| `General.UIScale` | `1` | Size of the tracker and list (0.5 – 2.5). |
| `General.BlockClicksOverUI` | `true` | Stop clicks on the mod's buttons from firing attacks/casts. |
| `Tracking.PollSeconds` | `5` | How often to ask which V Bloods are alive (min 2). |
| `Tracking.FallbackRespawnSeconds` | `600` | Estimate used for a boss with no default and no measured time. |
| `Tracking.LearnRespawnTimes` | `true` | Remember measured respawn times. |
| `Tracking.AutoTrackNext` | `false` | When nothing is tracked, track the nearest alive boss on your hunt list. |
| `Chat.ReadChatKills` | `true` | Start countdowns from V Blood kill announcements in chat. |
| `Chat.ChatKillChannels` | `System` | Channels to read announcements from: `System`, `Global`, `Region`, `Team`, `Local`, `Lore`. |
| `Chat.ChatKillWords` | `killed,slain,defeated,...` | Words that mark a message as a kill announcement. |
| `Debug.Diagnostics` | `false` | Verbose logging of what the mod sees. Turn on when reporting a problem. |

Your hunt list is saved in `BepInEx/config/VBloodHunter/hunts.txt`.

## Building

```
dotnet build -c Release                         # bin/Release/net6.0/VBloodHunter.dll
dotnet build -c Release -p:DeployToClient=true  # also copies it into the r2modman Default profile
```
Deploy goes to `%APPDATA%\r2modmanPlus-local\VRising\profiles\<R2ModmanProfile>\BepInEx\plugins\VBloodHunter`. Pick another profile with `-p:R2ModmanProfile=Name`, or any BepInEx install with `-p:ClientPluginDirectory=...`. When the game is launched from r2modman, only the profile's plugins load, not the ones in the Steam folder.

## In-game test checklist

Set `Debug.Diagnostics = true` for the first runs. The log is `BepInEx/LogOutput.log`.

1. **Load.** Log in. About 2s after spawning, the **V Blood Hunt** header appears top-left, and the log shows `Client world bound` and `UI built`.
2. **Server data.** Within ~5s the log shows `V Blood position response #1: N listed`. Note **N**, and whether bosses above your level are listed. That answers the open question under "Limits".
3. **List.** Open the list, tick a few bosses, and close it. The tracker shows them. Restart the game and they're still there.
4. **Direction.** Stand still and compare an alive boss's `m`/compass reading with the map. If the compass is mirrored, report it; it assumes +Z is north.
5. **Track.** Click **Track** on a boss. The game's tracking trail appears, the button turns red **Tracked**, and the log shows `Now tracking ...`. Click another boss's Track to switch. Click **Tracked** to stop.
6. **Kill.** Kill a hunted boss. It goes to `~m:ss` within one poll. Stand near its ghost: the `~` disappears (an exact timer), and the log shows a `Ghost buff ...` line naming the boss. If it says `unmatched`, copy that line into a bug report.
7. **Respawn.** Wait for the respawn. The status goes back to alive, and the log shows `Learned respawn for ...`.
8. **Clicks.** Click tracker/list buttons with enemies around. Your character must not attack, and must not keep auto-attacking afterwards. Keyboard spells with the cursor resting on the tracker must still cast.
9. **Logout / server switch.** Leave the game. The UI disappears in the main menu, with no crash. Join again (or join another server) and the UI comes back with fresh statuses.

## Credits

- The click-through protection is adapted from [Raphael](https://github.com/KDavidP1987/Raphael-Lord-of-Wisdom) by KDavidP1987 (MIT). So are the lessons about safe world teardown, which also come from Raphael's notes.
- The client-world hooks follow [Eclipse](https://github.com/mfoltz/Eclipse) by zfolmt. No Eclipse code is included.
- V Blood prefab data: [decaprime/vrising-modding](https://github.com/decaprime/vrising-modding).

## License

MIT. See [LICENSE](LICENSE).
