# Wild Wasteland 0.1.24 RC21

Fresh release of the current committed source, including the latest Engine Sim 1.7.0 compatibility update (#127) and Denis's finished editable bolt icon. Fetching GitHub at preparation time found no additional uncommitted or unpushed gameplay changes beyond the final RC20 source. RC21 changes the plugin version and release documentation; no new gameplay settings or defaults are introduced.

- Engine Sim remains a separate optional install. The bridge targets 1.7.0, retains the source's simulation/audio/tuning behavior and ships none of its DLLs/assets. [Compatibility details](compat-EngineSim.md).
- All RC20 features remain included: the modal CleanHUD inventory and equipment drops, outlined bolts balance, editable icon folder, smaller green player chevron, map wheel zoom, independent traffic markers/routes, vehicle HUD controls, crew awareness/return-to-car and foot patrols. [RC20 changes and tests](release-0.1.23-RC20.md).
- The ZIP retains the previous release assets and includes the editable `CleanHUD/Icons/` PNGs, including bolt.png. Keep customized PNGs when updating; copy only missing defaults. Extract into BepInEx/plugins and restart after replacing icons.

Gameplay and performance remain Testing. No issue statuses or Apocasetter index registration change.

## Check after installing

1. Confirm `Loading [Wild Wasteland 0.1.24]` and the expected module summary without failed hooks or disabled jobs.
2. With optional Engine Sim 1.7.0 installed, check `Engine Sim 1.7.0 scheduled` and player/crew ignition. Without Engine Sim, the bridge remains idle.
3. Check the finished bolt icon and balance above HP/armor; collect or spend bolts and toggle Needs. Open the inventory, drop equipment, close it and confirm input resumes.
4. Check the player map chevron, wheel zoom, traffic marker/route switches, fog and a save/load round trip. Repeat the crew/foot-patrol checks from [the detailed notes](feature-crew-foot-patrols.md).

Validation: Windows Release build with Deploy=true passed with zero errors and eight existing warnings. Built/deployed DLL MD5: `1DFAFED06C2E82EC31AEDC31792D8909`. Agents do not run the game.
