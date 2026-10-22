# Wild Wasteland MP RC15 (0.1.51) — Single-player save loading

Fixes the selected SP save being replaced by a new working-save identity during scene startup when Apocalypter Multiplayer is installed. The early native `setSeed` state runs before the menu broadcasts `LoadGame`; both SP launch paths now pin the selected file before entering the scene and clear the marker if launch fails.

The reported reproduction was seed **8975309 → save → load → fall without ground**. The saved MyTest snapshot still contains seed 8975309, while the affected load logged default seed 12345 and a different working file. This release corrects that file-selection race. In-game confirmation of the falling report remains **Testing**.

No save migration, config, protocol or handshake change. Existing saves are retained. Handshake **27** remains; all multiplayer peers should use the same WWMP release and loaded mod builds. Required dependencies remain separately installed **BepInEx 5 x64** and [Apocalypter Multiplayer 0.4.1](https://www.nexusmods.com/apocalypter/mods/10). Extract the full ZIP into `BepInEx/plugins`, keeping standard Wild Wasteland disabled. RC14 multiplayer changes and their verification limits remain included.

Manual checks:

1. Restart and load the existing MyTest snapshot from the title menu. Expect the original world, position, inventory and solid ground. The log should show `[System] Loading offline save:` and world readiness with seed 8975309.
2. Create a fresh SP world with seed 8975309, save on foot, return to title and load. Expect the same world without falling.
3. Load the same snapshot from a running SP world. Then start a genuinely new game with another seed to confirm world isolation. Retest a seated save through the existing parked-car/standing placement flow.

No game or harness launch, live save/config edits, or changes to other repositories. Gameplay, save integrity and performance remain Testing. Full source diagnosis: [SP load selection](sp-load-selection-2026-10-21.md).

Windows Release/Deploy=true passed with zero warnings/errors; game closed and complete MP installation present. Exact stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.51-RC15a.dll`; built/staged/installed MD5 **D509E673A723BA7C9F86ABE0178BA702**. This versioned release build supersedes the unversioned 0.1.50 diagnostic deployment.

Runtime-only ZIP: **287 files**, **236,693,625 bytes**; SHA256 **302c5228429a162e831d02d1c627b09867968a702ac7e050854272270d67ed4d**. CRC, exact source bytes, safe unique paths, single plugin DLL, 18 loading screens and 156 retained published baseline assets checked. Dependency DLLs and personal runtime data are excluded. Published as an official non-prerelease release candidate for latest-release discovery.
