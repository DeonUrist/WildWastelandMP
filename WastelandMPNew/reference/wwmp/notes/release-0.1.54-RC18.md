# Wild Wasteland MP RC18 (0.1.54) — Fog-of-war saving

Fixes saving being rejected with `Fog of war save failed: Win32 IO returned 1175`. The fog writer now uses the existing Windows-safe atomic replacement used by other save participants. A later successful callback does not erase a genuine earlier transaction failure.

Save formats and settings are unchanged. Reload the last good snapshot after an already failed transaction, then test saving twice, overwriting a named save, and save-and-exit/reload. Expect save completion, no error 1175 and retained explored map areas. Gameplay/save-integrity confirmation remains **Testing**.

Extract the complete ZIP into `BepInEx/plugins`, preserving customized files and backing up saves. No companion mod DLLs are bundled.

Both personal and host shared-vision fog files are covered. Handshake **27** retained; update every peer. Requires BepInEx 5 x64 and the separate [Apocalypter Multiplayer mod 0.4.1](https://www.nexusmods.com/apocalypter/mods/10). Keep standard Wild Wasteland disabled. Includes RC17 merchant corrections; unpublished Vistas multiplayer work is excluded.

Build and package: Windows Release/Deploy=false: zero warnings/errors. Exact stage `_releases/WildWastelandMP_0.1.54-RC18a.dll`, MD5 **6692AB5E513B0F0C44F6E78B930FCAFE**. Built from RC17 HEAD plus only the fog fix/release metadata; pending Vistas work excluded. Not deployed: standard-WW installation lacks active MP folder/dependency. ZIP **287 files**, **236692474 bytes**; SHA256 **4435ec602d9549753c8445acc2ce37c547ddbfec1b5cc7c468ef339ecd1e123c**. All **286 non-DLL entries** are byte-identical to the previous release. CRC/exact bytes/safe unique paths/single DLL/18 loading screens checked. No game/harness/live save/config edits.
