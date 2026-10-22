# Wild Wasteland MP RC19 (0.1.55) — Respawn persistence and Vistas multiplayer

An explicitly selected seat, bed or bedroll now survives downing, revival and death. Native health resets call the same restore hook as full player saves, but contain no respawn data; the hook previously interpreted that absence as a cleared selection. Partial vitals updates now preserve it. Choose a different respawn object with Secondary Use to replace the previous one. The anchor follows the object, including a seat in a moving car; placement is on a clear supported surface nearby, rather than sitting in the seat. An unavailable/blocked anchor can still use the existing host/camp fallback without clearing the choice. Loading an older world save restores that save's recorded selection.

Includes the pending optional Vistas World Gen multiplayer compatibility: host generation/world settings are shared and locked during sessions, guests follow host weather and wind time, and per-machine spawn tracks and dust devils are disabled for the session. Engine heat ignores locked/unentered Patrols cars and vehicles simulated elsewhere; Vistas forces and dew writes respect object ownership. Terrain probes report matches or repeated differences. Local visual randomness remains local. Vistas is installed separately; see [the compatibility details and tests](vistas-mp-compat-2026-10-22.md).

Handshake **28**: update every peer to this exact release and matching dependency/mod builds. Requires BepInEx 5 x64 and the separate [Apocalypter Multiplayer mod 0.4.1](https://www.nexusmods.com/apocalypter/mods/10). Extract the complete runtime ZIP into `BepInEx/plugins`, preserve customized assets and keep standard Wild Wasteland disabled. No new WW config entry or save key; retains RC18 fog-save fixes.

All pending repository work is included in the source commit. `WastelandMPNew/` is an independent kernel, Steam adapter and WW2 integration dossier outside the active plugin source. It is a buildable foundation with missing gameplay adapters, not an installed multiplayer replacement, and is excluded from the runtime ZIP.

## Required in-game checks — Testing

1. Host and two guests each select a car seat using Secondary Use and see the selection confirmation. Move the car away from camp and host, then die/Give Up twice without selecting again. Expect placement beside the selected seat at its current position, with `Respawn placed beside selected object <number>` in the affected player's log.
2. Repeat downed/revive, sleeping in a different bed, detaching/moving the selected seat, and selecting a new object. Only explicit reselection replaces the anchor. Save/reload and disconnect/rejoin must retain the selection in the matching world snapshot. If the previous build already lost it, select it again once.
3. With Vistas installed on every peer, test mismatched local generation settings before joining, host weather/wind, late joins, terrain borders, guest-owned vehicles and AI cars. Expect host settings applied before world launch and `[Vistas] Terrain matches ...`; investigate `Terrain DIFFERS` or missing compatibility hooks.
4. Regress personal/shared fog save twice, overwrite, save-and-exit/reload, plus merchant purchases and cargo revisits.

## Build and package

Windows Release/Deploy=true succeeded with zero warnings/errors. Exact stage `_releases/WildWastelandMP_0.1.55-RC19a.dll`; built/staged/installed MD5 **5B7B71674F87C6640D5AC39C44E839C4**. Game processes were closed and the complete MP assets/dependency were present; the exact DLL was deployed. This supersedes the intermediate respawn-only RC18 build made before the request to release everything.

Runtime ZIP `WildWastelandMP-0.1.55-RC19.zip`: **287 files**, **236,701,736 bytes**, SHA256 **f53838f8e1c437ebaa141ede4108c3a69bcca55d94daeb2830df6e32d370a3f3**. Packager verifies baseline checksum, every source byte, CRC, safe unique paths, one plugin DLL and 18 loading screens; 155 retained baseline assets unchanged. All **286 non-DLL entries** are byte-identical to RC18. Documentation, the independent kernel, dependency DLLs and personal runtime data are excluded.

Build-only verification; gameplay, Steam, save integrity and performance remain Testing. No game launch, automated gameplay, live save/config edits, base/Index/wiki changes.
