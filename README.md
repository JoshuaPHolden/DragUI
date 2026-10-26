# DragUI

*Formerly **VBloodHunter**. It started as a V Blood hunt tracker and grew into a general utility UI. Updating from VBloodHunter: remove the old `VBloodHunter` plugin folder. Your settings (`nezoic.VBloodHunter.cfg`) and data folder (`BepInEx/config/VBloodHunter`) are carried over automatically on the first launch.*

A client-side V Rising mod for hunting V Bloods. You pick the bosses you want, and an on-screen tracker shows whether each one is up, how far away it is, and when it respawns. One click switches which V Blood is tracked.

Client only: nothing is needed on the server. It works on any server, vanilla or modded. The optional **Familiars** window is the one exception: it needs a server running [Bloodcraft](https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/) with familiars enabled.

## Features

- **Control bar.** A small bar with one button per feature: **Tracker** shows or hides the hunt tracker, **Familiars** opens the familiar window, **Character** drops down a menu with **Stats** (the full character sheet), **CD Test** (the cooldown tester) and **Loadouts**, **DPS** shows your last fight's DPS and opens the meter, **XP …/h** shows your experience per hour and opens its breakdown, **Auto TP** turns auto-accepting teleport requests on or off, and **Alarm** turns the boss alarm on or off. A button is lit while its window is open or its feature is on. Drag the bar by its grip (the dots on the left). Drag the strip on its right edge (the dots on the right) to make it narrower or wider: the buttons wrap into rows to fill the width, so the bar can be a row, a column, a square or anything in between. Click the grip (without dragging) to switch between one row and one column.
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
- **Loadouts.** **Character → Loadouts** saves your build and puts it back on with one click. Type a name and click **Save current** to record your equipped gear (weapon, armor, amulet, cloak, bag), your spells (both spell slots and the ultimate), your spell school passives, and your Bloodcraft class and class shift spell. **Apply** equips the saved gear from your inventory, puts the spells back on the bar (as picking them in the spellbook does), swaps the passives, then sends Bloodcraft's `.class csp`. Anything it can't find in your inventory is listed. **Update** re-records a loadout from what you have on, after a prompt warning that it overwrites the saved one (saving under a name that's already taken asks the same), and **x** deletes it, also after a prompt. Applying uses the same requests as the game's inventory and spellbook menus, so the server checks each one as if you did it by hand. Bloodcraft keeps weapon and blood stats per weapon type and blood type, so they follow your gear by themselves. Changing an already chosen class costs items (`.class c`, 750 of an item by default), so a loadout with another class turns its **Apply** into **Confirm** first. The class and shift spell come from Bloodcraft's Eclipse data, so they're only recorded with Eclipse installed. Jewels aren't included. Saved per character in `BepInEx/config/DragUI/loadouts-<character>.json`.
- **Cooldown tester.** **CD Test** opens a window that times your skills. Use a weapon skill or spell and its row shows:
  - the unmodified **base** cooldown and the cooldown the **game** applied;
  - the **measured** time until it's ready again (last, and the average of the last 10), plus a live countdown.
  - From base and measured it works out the bonus the result implies under both possible formulas: **as rate** (`base ÷ (1 + bonus)`) and **as reduction** (`base × (1 − bonus)`). Compare those with the weapon/spell cooldown stat your client shows in the header, and the closer one is how the stat really works.
  - A value in **green** means the server applies more bonus than your client shows, e.g. Bloodcraft/PerkShop bonuses beyond the 40% cap.
  - A `*` marks a cooldown that changed while running (reset or shortened).
  - Tracking runs only while the window is open.
- **Character sheet.** Every combat stat with its value and where it comes from: each piece of gear, set bonus, blood, potion and buff, plus **Bloodcraft weapon expertise and blood legacy bonuses**. Those are recomputed from the data Bloodcraft sends the Eclipse client (`cap × class synergy × (1 + prestige multiplier × prestige) × level / max level`), because the server's real values never reach your client. **Base / unlisted** is what the listed sources don't explain (base stats, gear level). When the game's own number differs (it doesn't know Bloodcraft's bonuses), the sheet shows it as `game: …`. The header estimates your average hit power including crits. Requires Eclipse for the Bloodcraft part. **PerkShop stat perks** (bought with `.perk statbuy`, or admin-granted) are added too: when the sheet opens, the mod sends `.perk status`, hides the reply and reads your perks from it. **Refresh perks** re-reads them after you buy one. Like Bloodcraft's, these live in a buff whose real values never reach your client (PerkShop's default carrier, `SetBonus_ShieldPowerAndHealingReceived_T08`). If a server configures a different carrier, its client copy isn't swapped out. Perk *buffs* (potions, elixirs, blood buffs, relics) are ordinary game buffs and are listed like any other. Stats a server changes without telling the client can't be shown; the DPS meter still measures them.
- **Experience per hour (Bloodcraft).** The **XP** button on the control bar shows your current XP/hour. Click it for the breakdown: one row per prestige level (`Prestige 4`, or `Exo 2` after max prestige) with active time and XP/hour from **quests**, **mobs** and **V Bloods**, plus the total. A prestige or exo starts a new row, and older rows are kept. It reads Bloodcraft's leveling log (`+123 experience (45%)`), so the log must be on: use **Toggle .lvl log** in the window (it shows whether the log is on). Those lines are hidden from chat (`Experience.HideXpMessages`). Classification: a gain arriving with Bloodcraft's *"You've been awarded …% of your total experience"* is a quest; the biggest gain within a few seconds of a V Blood dying near you is that V Blood; everything else is mobs. Time only counts while you're gaining XP, so a break longer than 5 minutes (`Experience.IdleMinutes`) doesn't lower the rate. The first time, the mod asks Bloodcraft for your levels (`.lvl get`, `.prestige get exo`) with the replies hidden. After that it follows prestige/exo messages; **Sync levels** asks again. **Reset row** clears the current row. Saved per character in `BepInEx/config/DragUI/xp-<character>.json`. Bloodcraft sends no XP lines at max level, so nothing is counted there.
- **Auto-accept teleport requests (ScarletTeleports).** With **Auto TP on**, when someone sends you a teleport request (`.stp tpr <you>`), the mod answers `.stp tpa <name>` for them right away. If the accept fails because they're in combat (or you're in Dracula's room or a no-teleport zone), it retries every 3 seconds until it works or the request expires. Nothing else is retried. Each request gets its own immediate send rather than a shared, rate-limited queue, which is what made BloodCraftEZLife's version stop working over time. To limit it to friends, list their character names in `AutoTeleport.OnlyFrom` (comma-separated); otherwise anyone who requests is pulled to you, wherever you are.
- **Boss alarm (server-specific).** Click **Alarm** on the control bar to turn it on. While it's on, the button counts down to the next scheduled boss (`Alarm 42m`). **2 minutes before** each boss spawns (`BossAlarm.LeadMinutes`), giving you time to get out of combat, a soft 4-second chime plays. A **TP *boss* 1:59** button then appears on the bar, counting down to the spawn, and sends that boss's teleport command (e.g. `.stp tp apex`, ScarletTeleports). It flashes until the spawn (`BossAlarm.FlashMinutes`) and stays until 15 minutes after it (`ButtonMinutes`). If you log in during that window, the button shows without the chime. Turning the alarm on plays the chime once so you can set the volume. The schedule is `BepInEx/config/DragUI/boss-schedule.txt`, one boss per line as `time | label | command` (e.g. `23:00 | Apex Judgement | .stp tp apex`). It's written on first run with one server's Apex/Prime rotation; edit it for your server. Times are in your PC's time zone unless `BossAlarm.TimeZone` says otherwise. The chime plays through Windows, not the game's audio, so the game's volume sliders don't affect it.
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

1. Install [BepInExPack V Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) on the **client**.
2. Copy `DragUI.dll` into `VRising/BepInEx/plugins`.

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
| `AutoTeleport.Enabled` | `false` | Auto-accept teleport requests (the control bar's **Auto TP** button). |
| `AutoTeleport.OnlyFrom` | *(empty)* | Only auto-accept these players (comma-separated). Empty = anyone. |
| `BossAlarm.Enabled` | `false` | Boss alarm on/off (the control bar's **Alarm** button). |
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

## Building

```
dotnet build -c Release                         # bin/Release/net6.0/DragUI.dll
dotnet build -c Release -p:DeployToClient=true  # also copies it into the r2modman Default profile
```
Deploy goes to `%APPDATA%\r2modmanPlus-local\VRising\profiles\<R2ModmanProfile>\BepInEx\plugins\DragUI`. Pick another profile with `-p:R2ModmanProfile=Name`, or any BepInEx install with `-p:ClientPluginDirectory=...`. When the game is launched from r2modman, only the profile's plugins load, not the ones in the Steam folder.

## In-game test checklist

Set `Debug.Diagnostics = true` for the first runs. The log is `BepInEx/LogOutput.log`.

1. **Load.** Log in. About 2s after spawning, the control bar and the **V Blood Hunt** header appear top-left, and the log shows `Client world bound` and `UI built`.
2. **Server data.** Within ~5s the log shows `V Blood position response #1: N listed`. Note **N**, and whether bosses above your level are listed. That answers the open question under "Limits".
3. **List.** Open the list, tick a few bosses, and close it. The tracker shows them. Restart the game and they're still there.
4. **Direction.** Stand still and compare an alive boss's `m`/compass reading with the map. If the compass is mirrored, report it; it assumes +Z is north.
5. **Track.** Click **Track** on a boss. The game's tracking trail appears, the button turns red **Tracked**, and the log shows `Now tracking ...`. Click another boss's Track to switch. Click **Tracked** to stop.
6. **Kill.** Kill a hunted boss. It goes to `~m:ss` within one poll. Stand near its ghost: the `~` disappears (an exact timer), and the log shows a `Ghost buff ...` line naming the boss. If it says `unmatched`, copy that line into a bug report.
7. **Respawn.** Wait for the respawn. The status goes back to alive, and the log shows `Learned respawn for ...`.
8. **Clicks.** Click tracker/list buttons with enemies around. Your character must not attack, and must not keep auto-attacking afterwards. Keyboard spells with the cursor resting on the tracker must still cast.
9. **Control bar.** Each button opens and closes its window and is lit while the window is open; closing a window with its **X** unlights the button. **Tracker** hides and shows the tracker, and that survives a restart. Drag the bar by its grip; the position is saved. Click the grip: the bar becomes one column; click again: one row. Drag the right-edge strip left: the buttons wrap into more rows, each row filling the width; drag right past one row's width: back to one row. Restart: the shape is kept.
10. **Familiars** (Bloodcraft server). Click the text box and type a name containing W, A, S, D, Q, E, R and numbers. Your character must not move, cast or open menus, and the log shows `Typing lock engaged` once. Press Enter: Bloodcraft replies in chat, the familiar is bound and called out, the box clears, and the name appears under **Saved**. Press Enter again (box empty or not focused): chat must not open on the same keypress. Click a saved name to summon it. The **Bound** line shows it as out (green). Click it again: it's dismissed with `.fam t` (amber), and once more brings it back, even in combat. Dismiss it, then summon a different saved familiar: the mod calls the first one back, then binds the new one, with no `Can't unbind ... (dismissed)` error. Relog and summon the bound familiar: the first press rebinds it, and later presses toggle. Click **x** to remove it. Click a name's **+ key**, press F6: the button shows `F6`, and pressing F6 with the window closed summons that familiar. Bind Ctrl+F7 to another one: F7 alone does nothing. Typing F6 in chat must not summon. Restart: the binds are still there. Click **Call / Dismiss**. Press Escape while typing: the box lets go of the keyboard and your keys work again.
11. **Boss alarm.** Click **Alarm**: the chime plays once and the button shows `Alarm …m`. To test without waiting, add a line four minutes from now to `boss-schedule.txt` (e.g. `15:44 | Test | .stp tp apex`) and restart the game. Two minutes before that time the chime plays, the log shows `Boss alarm: Test spawns at 15:44`, and a flashing **TP Test 1:59** button appears and counts down. At the spawn time it stops flashing, and it disappears 15 minutes later. Clicking it teleports you. Click **Alarm** again to turn it off: the button text changes to `Alarm off` and the TP button goes away.
12. **Experience** (Bloodcraft). Open **XP** and click **Toggle .lvl log** until it says *on*. Kill a few mobs: no `+XP` lines in chat, and after a minute the bar shows `XP …/h` and the **Mobs/h** column fills in. The row is labelled with your prestige (from the hidden `.lvl get`). Kill a V Blood: its XP lands in **V Blood/h** (log with Diagnostics: `XP +… (VBlood)`). Hand in a quest: **Quests/h**. Prestige: a new row starts at zero and the old one stays below it. Restart the game: the rows are still there.
13. **DPS meter.** Open **DPS** and hit a training dummy or a mob. The header says *Fighting* with a live DPS, and your abilities appear in the table. Stop for 6 seconds: the fight appears under *Recent fights* and the log shows `DPS: fight vs …`. If the table stays empty while you hit things, set `Diagnostics = true` and send the `DPS:` lines from the log.
14. **Character sheet.** On a PerkShop server the header says *PerkShop: N stat perk(s) included*, and each perk shows as a *PerkShop key xN* line under its stat. No `.perk status` text should appear in chat. Open **Character → Stats**. Within a few seconds the header turns green (*Bloodcraft bonuses: included*). Physical Power lists your weapon's EquipBuff and a *Bloodcraft … expertise* line matching `.wep get`. Change weapon or blood: the Bloodcraft lines follow. Compare a stat with the game's character sheet: where they differ, `game: …` shows the game's number.
15. **Auto TP** (ScarletTeleports). Click **Auto TP** to turn it *on*. Have a friend send `.stp tpr <you>`: they're pulled to you within a second, and chat shows *Player … has teleported to you*. Have them request while in combat: they arrive once they leave combat, if that's within the request's time. Turn it *off*: the request just sits there as normal. Leave it on for a long session: it keeps working (EZLife's stopped).
16. **Cooldown tester.** Open **Character → CD Test** and use a weapon skill: its row appears with *Base* and *Game* filled in and *Left* counting down. When it's ready, *Last* shows the measured time. Use it a few times for an average. With the 40%-capped weapon cooldown, a green *As rate* above 40% means server-side bonuses go past the cap.
17. **Loadouts.** Set `Diagnostics = true`. Open **Character → Loadouts**, type *Test A* and click **Save current**: the row shows your class, weapon and counts, and the log shows `Loadout 'Test A' recorded:` with your gear and passives. Check the passives match the spellbook; if they don't, send that log line. Swap a few armor pieces, the weapon and a passive, then save *Test B*. Click **Apply** on *Test A*: within a couple of seconds everything goes back, and the status says *Applied Test A.* Put one of A's items in a chest and apply A again: the status lists it as not in your inventory. With a different class saved in a loadout, **Apply** turns into **Confirm** with the cost warning, and going back to the list without clicking resets it after 6 seconds. Relog: the loadouts are still there.
18. **List All** (Bloodcraft, with familiars in two or more boxes). Note your active box (`.fam l`). Click **List All**: the button says *Listing...*, the status counts through the boxes, no `.fam` replies appear in chat, and the list shows every familiar with its box. `.fam l` afterwards shows the same active box as before. Click **Save** on one: it shows *Saved* and appears under **Saved**. Click **Delete** on a throwaway familiar: it turns into **Sure?**; wait 4 seconds and it resets. Click it twice: the row disappears, the status says *Deleted … from …*, and `.fam l` in that box no longer shows it.
19. **Logout / server switch.** Leave the game. The UI disappears in the main menu, with no crash. Join again (or join another server) and the UI comes back with fresh statuses.

## Credits

- The click-through protection and the typing lock are adapted from [Raphael](https://github.com/KDavidP1987/Raphael-Lord-of-Wisdom) by KDavidP1987 (MIT). So are the lessons about safe world teardown, which also come from Raphael's notes.
- The Bloodcraft stat recomputation reads the data format of [Eclipse](https://github.com/mfoltz/Eclipse)'s server protocol (zfolmt, CC BY-NC 4.0); no Eclipse code is included.
- The client-world hooks follow [Eclipse](https://github.com/mfoltz/Eclipse) by zfolmt. No Eclipse code is included.
- V Blood prefab data: [decaprime/vrising-modding](https://github.com/decaprime/vrising-modding).

## License

MIT. See [LICENSE](LICENSE).
