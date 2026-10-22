# Wild Wasteland 0.1.23 RC20

Includes all changes since RC19 and Denis's finished bolt icon. This is an experimental prerelease; gameplay and performance verification remain Testing.

- Inventory: CleanHUD framing, padded faction/day/time labels, a framed player portrait and one ammo/supplies panel, including grenade and battery counts. Opening releases the mouse, pauses and blocks actions using the F3 modal mechanism. Click binoculars, armor, flashlight or backpack to drop them through the native item FSM. The inventory clears the HP/armor and weapon panels.
- Bolts currency: the native Money balance and a small bolt symbol appear above the health/armor panel's left edge with black outlines. The shipped `CleanHUD/Icons/bolt.png` is Denis's replacement artwork.
- Editable icons: map/compass POI images are external PNGs under `CleanHUD/Icons/`, included in this archive. Building_* uses building.png; unclassified POIs use other.png. Camp, cave and wreck keep separate images. The player map chevron stays 18x21 HUD pixels with a 22x25 outline and draws above routes and marker icons. Higher-resolution images do not increase UI size.
- Map: mouse wheel zooms in up to 1.5x, resets on close and prevents simultaneous ironsight/third-person wheel zoom. World traffic markers controls moving dots/names independently from route lines; fog remains respected and ambiguous markers show Unknown.
- HUD settings: Vehicle Hints and Vehicle Icons use Off/On controls, default on. ChaseCam HUD is renamed Gear/RPM/Speed Hud and sits under Vehicle Hints. Debug switches are grouped under Logging.
- Crew and traffic: idle dismounted crew stop leftover running, chase with baked navigation/feelers, acquire the player through awareness or fought-memory, and can walk back to a usable car after combat. Living passengers/gunners can replace a dead driver. Generated foot patrols and stranded crew use persistent foot journeys. See [crew/foot-patrol notes](feature-crew-foot-patrols.md).
- Engine Sim: includes the other session's compatibility update for separately installed Engine Sim 1.7.0. The exact-version gate and startup message now accept 1.7.0; source simulation/audio, AutoQuality and tuning toolkit remain with Engine Sim. No Engine Sim DLL/assets are included. See [compatibility notes](compat-EngineSim.md).

## Settings and installation

New settings.cfg entries: Patrols Foot patrols and Return to car (both true); CleanHUD / Map TrafficMarkers (true). Map Routes defaults Off for fresh settings; saved choices remain intact. NPC and Patrols gain crew awareness, travel and foot-traffic tuning in tools.cfg, listed in the crew notes. No external companion DLLs are included.

Extract the archive into BepInEx/plugins. Keep customized `WildWasteland/CleanHUD/Icons/` files when updating and copy only missing defaults. White artwork on transparent PNGs accepts the HUD's marker colors; restart after replacing images. Inventory and currency add no config entries. Existing game saves retain their native keys; foot journeys use the existing traffic sidecar.

## In-game checks

1. Restart and confirm `Loading [Wild Wasteland 0.1.23]`, the expected module summary and `Map/compass icons loaded from disk:` without icon decode warnings.
2. Collect/spend bolts: the balance changes above HP/armor with the finished icon and black outline. Toggle Needs and HUD scale; the display follows the panel.
3. Open inventory with Tab/rebound Player Sheet: mouse is free and movement/fire stop. Check labels, portrait, combined ammo/counts and clearance above gameplay panels. Drop each of the four equipment types, verify capacities/effects, then close with Tab/Escape and confirm input resumes.
4. Open the map: check the smaller green chevron above route lines/icons, distinct buildings/camps/caves/wrecks and replacement artwork on map and compass. Zoom with the wheel, close and reopen; camera zoom should reset and weapon/body zoom must not change while the map is open.
5. Toggle World traffic markers and Map Routes independently; verify fog and Ambiguous/On rendering. Check Vehicle Hints, Vehicle Icons, Gear/RPM/Speed Hud and grouped Debug switches.
6. End a crew fight: survivors stop running in place, return to a usable car and drive away together. Test a dead driver, a destroyed/stuck car and player awareness after dismount. Foot patrols should materialize, reach camps and survive save/load. Diagnostics and detailed steps are in the crew notes.
7. With optional Engine Sim 1.7.0 installed, confirm `Engine Sim 1.7.0 scheduled` and check player/crew ignition with its simulated drivetrain on/off. Without Engine Sim, compatibility stays idle.

Validation: Windows Release build with Deploy=true passed with zero errors and eight existing warnings. Built/deployed DLL MD5: `3AC2B1331EDA3230B2E70F389BD2154C`. Agents do not run the game.
