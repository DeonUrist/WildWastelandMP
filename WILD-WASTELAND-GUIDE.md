# Inherited Wild Wasteland guide

This is the historical guide for the base mod and its earlier releases. For current WildWastelandMP installation and multiplayer controls, start with [README](README.md) and [the multiplayer guide](README-MULTIPLAYER.md). Base-mod version numbers and release history below are retained as reference.

A BepInEx 5 mod for **Apocalypter** that combines gameplay mods into one plugin: one load order, one set of Harmony patches, one save/scene lifecycle and one shared frame budget. Every former mod is a **module** with its own on/off switch; everything else about them (features, settings, saves, data files) stays as it was.

Version **0.1.36 RC33** (plugin GUID `com.denis.apocalypter.wildwasteland`). Status: in-game verification remains pending for features marked Testing on the project board.

| Module | What it is | Replaces (remove these from `BepInEx\plugins`) |
|---|---|---|
| Player body & animation (`PlayerBody`) | Your character's body, third-person camera, animations, climbing, equipment on the body, vehicle HUD and hotkeys | Apocaplayer 2.3.9, Apocaclimber |
| NPC AI & navigation (`NPC`) | NPCs that see, hear, search, take cover and pace their bursts; camp maps; idle life; burrowing / ambushing / pouncing creatures, dogs, spider nests, storms; female raiders and crew | NPC Overhaul (NPCAI) 1.9.15, WomenOfWasteland 1.0.0 |
| Guns, ammo & tracers (`Guns`) | Real bullets with tracers and range falloff, vehicle part damage, NPC aim and lead, melee on wheels and corpses, boss health, per-gun magazines, hold-to-unload, backpack / armor overflow | Gunplay 1.3.0, Apocaunloader |
| Combat HUD (`HUD`) | Crosshair styles, selection dot, hit marker, damage numbers, headshot sound | Gunplay HUD 1.2.2 |
| Clean HUD & menus (`CleanHUD`) | Vitals, weapon silhouettes and ammo, quick slots, strip compass, message log, title/pause menus and controls catalog | CleanHUD 1.5.1 |
| Patrols, traffic & War Rig (`Patrols`) | Raider cars, trucks and bikes with live crews, patrols and convoys, persistent world traffic with faction battles and Coyote merchants, vehicle templates and editors, the War Rig tanker trailer | Apocapatrol 2.14.4, War Rig 1.1.3, Merchant Object Spotter |
| Survival (`Survival`) | Stew pots for filling, drinking, cooking, spoilage and optional pouring | Stew Pot 0.1.1 |
| Performance (`Performance`) | Experimental vanilla runtime, rendering and world optimizations; separate F3 Performance page and presets | - |
| Points of interest (`POI`) | Generated War Rig encounter sites | - |
| Quests (`Quests`) | Native authored/generated jobs, factions, reputation, voices and hiring; Talk content optional | Apocalypter Quests 1.8.2 |
| Optional compatibility (`QuestsCompat`, `EngineSimCompat`) | Scheduling and integration for separately installed Apocatalker and Engine Sim | None; keep optional content/simulation mods installed |

With Apocasetter and its installer available, WW queues replaced standalones for disable and shows a title-menu restart notice. Restart before playing so the installer can move their files into its recoverable disabled cache; configs remain intact. WW waits for that restart before taking over the conflicting features. Without the installer, follow the notice to disable the named mods manually. This also covers Immersive Map, WomenOfWasteland, HealthStacks and iTweenGuard; optional Talk, Engine Sim, ChaseCamera and Apocapocket remain installed. Apocaraider (the old monolith) is not detected: remove it by hand.

RC33 adds steering-wheel hands with ten calibrated per-wheel defaults, repeated hand-over-hand turns, relaxed side grips on return, mirrored left/right setup and a live numpad editor. Tools hide the extra right first-person body arm while retaining the full third-person body. Apocalypter Quests 1.8.2 is now included: jobs, reputation, hiring and 76 supplied voice recordings, with CleanHUD dialogue, tracker, journal and notifications. Disable standalone Quests; Talk remains optional. Manual saves show SAVING and block menu navigation until completion; optional World Amp overheating/seizure warnings use the message log. Quests settings migrate individually; existing wheel files, content and saves are preserved. See [RC33 changes and test steps](notes/release-0.1.36-RC33.md).

RC32 includes RC31's features below, adds CleanHUD Tutorial/Codex panels retaining native content, and fits Codex pictures and Tutorial videos without scrolling. Hides the startup tutorial suggestion and blocks F3/F6 menus during loading, including autosave presentation. Vistas routine logs, PlayMaker handler diagnostics and cache-ready messages follow WW VerboseLog. War Rig markers use CleanHUD's diamond, and held weapon copies no longer cast flashlight shadows while retaining sunlight shadows. No config/save changes in this RC. The [release description](notes/release-0.1.35-RC32.md) retains RC31's player-facing changes since RC29, as requested.

RC31: prepares complete patrol and War Rig builds during loading and restores saved vehicles/crews before returning control, retaining damage, missing parts, cargo and trailer state. Corrects Coyote scout/patrol defaults. Adds the long accent-framed loading bar without module-list text, hides Recommended mods during loading, defaults to the game font with optional CleanFont in Debug, and restores inventory DROP including binoculars. Includes 41 navigation maps for all 39 vanilla POI types, persistent custom maps and cache tools, Game RESET, four Performance presets with Vanilla as the fresh default, and Performance and jobs at the bottom of Performance. Adds Vistas compatibility, fixes its activation, improves high-ground scouting and vehicle route selection, and attempts compatibility with newer optional-mod versions. Existing choices and custom assets remain intact. See [changes since RC29](notes/release-0.1.34-RC31.md).

RC30 (historical): introduced persistent NPC navigation maps, cache/export tools, the optional purple capture diagnostic, Game RESET, the four Performance preset labels and styled Recommended mods. RC31 now bundles the captured maps and defaults the purple diagnostic off. See [RC30 changes and checks](notes/release-0.1.33-RC30.md).

RC29: removes Performance's extra distance limits for items, food and weapons as a mitigation for reported vehicle-cargo flicker. Dog Companion's skinned bounds now follow WW's custom poses in every camera mode. Resolves all ten reviewed compiler warnings. Cargo cause remains unconfirmed; both visibility changes remain Testing. No config keys/defaults or save-format changes; includes the complete RC28 asset set. See [RC29 changes and checks](notes/release-0.1.32-RC29.md).

RC28: empty stew pots can appear wherever natural loot rolls a gas stove, replacing 25% of those rolls. Existing items, merchants and manual spawns are unaffected. No new config or save format; includes the complete RC27 asset set. See [RC28 changes and checks](notes/release-0.1.31-RC28.md).

RC27: crossbow damage reads 50 in Apocaspawner and native pickup/shop descriptions while WW player projectiles are enabled. These displays previously showed the native 20 despite WW's existing 2.5x projectile multiplier. No config changes; includes all RC26 assets. See [RC27 changes and checks](notes/release-0.1.30-RC27.md).

RC26: replaces natural vanilla sandstorms with moving WW storms, swirling wide/thin hurricanes with safe eyes, native lifting/damage in the sand wall, live colors, sand clouds, dark debris, lighting flashes and distance-delayed thunder. Fresh-install storm defaults match Denis's current palette, dimensions, density and darkening; existing saved settings stay intact. Includes required storm audio. Gameplay and performance remain Testing. See [RC26 changes and checks](notes/release-0.1.29-RC26.md).

RC25: fixes car-panel switches (headlights, cassette Play/Stop) flipping twice per click when Burner's Utilities `[PlayMaker] FastEvents` is installed together with Performance. PM2 Targeted events no longer patches event dispatch another mod already patches, and never sends an event a second time. See [RC25 notes](notes/release-0.1.28-RC25.md).

RC24: F3 settings use CleanHUD's panel, atlas text throughout, larger rows, right-side toggles, text choices, filled slider bars and eight live accent colors. A gold WILD WASTELAND entry above SETTINGS opens the config from both title and ESC menus; closing returns to the paused menu. Performance keeps the module switch, preset, measurements and counter buttons; individual optimization modes, feature statistics, shadow diagnostics and tuning overrides move to Developer (Enable Debug plus Developer mode). Includes explicit Apply Display and a shared Apocaspawner catalog hook for Stew Pot/War Rig. See [RC24 notes and checks](notes/release-0.1.27-RC24.md).

## Install

1. BepInEx 5 is installed and Apocasetter is recommended (the F6 Mods menu shows and rebinds every setting; WW works without it).
2. Disable the replaced mods, or let Apocasetter queue their disable and restart when WW requests it. Keep their config and data files for migration. Preserve customized external PNGs and models when updating.
3. Unzip the release into `BepInEx\plugins\`:
   ```
   BepInEx\plugins\WildWasteland\WildWasteland.dll
   BepInEx\plugins\WildWasteland\theme\...            shared UI theme of the tool window and editors
   BepInEx\plugins\WildWasteland\PlayerBody\Models\, Sounds\   body models, animation bundles, HUD icons, pain sounds
   BepInEx\plugins\WildWasteland\NPC\Models\          dog animation bundles
   BepInEx\plugins\WildWasteland\Guns\Sounds\         knifestab.wav
   BepInEx\plugins\WildWasteland\HUD\Sounds\, Textures\   headshot.mp3, crosshair textures
   BepInEx\plugins\WildWasteland\CleanHUD\Icons\        editable map, compass and bolts currency PNGs
   BepInEx\plugins\WildWasteland\Patrols\             templates, textures, sounds, layouts, WarRig\ models
   BepInEx\plugins\WildWasteland\Survival\Models\, Sounds\   stew pot defaults
   ```
   If an asset is missing, WW falls back to the old plugin folder (`plugins\Apocaplayer\`, `plugins\NPCAI\Models\`, `plugins\Gunplay\Sounds\`, `plugins\GunplayHUD\Sounds\`, `plugins\Apocapatrol\`, `plugins\WarRig\`) or to a copy built into the DLL, and warns in the log. Patrols copies its old data into its new folder on the first run.
   Preserve your customized `CleanHUD\Icons\` files when updating; copy only missing defaults from the archive.
4. Start the game. `BepInEx\LogOutput.log` shows `Loading [Wild Wasteland 0.1.36]` and a summary `Wild Wasteland 0.1.36 modules:`. Resolve any restart notice before testing the affected modules.

Engine Sim compatibility (#127, Testing): **Engine Sim is optional; WW works normally without it and the bridge stays idle.** If you want its features, install [Engine Sim 1.7.0](https://github.com/Wad67/Apocalypter-enginesim/releases/tag/v1.7.0) separately, including its native library and impulse responses. WW schedules its manager and supplies a 500 microsecond scene-discovery cache; the original native simulation/audio threads, engine selection, settings and vehicle-part physics remain with Engine Sim. With its experimental simulated drivetrain enabled, the existing Ignition key supports click to toggle and hold to crank; vehicle hints and CleanHUD's existing engine-status icon follow the coupled engine. Patrols start/stop requests use the simulated switch. Turn off the Engine Sim compatibility module to restore the standalone callbacks. Engine Sim is not included in this archive. Its settings remain in its own Apocasetter page and cfg files; only `[EngineSimCompat] Enabled` is added to WW settings.cfg. Performance and in-game verification remain pending.

## Settings: F6 and F7

RC23: binoculars remember 1–2x additional wheel zoom, identify visible locations and traffic, and show colored captions with capitalized location names without numeric IDs. Fog reveals twice its former radius; binoculars extend the viewed cone to 2x normal range, scoped sniper rifles to 1.5x with no identification or names. Weapon wheel zoom caps at 3x. World traffic markers now have independent Off / Ambiguous / On choices. Debug and Developer are separate pages; Clean HUD/Clean menus switches live in Debug. Includes Survival/Stew Pot (spilling off by default), automatic standalone replacement, updated quest compatibility and transparent editable body portraits. Performance remains experimental. See [RC23 notes and tests](notes/release-0.1.26-RC23.md).

RC22: adds an adjacent CleanHUD LANGUAGE picker for optional ApocaLanguage, with English language names in the main menu font, and eight saved accent choices for CleanHUD text and borders. Swatches are ordered by color, centered at the bottom and raised clear of the screen edge. Hides Apocasetter's duplicate corner MODS button and improves distance-travelled visibility with an accent bar. Includes all RC21 assets. See [RC22 notes and tests](notes/release-0.1.25-RC22.md).

RC21: rebuilds the current committed source with the Engine Sim 1.7.0 compatibility update and finished editable bolt icon. Retains the RC20 inventory, currency, map/HUD and crew changes. No additional gameplay or configuration changes were pending when preparation began. See [RC21 notes](notes/release-0.1.24-RC21.md).

RC20: adds the CleanHUD inventory with click-to-drop equipment and F3-style cursor/pause handling, an outlined bolts balance above HP/armor, editable PNG icons, the reduced green player map chevron and map wheel zoom. Vehicle Hints/Icons and Gear/RPM/Speed Hud are grouped in HUD. Includes crew awareness/navigation fixes, return-to-car and foot patrols, independent World traffic markers and grouped Debug switches. New settings and in-game test steps are listed in [RC20 changes](notes/release-0.1.23-RC20.md).

RC19: restores the native HEADLIGHTS hotkey and restricts first-person wheel zoom to an active weapon with its sights fully raised. Includes the local performance pass across scheduling, HUD/map rendering, NPC navigation/detection, traffic, weapon/body updates and Talk/Quests compatibility. Performance gains and in-game behavior still need verification; no settings defaults change. See [RC19 changes and test steps](notes/release-0.1.22-RC19.md).

RC18: parked merchant buses switch ignition off, hold the handbrake and suspend driving/start attempts with or without Engine Sim. The five-minute signalled stop extends while the player stays within 100 m; normal startup resumes when the wait expires after the player leaves. See [RC18 changes and test steps](notes/release-0.1.21-RC18.md).

RC17: fog of war also hides traffic routes, route labels, merchants and battle skulls in unexplored areas. Fog exploration saves when saving begins, so save-and-quit retains it without waiting for gameplay to resume. No config entries or file formats change. See [RC17 changes and test steps](notes/release-0.1.20-RC17.md).

RC16: approaching a location within 100 horizontal metres reveals its real icon on both map and compass. Discovery survives marker/ambiguity toggles and save/load; cleared red scrapyards become white. The map gains saved fog of war, importing Immersive Map exploration when available, and CleanHUD framing. Map/compass detail defaults to Ambiguous for fresh settings. Needs flash yellow at 80–90 and red above 90. Community links share one background. See [RC16 changes and test steps](notes/release-0.1.19-RC16.md).

RC15: Controls uses a draggable scrollbar and mouse-wheel scrolling, with background dragging disabled. UPDATE AVAILABLE has a dark HUD panel. Smaller framed DISCORD and GITHUB links stack vertically at the bottom right of title/pause main screens, above MODS. Duplicate-key warnings appear in Controls, without the red warning on the main screen. See [RC15 changes and test steps](notes/release-0.1.18-RC15.md).

RC14: the optional Quests J journal now uses CleanHUD's panel and atlas font, with gold quest headings, the original completed count and full journal text, and mouse-wheel scrolling. The source mod retains its J/rebound-key controls and unpaused behavior; disabling CleanHUD restores the original window. No new config entries or save data. See [RC14 changes and test steps](notes/release-0.1.17-RC14.md).

RC13: the main menu shows UPDATE AVAILABLE in the top right, labeled WILD WASTELAND, when the mod's own GitHub releases contain a newer version. The asynchronous check runs once per game launch and includes experimental prereleases, independently of the Apocasetter index. Clicking opens the newer release page. See [RC13 changes and test steps](notes/release-0.1.16-RC13.md).

RC12: F3 → Debug starts with general VerboseLog and groups all other WW verbose/log switches directly below it. Existing keys, values and logging behavior are preserved. CleanHUD's ChaseCam speed/RPM/gear readout follows live driving in either camera view; a latched scope/binocular camera flag no longer suppresses it until ESC. See [RC12 changes and test steps](notes/release-0.1.15-RC12.md).

RC11: Settings → Game → HUD → Patrols on compass → Ambiguous keeps vehicle icons and makes every group white. Locations on compass → Ambiguous uses white question marks. Both keep distances; map modes and quest flags retain their existing behavior. Existing ambiguity settings are preserved.

- **F6 (Apocasetter → Wild Wasteland)** shows `In-Game Settings Button` (default **F3**) and `Enable Debug` (default false). The Apocasetter opt-in remains enabled and hidden. File: `BepInEx\config\com.denis.apocalypter.wildwasteland.cfg`.
- **Internal SETTINGS menu** (default **F3**, historically called F7) has **Game**, **Modules**, **Performance**, and **Debug** when enabled in F6. Debug contains logging, gameplay/debug switches and patrol editor access; its final Developer mode switch reveals a separate **Developer** page for the remaining advanced tools. It pauses the game; Esc or Back closes it. Performance retains presets and measurements; per-feature optimization modes, statistics and tuning are in Developer. Gameplay values and keys live in `BepInEx\config\WildWasteland\settings.cfg`; technical settings live in `tools.cfg`.
- RC1 defaults match Denis's reviewed setup, except **Toggle Ironsights stays off** as requested. CleanHUD needs and HP/armor numbers are on, the crosshair is Vanilla, observing is toggled, camera occlusion is off, and third-person distances are 3.26 m on foot / 5.38 m in cars. Patrols debug spawn controls and traffic debug-map routes are on. Global verbose logging and Enable Debug remain off. The [complete defaults audit](docs/defaults-rc1.md) records the 691 RC1 base entries; RC2 adds ShowConvoys and ShowLocations, both enabled by default. Existing config values and first-run standalone migration are preserved.

Module data and sidecars: `BepInEx\config\WildWasteland\<Module>\`.

CleanHUD loading screens use `BepInEx\plugins\WildWasteland\LoadingScreens\Screen1.png`, `Screen2.png`, etc. The `screens.json` in the same folder maps each image filename to its tip text, with defaults for Screens 1–5. Available numbered images are chosen randomly for each new game/save load or Apocasaver autosave; repeats are allowed. JSON edits and added/replaced images apply on the next load; unchanged textures remain cached. The persistent screen keeps the CleanHUD font through scene changes, with LOADING (AUTOSAVING during Apocasaver saves) bottom left and a smaller wrapped tip near top left. Clean menus must be enabled. Missing numbered images are skipped.

CleanHUD controls are in **F7 → Game → HUD** (Clean HUD, Clean menus, Needs, HP and armor numbers, Insanity, Scale). Its module switch and job timings are in **F7 → Modules**; EnableCheats is in Debug. Settings live in `config\WildWasteland\settings.cfg`, under `CleanHUD / HUD` and `CleanHUD / Menu`. Missing settings import the standalone choices even on an existing WW install. Layout and the game key cache copy once into `config\WildWasteland\CleanHUD\`; edits to the new layout reload within two seconds. POI/map icons are editable PNGs in `plugins\WildWasteland\CleanHUD\Icons\` (building, other, camp, cave, wreck, diamond, unknown and player); `bolt.png` controls the bolts currency icon above HP/armor. Replace a PNG and restart to reload it. Preserve customized files when updating. Other CleanHUD textures/font assets remain embedded. Disable standalone CleanHUD through Apocasetter and restart to use the built-in module.

RC3 adds optional CleanHUD compatibility with **Apocapocket 2.0.10**, which stays a separate plugin and is not included in this archive. CleanHUD shows its three to six logical slots, highlights extra-slot weapons correctly, displays ordinary item icons, and updates key badges after rebinding. Backpack capacity controls the number of shown slots. The row retains its existing placement and scale; switching CleanHUD off restores the native pocket UI. No new config entries or save data. Verification remains Testing; see [test steps](notes/compat-Apocapocket-CleanHUD.md).

Historical RC4 (superseded by the native Quests port below) added optional compatibility with **Apocatalker 2.0.0** and **Apocalypter Quests 1.8.0**. Keep those two mods installed, including their dialogue, quest, faction, theme and voice files; they are not bundled or replaced. WW schedules their callbacks and slices world discovery, renders CleanHUD dialogue with the original window as its fallback, adds native map/compass quest markers and connects hired followers to WW animations, reload holds and passenger combat. Immersive Map remains supported when installed. `[QuestsCompat] Enabled` defaults true in settings.cfg. Original ES3 key `ApocalypterQuests` and Talk's shared/manual story profiles are preserved. Features remain Testing; see [compatibility and test steps](notes/compat-Talk-Quests.md). If native plugin components were destroyed during a scene change, restart the game to compare fully standalone behaviour after disabling the bridge.

Women of Wasteland is now part of **NPC**. Its population controls live in **WW Settings > Debug > NPC AI & navigation > NPC population**, not Apocasetter/F6. The default female population is 33%. Eligible Flexa/Sprokka drivers, passengers and gunners use the configured female percentage; editors also offer Gungirl/Shoota explicitly. Default models/textures/voices are included in the DLL and installed only where missing under `WildWasteland/NPC`. Existing WoW settings and custom assets migrate without overwriting WW choices. If standalone WoW is loaded, the integrated female feature stays off while NPC AI continues; disable standalone WoW and restart. In-game verification remains Testing; see [port and test steps](notes/port-NPC-Women.md).

## First run: migration

When WW's cfg files do not exist yet, it reads the old ones once (`com.denis.apocalypter.apocaplayer.cfg`, `apocaclimber`, `npcai`, `gunplay`, `apocaunloader`, `gunplayhud`, `apocapatrol`, `warrig`, `merchantspotter`, Apocaraider as a fallback, `config\Apocaplayer\camera.cfg`, the `hidden-settings.not-saved` files) and copies every value it knows, including key bindings (converted to the new key names). Data files are copied where missing: `config\Apocaplayer\*` -> `config\WildWasteland\PlayerBody\`, `config\NPCAI\*` -> `config\WildWasteland\NPC\`, `plugins\Apocapatrol\*` + `plugins\WarRig\*` -> `plugins\WildWasteland\Patrols\`. The old files are never changed, and WW keeps reading the old locations as a fallback. Keys whose default became None (bus pose editor Insert, merchant editor Numpad*, NPC test web F9) are only carried over if you had changed them. The log lists every migrated value.

## Saves

Nothing about saves changed: per-gun magazines stay under `Apocaunloader.Magazines` in the game's save file, patrols under `Apocapatrol.Patrol` plus the sidecar `config\WildWasteland\Patrols\Saves\<SaveGameN.es3>.bin` (old `config\Apocapatrol\Saves\` still read), NPC knowledge in `config\WildWasteland\NPC\Saves\<slot>.senses.txt`, motorcycles and War Rigs keep their prefab ids. Saves made with the standalone mods load in WW, and saves made with WW load in a game **without** WW exactly as they did without the standalone mods: everything vanilla is fine; raider crews, motorcycles and War Rigs that came from the mods cannot be restored by the game alone (same as before).

## Known gaps (0.1.0)

- Not yet verified in the game (no game in the build environment): run the checks in `verification/README.md`; the first bugs will show up as `job '...' threw 20 times in 10 s - job DISABLED` lines in the log.
- Assets are not in the repository; the release zip must contain them (see Install) or the old plugin folders must still be present.
- Other mods that used the old public APIs by reflection (`NPCAI.Api`, `Apocaplayer.ModAPI`, `Gunplay.Api`, `GunplayHUD.Api`) will not find them; no known mod does. Ask for a shim if you have one.
- Apocasaver no longer recognises the patrol system (it keyed on Apocapatrol's GUID): patrol saves work exactly as without Apocasaver; Apocasaver's clean-save step no longer removes abandoned raider cars.
- Performance has not been measured yet (`docs/perf-log.md` has the procedure); the budget is 0.5 ms average / 2 ms worst per frame for the whole mod.
- Simple-bandit crews are not handed back to the NPC module when Patrols is switched off at runtime (they stay seated and inert until reload).

RC5 fixes the Quests follower hook that prevented hiring, adds distinct cave/wreck/camp icons and separate compass/map/marker/route controls, and uses compact gold dialogue choices plus CleanHUD quest notifications. Map icons have no name labels. See [RC5 changes](notes/release-0.1.8-RC5.md) and [in-game test steps](notes/patch-map-hiring-dialogue.md).

RC6 filters new delivery destinations to camps with a living, loaded NPC accepted by Talk and non-hostile to the player. Empty camps with a faction-named spawner no longer qualify. Existing accepted quests keep their saved destinations. See [RC6 changes and test steps](notes/release-0.1.9-RC6.md).

RC7 replaces RC6's restriction with delivery recovery: distant camps qualify again; reaching an empty camp changes the objective to return the goods and explain that nobody was there. A destination already known to be cleared, or an empty return point, fails the quest. Native spawners must finish and a fresh NPC scan plus 15 seconds of active play near the empty point must pass. Return progress uses the existing quest save fields. See [RC7 behavior and test steps](notes/release-0.1.10-RC7.md).

RC8 uses flags for every quest phase, with each flag matching its quest tracker bar color. The tracker clears the compass even when Insanity is hidden. Clean menus also styles Apocasaver's save-name prompt. The full package includes Women of Wasteland assets with a 33% default population. Existing configured population values remain preserved. See [RC8 changes and test steps](notes/release-0.1.11-RC8.md).

RC9 adds optional **ApocaChaseCamera 0.2.5** compatibility. Keep it installed separately: it retains its driving camera and original instruments with vanilla UI. CleanHUD shows smaller unboxed speed/RPM/gear text above the weapon panel, with RPM redline color and the rendered camera's compass heading. Size, height and enable remain ChaseCamera settings; hide diagnostics require WW verbose logging. RC9 also includes the pending crew quest restrictions: seated crew cannot be hired; current/former crew cannot offer new quests, with former-crew names saved per slot. See [RC9 changes and tests](notes/release-0.1.12-RC9.md).

## Build

On Windows (PowerShell or cmd, .NET SDK 6+, nothing else needed):
```
dotnet build WildWasteland.csproj -c Release -p:Deploy=true    # builds and copies the DLL into the game (game closed)
```
Add `-p:GameDir="<path to Apocalypter>"` if the game is not in `E:\SteamLibrary\steamapps\common\Apocalypter`. Do not use WSL for this repo.

Claude cloud sessions build with mono instead (Linux container only, not for the game PC):
```
apt-get update && apt-get install -y mono-mcs
MANAGED=<game>/Apocalypter_Data/Managed BEPCORE=<game>/BepInEx/core sh build.sh <outdir>   # -> <outdir>/WildWasteland.dll
MANAGED=... BEPCORE=... sh tools/build-module.sh Patrols                                   # Core + one module, others stubbed
MANAGED=... BEPCORE=... sh tools/config-harness/run.sh <outdir> <BepInEx/config with old cfgs>   # binds every config without the game
```
`AGENTS.md` has the rules for contributors and AI sessions; `docs/` has the module, patch, decision and performance records.

## Links

- Wiki (features, settings, troubleshooting, architecture): https://github.com/DeonUrist/WildWasteland/wiki (source: `V:\Repos\Apocalypter\WildWastelandWiki`)
- Project board (feature status: Backlog / Working on / Testing / Verified / Done): the GitHub project of this repository; `Feature-Status` in the wiki mirrors it.
- Apocasetter / mod index: https://github.com/DeonUrist/Apocasetter-Index

Compass markers (Testing): SETTINGS → Game → HUD has **Convoys on compass** and **Locations on compass (1 km)**, both on by default. Convoys covers all vehicle groups: merchant yellow, Coyotes green, patrols red, convoys orange, with one car icon and distance per group. Nearby native locations use a camp icon and distance within 1 km. These are independent of debug-map routes; toggles are saved as `CleanHUD / HUD` ShowConvoys / ShowLocations in settings.cfg. Existing unrelated compass markers remain available.

## Native Quests (Testing, 2026-10-18)

Quests 1.8.2 now runs inside WW, including jobs, factions/reputation, takeovers, voices and hiring. Disable standalone Apocalypter Quests and retain its config/content for migration. Talk is optional: without it, aim at a friendly NPC and press F; J opens the journal and Delete dismisses a hire. Existing AQ1 saves and custom content/settings are preserved. Supplied voices credit LostSilver666 and Soldier96. Quests is removed from suggested mods. Build/deployment and the full acceptance checklist are in [the port report](notes/port-Quests.md); gameplay/performance remain unverified.
