# Wild Wasteland 0.1.12 RC9

Numeric plugin version 0.1.12; tag v0.1.12. Status: Testing. Includes all pending local source and documentation changes.

## Changes

- Optional ApocaChaseCamera 0.2.5 compatibility (#126). PlayerBody adapts the original mod's Apocaplayer type discovery so its driving camera, aim recenter, cutaway and world-origin handling work with WW. Keep ChaseCamera installed separately; its source and DLL are not bundled.
- Vanilla UI retains ChaseCamera's original driving instruments. CleanHUD shows smaller unboxed speed, RPM and gear text above the weapon panel, lowered by one font height from the first text-only build. RPM turns red near the limiter, and the compass follows the rendered driving view. ChaseCamera's HUD enable, size and height settings remain authoritative.
- The occasional reported disappearance is not yet explained and could not be reproduced. The `ChaseCamera readout hidden:` trace now requires WW global verbose logging and records source snapshot age, camera/view, input and focus state only on a visible-to-hidden transition, at most once per second. No speculative visibility fix is included.
- Crew eligibility restrictions: seated drivers, passengers and turret occupants cannot be hired or offer new quests. Former crew on foot retain the source hiring rules but cannot offer new quests. Existing quest turn-ins and follower dismissal remain available. Guards include cached acceptance and scripted quest.give.
- Former-crew names persist per save slot in `config/WildWasteland/QuestsCompat/Saves/<slot>.crew.txt` after the native save flush. Ordinary bailouts and turret landings record their replacement's existing numbered name. New worlds clear history; pre-patch survivors whose origin tags were already lost cannot be reconstructed. See [crew behavior and tests](patch-crew-quest-restrictions.md).

## Installation

Extract the full ZIP into BepInEx/plugins. The archive retains RC8's theme, module, loading-screen and Women of Wasteland assets. Keep optional ApocaChaseCamera, Apocatalker and Apocalypter Quests installed separately. No configuration entries were added; existing choices and save formats are preserved, with the crew-history sidecar described above.

## Test in game

1. Restart and check `Loading [Wild Wasteland 0.1.12]`. With ChaseCamera 0.2.5 enabled, expect `ChaseCamera 0.2.5 type adapter ready` and `ChaseCamera 0.2.5 HUD bridge ready` without bridge warnings.
2. Enter a car, holster the weapon and select third person. Check the smaller speed/RPM/gear line just above the weapon panel, its lowered position and RPM redline color. Compare reverse, neutral and forward gear/total gears. Adjust ChaseCamera's HUD size/height.
3. Toggle CleanHUD off/on: the original instruments and clean text replace each other without duplicates. Check first person, aiming, pause, exit/re-entry, save/load, orbit, recenter and an origin shift; compass and crosshair should follow the rendered view.
4. With verbose off, expected hide transitions must not emit `ChaseCamera readout hidden:`. With WW global verbose enabled, a visible-to-hidden transition records its state. If the unexplained disappearance returns, retain that log and note whether ESC twice restores the readout.
5. Follow the crew note's seated hiring, former-crew quest, bailout/turret, save/load/autosave and per-slot checks. Ordinary on-foot NPCs retain their original work/hiring eligibility.

Validation: Windows Release build and deployment only. Gameplay verification remains for Denis; features remain Testing.

Release build: **zero errors, seven existing warnings**. Built and deployed DLL MD5: **4D06742F7D2F6D02E92C54FA4F6F22C8**.
