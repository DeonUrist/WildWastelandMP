# WildWastelandMP RC1 — 0.1.37

First public release candidate of the multiplayer fork, including all pending work through 2026-10-19.

## Requirements and installation

- Apocalypter with [BepInEx 5, Windows x64](https://docs.bepinex.dev/articles/user_guide/installation/index.html).
- The separate [Apocalypter Multiplayer mod from Nexus Mods](https://www.nexusmods.com/apocalypter/mods/10). Its DLL is not bundled.
- Extract the full RC1 ZIP into `BepInEx/plugins`, producing `WildWastelandMP/WildWastelandMP.dll` and its asset folders. Disable standard WildWasteland and the standalone mods replaced by its modules; retain configs and customized assets.
- Every peer needs RC1, the same Multiplayer mod and matching mod versions. This release uses WW handshake revision23.

## Playing together

The host opens MULTIPLAYER from the title menu, chooses HOST (IP) or HOST (STEAM), then creates a world or loads a save. Guests join from the title menu using the host's IP/port, or accept the host's Steam invitation while at the title menu. F3 opens settings; TAB opens inventory and the player list. Guest saves request a save from the host. Local menus leave the multiplayer world running.

## Included in RC1

- Host-authoritative Wild Wasteland AI, traffic, quests, weather and shared world interactions, with local HUD/player preferences and matching-mod checks.
- Named screenshot save library with search, manual/autosave/backup tabs, overwrite/delete/restore controls, player snapshots and offline saving. Autosave countdown and combat deferral, parked/upright placement after loading seated saves, and camera-parent restoration.
- Persistent sleeping-player ragdolls, carrying, reconnect handoff, object-based respawn anchors and host player-reset controls. Reloading a world restores its matching player snapshot.
- Inventory quantity drops for ammo, grenades, batteries, Nuts and medical supplies; online/sleeping player roster. Reliable gun-drop updates to other guests.
- Eight player models and portraits, equipment/weapon replication, Low Crawl, seated gun/melee/fist/lance/grenade presentation, head look, body turning and first-person camera fit. Guest-owned motorcycle balance.
- Camp shopkeepers retain their idle positions; traveller hearing, multiplayer menu/notification improvements, animated loading artwork, native distance-haze removal, map fog debug controls and multi-part stew-pot rendering.
- Optional three-instance Windows launcher with separate local identities, saves and logs. Normal multiplayer play needs one game instance per person.

## Verification

RC1 is a prerelease. Windows Release compilation and package integrity are checked; current in-game visuals, networking, save recovery and performance remain Testing. Earlier isolated test results cover their documented historical builds only. No game or gameplay harness was run for this release.

Windows Release build: zero warnings/errors. Built, unique-stage and installed DLL MD5: `E2893753B3E5FFD241AA06F4B00D39D1`. The exact stage is `WildWastelandMP_0.1.37-RC1a.dll`; installation was done with the game closed and both disabled-install manifests absent. The full ZIP includes shipped assets/defaults, portraits, loading artwork and optional local launcher, with no personal configs/saves or third-party plugin DLLs.

Before relying on a campaign, test host/guest join, save/reload/reconnect, sleeper carry, inventory drops, vehicle driving/passengers and seated attacks in both camera modes. See [multiplayer guide](../README-MULTIPLAYER.md), [save checks](../compat/ApocalypterMP/HOST-SAVES.md), [sleepers/respawn](../compat/ApocalypterMP/SLEEPERS-RESPAWN.md) and [seated pose checks](seated-melee-throws.md). Report issues with `BepInEx/LogOutput.log` from the affected peer.
