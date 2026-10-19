# Wild Wasteland MP RC9 (0.1.45) — Guest coyote quests

Fixes the built-in Talk prompt missing for guests approaching friendly coyotes. Multiplayer disables NPC simulation FSMs on clients, which removed them from the list used by quest targeting. The quest cache now includes their disabled Health components through existing network spawn notifications and the existing scene walk, keeping NPC AI disabled and quest decisions on the host.

Guests can use the existing conversation requests to ask for work and accept quests. The four-metre targeting range and viewing cone are unchanged; a small collider was not the cause. Existing host validation, shared quest book and rewards remain in place. This fixes WW's built-in interaction cache; optional Apocatalker still owns its own targeting when installed.

No new settings, save formats or protocol changes; handshake 24 remains. Install the full ZIP into `BepInEx/plugins`, with standard Wild Wasteland disabled. Hosting/joining requires separately installed BepInEx 5 x64 and [Apocalypter Multiplayer 0.4.1](https://www.nexusmods.com/apocalypter/mods/10). All peers must restart with matching WWMP builds. Offline play retains its dependency-free mode.

Manual check: as a guest, approach a living friendly camp coyote within four metres, face its torso, press the configured Quests talk key (default F), ask for work and accept. Expect the Talk prompt, conversation choices and matching party journal updates. Repeat for newly spawned NPCs, after reconnecting, and on the host. Runtime acceptance remains Testing; no game or harness was launched.

Windows Release/Deploy=false passed with zero warnings/errors. Built/staged DLL MD5 **04BC690174D31569EDC8F6C2F90C580B**; exact stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.45-RC9a.dll`. No deployment to the current standard-WW installation, which lacks the MP assets/dependency.

Runtime-only ZIP: 287 files, 236,668,535 bytes; SHA256 **81651e13d3088a516f543edede08e7c27165cf67dd72af2b9ac8ce459a7cb9cf**. CRC, unique/safe paths, exact source bytes, 156 unchanged baseline assets, 18 loading screens and single plugin DLL checked. Documentation, dependency DLLs and personal runtime data are excluded.
