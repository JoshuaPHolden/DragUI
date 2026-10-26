# DragUI

An all-in-one utility UI for V Rising. One small control bar, which you can drag anywhere on screen, opens every tool:

- **V Blood hunt tracker:** whether each boss you're hunting is up, how far away and in which direction, and an exact or estimated respawn countdown. One click switches the game's tracking trail.
- **Loadouts:** save your gear, spells, spell school passives and Bloodcraft class, and swap the whole build back on with one click.
- **DPS meter:** real damage after every modifier, per ability, with your recent fights to compare builds.
- **Character sheet:** every combat stat and exactly where it comes from, including the Bloodcraft and PerkShop bonuses the game's own sheet can't see.
- **Cooldown tester:** measures your real cooldowns and shows how your cooldown stat actually applies.
- **Familiars:** summon by name or hotkey, call/dismiss, and see every familiar from every box in one list.
- **Experience per hour:** XP/hour on the bar, split into quests, mobs and V Bloods per prestige level.
- **Teleports:** auto-accept or accept/deny teleport requests, one-click and hotkey teleports, and a pop-out teleport window to keep on screen.
- **Boss alarm:** a chime and a one-click teleport notice two minutes before each scheduled world boss spawns.

It's a **client** mod: install it on your PC, nothing goes on the server. The core tools work on any server, vanilla or modded. Some tools drive server mods through their chat commands, so they only work where the server runs those mods:

| Tool | Works on | Needs on the server |
|---|---|---|
| Hunt tracker, DPS meter, cooldown tester | any server | nothing |
| Character sheet | any server | Bloodcraft (+ Eclipse on your client) for the Bloodcraft bonuses; PerkShop for perk stats |
| Loadouts | any server | Bloodcraft (+ Eclipse on your client) for the class part only |
| Familiars, Experience per hour | Bloodcraft servers | [Bloodcraft](https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/) (familiars enabled for Familiars) |
| Teleports | ScarletTeleports servers | ScarletTeleports |
| Boss alarm | any server with a boss schedule | ships with Noctveil's schedule (editable); its teleport button uses ScarletTeleports |

## Features

- **Control bar.** A small bar with one button per feature: **Tracker** shows or hides the hunt tracker, **Familiars** opens the familiar window, **Character** drops down a menu with **Stats** (the full character sheet), **CD Test** (the cooldown tester) and **Loadouts**, **DPS** shows your last fight's DPS and opens the meter, **XP …/h** shows your experience per hour and opens its breakdown, **Teleport** opens the teleport window (it shows how many requests are waiting, e.g. *Teleport (1)*), and **Server Settings** drops down a menu of server-specific settings (**Noctveil**). When a feature has something for you (like a boss alarm's teleport), it shows in a **notification slot** at the end of the bar. The newest notice replaces the one before, and clicking it does what it says (or dismisses it). A button is lit while its window is open or its feature is on. Drag the bar by its grip (the dots on the left). Drag the strip on its right edge (marked with a ◀|▶ resize icon) to make it narrower or wider: the buttons wrap into rows to fill the width, so the bar can be a row, a column, a square or anything in between. Click the grip (without dragging) to switch between one row and one column.
- **V Blood list.** Click the gear on the hunt tracker, or bind a hotkey, to open a list of all 64 V Bloods. Each row shows the region, act, live status and a **Track** button. Tick a row to add that boss to your hunt.
- **Hunt tracker.** A compact on-screen widget for the V Bloods you're hunting. Each row shows:
  - `612m NE`: alive, with distance and direction from you
  - `4:37`: dead, with an exact respawn countdown
  - `~4:37`: dead, with an estimated countdown
  - `Due`: the countdown has passed and the boss hasn't been reported alive yet
  - `Not listed`: the server didn't report it (dead before we saw it, or not trackable for your character yet)
- **Track buttons** in both the tracker and the list. A Track button sends the same request as the game's own V Blood menu, so the normal tracking trail/marker switches to that boss. Click **Tracked** to stop tracking.
- **Familiars (Bloodcraft).** Type a familiar's name and press Enter or **Summon** to bind and call it out. Partial names work (`alpha`). If the familiar you summon is **already bound**, the mod sends `.fam t` instead, which calls it out or dismisses it. That's quicker and works in combat. If a *different* familiar is bound but dismissed (Bloodcraft won't swap a dismissed familiar), it's called back first and the new one is bound right after. The mod learns what's bound from Bloodcraft's replies (`… bound!`, `Familiar disabled!`, ...). The window shows it on the **Bound** line and colours that familiar's saved name: green when it's out, amber when it's dismissed. Right after a login the state is unknown, so the first summon always rebinds. Binding a different familiar still can't happen in combat (Bloodcraft's rule), and `.fam sb` unbinds your current familiar before it finds out you're in combat, so swap before the fight. Every name you summon this way is saved as a one-click button; **x** removes one. Give a saved familiar a **hotkey** by clicking its key button (**+ key**) and pressing a key, optionally with Ctrl/Alt/Shift. From then on that key summons it, even with the window closed. Esc cancels, Backspace clears the bind, and a key already used by another familiar moves to the new one. The mod doesn't stop the game from also seeing the key, so pick keys the game doesn't use (F5–F12, numpad, or Ctrl/Alt+key). Hotkeys don't fire while you're typing in chat. **Call / Dismiss** calls out or dismisses your bound familiar. The buttons send Bloodcraft's own commands for you (`.fam sb "name"` and `.fam t`), so Bloodcraft's replies appear in chat as usual. A name that matches familiars in more than one box won't bind (Bloodcraft's limit), so use a more specific name.
- **All familiars (Bloodcraft).** Click **List All** in the Familiars window to see every familiar you've unlocked, from every box, in one scrollable list with its level, prestige, box and a gold `*` if it's shiny. The mod reads each box with Bloodcraft's `.fam boxes`, `.fam cb` and `.fam l`, hides those replies from chat, and selects your previous box again at the end. **Save** adds a familiar to the quick summon list. **Delete** removes it from its box permanently (`.fam r`), so the first click asks **Sure?**. Before deleting, the mod lists that box again and only removes the familiar if it's still there, so a stale list can't delete the wrong one. **Saved** switches back to the quick summon list.
- **DPS meter.** Counts the damage numbers the server sends you for your own hits on creatures (plus your minions' and familiar's; trees, rocks and buildings are ignored), so it measures real damage after every modifier: gear, Bloodcraft, procs, other mods. A fight starts with your first hit and ends 6 seconds after your last (`Dps.FightTimeoutSeconds`). The window shows the fight's duration, DPS, total damage and main target, then a per-ability table: damage, DPS, hits, crit % and share. Below that are your last fights; click one to view it, or **Live** to go back. Use it to compare builds on the same target.
- **Loadouts.** **Character → Loadouts** saves your build and puts it back on with one click. Type a name and click **Save current** to record your equipped gear (weapon, armor, amulet, cloak, bag), your spells (both spell slots and the ultimate), your spell school passives, and your Bloodcraft class and class shift spell. **Apply** equips the saved gear from your inventory, puts the spells back on the bar (as picking them in the spellbook does), swaps the passives, then sends Bloodcraft's `.class csp`. Anything it can't find in your inventory is listed. **Update** re-records a loadout from what you have on, after a prompt warning that it overwrites the saved one (saving under a name that's already taken asks the same), and **x** deletes it, also after a prompt. Applying uses the same requests as the game's inventory and spellbook menus, so the server checks each one as if you did it by hand. Bloodcraft keeps weapon and blood stats per weapon type and blood type, so they follow your gear by themselves. While it applies, a progress bar shows each step (class, gear, spells, passives) and fills to the end. Applying a loadout with another class changes it right away with `.class c`, which costs items (750 of an item by default), so save class-switching loadouts with that in mind. The class and shift spell come from Bloodcraft's Eclipse data, so they're only recorded with Eclipse installed. Jewels aren't included. Saved per character in `BepInEx/config/DragUI/loadouts-<character>.json`.
- **Cooldown tester.** **CD Test** opens a window that times your skills. Use a weapon skill or spell and its row shows:
  - the unmodified **base** cooldown and the cooldown the **game** applied;
  - the **measured** time until it's ready again (last, and the average of the last 10), plus a live countdown.
  - From base and measured it works out the bonus the result implies under both possible formulas: **as rate** (`base ÷ (1 + bonus)`) and **as reduction** (`base × (1 − bonus)`). Compare those with the weapon/spell cooldown stat your client shows in the header, and the closer one is how the stat really works.
  - A value in **green** means the server applies more bonus than your client shows, e.g. Bloodcraft/PerkShop bonuses beyond the 40% cap.
  - A `*` marks a cooldown that changed while running (reset or shortened).
  - Tracking runs only while the window is open.
- **Character sheet.** Every combat stat with its value and where it comes from: each piece of gear, set bonus, blood, potion and buff, plus **Bloodcraft weapon expertise and blood legacy bonuses**. Those are recomputed from the data Bloodcraft sends the Eclipse client (`cap × class synergy × (1 + prestige multiplier × prestige) × level / max level`), because the server's real values never reach your client. **Base / unlisted** is what the listed sources don't explain (base stats, gear level). When the game's own number differs (it doesn't know Bloodcraft's bonuses), the sheet shows it as `game: …`. The header estimates your average hit power including crits. Requires Eclipse for the Bloodcraft part. **PerkShop stat perks** (bought with `.perk statbuy`, or admin-granted) are added too: when the sheet opens, the mod sends `.perk status`, hides the reply and reads your perks from it. **Refresh perks** re-reads them after you buy one. Like Bloodcraft's, these live in a buff whose real values never reach your client (PerkShop's default carrier, `SetBonus_ShieldPowerAndHealingReceived_T08`). If a server configures a different carrier, its client copy isn't swapped out. Perk *buffs* (potions, elixirs, blood buffs, relics) are ordinary game buffs and are listed like any other. Stats a server changes without telling the client can't be shown; the DPS meter still measures them.
- **Experience per hour (Bloodcraft).** The **XP** button on the control bar shows your current XP/hour. Click it for the breakdown: one row per prestige level (`Prestige 4`, or `Exo 2` after max prestige) with active time and XP/hour from **quests**, **mobs** and **V Bloods**, plus the total. A prestige or exo starts a new row, and older rows are kept. It reads Bloodcraft's leveling log (`+123 experience (45%)`), so the log must be on: use **Toggle .lvl log** in the window (it shows whether the log is on). Those lines are hidden from chat (`Experience.HideXpMessages`). Classification: a gain arriving with Bloodcraft's *"You've been awarded …% of your total experience"* is a quest; the biggest gain within a few seconds of a V Blood dying near you is that V Blood; everything else is mobs. Time only counts while you're gaining XP, so a break longer than 5 minutes (`Experience.IdleMinutes`) doesn't lower the rate. The first time, the mod asks Bloodcraft for your levels (`.lvl get`, `.prestige get exo`) with the replies hidden. After that it follows prestige/exo messages; **Sync levels** asks again. **Reset row** clears the current row. Saved per character in `BepInEx/config/DragUI/xp-<character>.json`. Bloodcraft sends no XP lines at max level, so nothing is counted there.
- **Teleports (ScarletTeleports).** **Teleport** on the control bar opens a window with three parts. **Auto-accept requests**: with it **On**, when someone sends you a teleport request (`.stp tpr <you>`), the mod sends `.stp tpa <name>` straight away; if they're in combat (or you're in Dracula's room or a no-teleport zone), it retries every few seconds until the request expires. `AutoTeleport.OnlyFrom` limits it to certain players. **Pending requests**: every request waiting for an answer, with a countdown and **Accept** / **Deny**, for when auto-accept is off or couldn't take it; those requests also show in the control bar's notification slot (*Accept TP: Name 25s*, click to accept). **My teleports**: your personal teleports and the server's, read with `.stp ltp` (the replies are hidden from chat). Each has **Go** (`.stp tp`) and a hotkey button (click it, press a key; Esc cancels, Backspace clears). A key that a familiar or the V Blood list already uses isn't taken: the window says what has it. Personal teleports also have **Here**, which moves it to where you stand (`.stp rtp` then `.stp stp`, keeping its name and hotkey), and **x** to remove it, both after a prompt. Empty slots up to your limit take a name and **Set here**. The server only tells you its limit when you reach it, so until then the window shows ScarletTeleports' default of 3 (plus one to try if you have 3); once the server says, the real limit is remembered. **Pop out** (next to *Refresh*) opens your teleports as a small window you can keep open while you play: one button per teleport (yours, then the server's) with its hotkey, click to teleport. Drag it by its header; **x** closes it. It remembers where it was and whether it was open. Hotkeys and the limit are saved per character in `BepInEx/config/DragUI/teleports-<character>.json`.
- **Boss alarm (Noctveil).** Open **Server Settings → Noctveil** on the control bar and click **Off** to turn the Apex / Prime boss alarm **On**. The window shows the next boss and the schedule (next one highlighted); **Test sound** plays the chime. **2 minutes before** each boss spawns (`BossAlarm.LeadMinutes`), giving you time to get out of combat, a soft 4-second chime plays and a **TP *boss* 1:59** notice appears in the control bar's notification slot, counting down to the spawn. Clicking it sends that boss's teleport command (e.g. `.stp tp apex`, ScarletTeleports). It flashes until the spawn (`BossAlarm.FlashMinutes`) and stays until 15 minutes after it (`ButtonMinutes`), unless a newer notice replaces it. If you log in during that window, the notice shows without the chime. Turning the alarm on plays the chime once so you can set the volume. The schedule is `BepInEx/config/DragUI/boss-schedule.txt`, one boss per line as `time | label | command` (e.g. `23:00 | Apex Judgement | .stp tp apex`). It's written on first run with Noctveil's Apex/Prime rotation; edit it for your server. Times are in your PC's time zone unless `BossAlarm.TimeZone` says otherwise. The chime plays through Windows, not the game's audio, so the game's volume sliders don't affect it.
- Drag the tracker by its header and windows by their title bars. Positions are saved. Click the tracker's **V Blood Hunt** header (or its **-** button) to collapse or expand it.
- Clicking the mod's buttons doesn't make your character attack. Typing in the mod's text box doesn't move your character or cast spells.

## How respawn tracking works

A client can't simply read every boss's timer, so the mod combines what the game already sends a normal client:

1. **Kills near you.** When a V Blood near you dies, the mod sees it die and reads the game's own time of death, so the countdown starts the moment you kill it.
2. **Ghosts (exact timers).** A dead V Blood leaves a ghost whose buff carries the exact time it respawns. Near a ghost, the countdown becomes **exact** (no `~`).
3. **Kill announcements in chat.** If the server announces V Blood kills in chat (kill-feed or announcement mods), the countdown starts when the message arrives, even if the kill was across the map. A message counts when it names exactly one V Blood (full name, short name like "Sir Magnus", or a nickname like "Quincy") and contains a kill word such as *slain*, *killed*, *defeated* or *beating* (e.g. "Congratulation to PlayerName for beating Alpha the White Wolf"). By default only server **System** messages are read, so a player typing "anyone killed alpha?" doesn't start a timer. "Primal ..." bosses (Bloodcraft) don't count.
4. **The server's V Blood list.** Every few seconds the mod sends the same `GetVBloodPositionsEvent` the game's V Blood menu sends. If a boss drops off that list, it died somewhere else, and an estimated countdown starts. The first time you kill a boss, the mod checks whether the server keeps dead bosses on the list, and logs the answer (`Calibration: ...`).
5. **Learning.** When the mod sees a boss die and later come back, it remembers how long that took (`BepInEx/config/DragUI/respawn-times.txt`). Until then it estimates from a per-boss default from community data, or from `FallbackRespawnSeconds`.

All countdowns run on one local clock; server times are converted once, and invalid values are ignored.

Limits:
- A boss that was already dead when you logged in shows **Not listed** (or alive, if the server lists dead bosses) until you get near its ghost or it respawns.
- If the server keeps dead bosses on its list and doesn't announce kills in chat, only bosses that die near you get timers.
- Custom world bosses from mods like BloodyBoss reuse V Blood names, so their kill messages also start that V Blood's timer. Turn off `ReadChatKills` if that's misleading on your server.

## Install

DragUI goes on **your PC (the game client)**, not on the server.

- **Mod manager (recommended):** in r2modman or Thunderstore Mod Manager, pick V Rising, search for **DragUI** and install it. BepInEx is installed with it. Start the game from the mod manager.
- **By hand:** install [BepInExPack V Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) on the client, then copy `DragUI.dll` into `VRising/BepInEx/plugins`.

**Updating from VBloodHunter:** DragUI was formerly VBloodHunter. Remove the old `VBloodHunter` plugin folder. Your settings (`nezoic.VBloodHunter.cfg`) and data folder (`BepInEx/config/VBloodHunter`) are carried over automatically on the first launch.

## Settings

`BepInEx/config/nezoic.DragUI.cfg`:

| Setting | Default | |
|---|---|---|
| `General.ToggleListKey` | `None` | Optional hotkey for the list (e.g. `F8`). Avoid keys you type in chat. |
| `General.UIScale` | `1` | Size of the tracker and list (0.5 – 2.5). |
| `General.BlockClicksOverUI` | `true` | Stop clicks on the mod's buttons from firing attacks/casts. |
| `Layout.BarWidth` | `0` | Width of the control bar's button area (the buttons wrap to fit). `0` = one row; narrower than the widest button = one column. Set by dragging the bar's corner. |
| `General.LockKeysWhileTyping` | `true` | Block game keys while you type in the mod's text box (the same lock the game's chat uses). |
| `Character.Enabled` | `true` | Stats button and character sheet. |
| `Character.CooldownTester` | `true` | CD Test in the Character menu and the cooldown tester window. |
| `Character.Loadouts` | `true` | Loadouts in the Character menu. |
| `Dps.Enabled` | `true` | DPS button and meter. |
| `Dps.FightTimeoutSeconds` | `6` | Seconds after your last hit that a fight ends. |
| `Experience.Enabled` | `true` | XP/hour on the control bar and the Experience window. |
| `Experience.HideXpMessages` | `true` | Hide Bloodcraft's `+XP` chat lines. |
| `Experience.IdleMinutes` | `5` | Gaps without XP longer than this don't count as play time. |
| `AutoTeleport.Enabled` | `false` | Auto-accept teleport requests (the On/Off button in the **Teleport** window). |
| `AutoTeleport.OnlyFrom` | *(empty)* | Only auto-accept these players (comma-separated). Empty = anyone. |
| `BossAlarm.Enabled` | `false` | Boss alarm on/off (the On/Off button in **Server Settings → Noctveil**). |
| `BossAlarm.Volume` | `0.35` | Chime volume, 0 – 1. |
| `BossAlarm.LeadMinutes` | `2` | How many minutes before the spawn the alarm sounds and the button appears. |
| `BossAlarm.ButtonMinutes` | `15` | How long the teleport button stays after the spawn. |
| `BossAlarm.FlashMinutes` | `2` | How long the button flashes after the alarm. |
| `BossAlarm.TimeZone` | *(empty)* | Windows time zone id the schedule is in (e.g. `Eastern Standard Time`). Empty = this PC's. |
| `Familiars.Enabled` | `true` | Show the Familiars window and its control-bar button. Turn off on servers without Bloodcraft: there the commands would appear as local chat. |
| `Tracking.PollSeconds` | `5` | How often to ask which V Bloods are alive (min 2). |
| `Tracking.FallbackRespawnSeconds` | `600` | Estimate used for a boss with no default and no measured time. |
| `Tracking.LearnRespawnTimes` | `true` | Remember measured respawn times. |
| `Tracking.AutoTrackNext` | `false` | When nothing is tracked, track the nearest alive boss on your hunt list. |
| `Chat.ReadChatKills` | `true` | Start countdowns from V Blood kill announcements in chat. |
| `Chat.ChatKillChannels` | `System` | Channels to read announcements from: `System`, `Global`, `Region`, `Team`, `Local`, `Lore`. |
| `Chat.ExtraChatKillWords` | *(empty)* | Extra kill words on top of the built-in ones (killed, slain, defeated, beating, ...). |
| `Debug.Diagnostics` | `false` | Verbose logging of what the mod sees. Turn on when reporting a problem. |

Your hunt list is saved in `BepInEx/config/DragUI/hunts.txt`, and your saved familiar names and their hotkeys in `familiars.txt` next to it (one per line: the name, then a tab and the hotkey, such as `Ctrl+F5`).

Building from source and the in-game test checklist are in [DEVELOPMENT.md](https://github.com/JoshuaPHolden/DragUI/blob/master/DEVELOPMENT.md).

## Credits

- The click-through protection and the typing lock are adapted from [Raphael](https://github.com/KDavidP1987/Raphael-Lord-of-Wisdom) by KDavidP1987 (MIT). So are the lessons about safe world teardown, which also come from Raphael's notes.
- The Bloodcraft stat recomputation reads the data format of [Eclipse](https://github.com/mfoltz/Eclipse)'s server protocol (zfolmt, CC BY-NC 4.0); no Eclipse code is included.
- The client-world hooks follow [Eclipse](https://github.com/mfoltz/Eclipse) by zfolmt. No Eclipse code is included.
- V Blood prefab data: [decaprime/vrising-modding](https://github.com/decaprime/vrising-modding).

## License

MIT. See [LICENSE](LICENSE).
