# Wild Wasteland 0.1.32 RC29

Experimental prerelease. Both visibility changes remain Testing; the reported cargo flicker has not been reproduced or confirmed fixed.

- Performance retains the original camera distances for Item, Food and Weapon layers, removing its additional 80-150 m limits as a conservative mitigation for cargo disappearing in boxes on moving cars. Car, NPC and hinge limits and static scenery LODs remain. More distant loose items may render, potentially affecting FPS.
- While WW owns Dog Companion's custom animation, Unity updates the companion's skinned-mesh bounds for its displayed pose in every camera mode. This addresses the case where imported bounds exclude an otherwise visible head, leg or tail. Original renderer flags and bounds are restored during save/load states and when the bridge releases the dog. Requires the existing NPC dog animation bridge; independent of Performance mode.
- Resolves all ten reviewed compiler warnings: marks intentional asset-directory member hiding, removes unused fields, and explicitly initializes Nav.PickInline to false while preserving its reflection-based verification-tool use.
- No config keys, defaults, saved components, prefab IDs or save-format changes. Includes the complete RC28 asset set, including Weather audio and natural Stew Pot loot. Install into BepInEx/plugins and preserve custom assets/configs.

## Check after installing

1. Confirm `Loading [Wild Wasteland 0.1.32]` and the NPC/Performance module startup lines in LogOutput.log.
2. With Performance on and the affected preset/camera, drive a car carrying loaded attached boxes. Contents should stay visible through driving and turns. Repeat in first and third person and after save/load. There is no cargo-specific confirmation trace; report whether the original disappearance still occurs.
3. With Dog Companion and WW's dog animation bridge active, look past a standing, moving or seated dog so only its head, legs or tail remain onscreen. Visible portions should remain drawn in first and third person, with Performance on and off. Repeat after save/load and after toggling companion compatibility off/on.
4. Enable the NPC Dogs Log switch for `Dog animations: <name> companion mesh bounds updating (<N> renderers).` This confirms activation, not visual acceptance.
5. Compare FPS on the same route with the same loaded cargo. Other performance limits remain active, but removing the item-layer caps can increase distant item rendering.

Windows Release build/deploy passed with zero warnings and zero errors. Built, installed and uniquely staged DLL MD5: `4CF6F5F3E559330CA973B0769BA9632B`. Gameplay and performance remain pending.
