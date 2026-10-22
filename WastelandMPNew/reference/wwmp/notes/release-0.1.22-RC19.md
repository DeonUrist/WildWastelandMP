# Wild Wasteland 0.1.22 RC19

This release includes all current local changes since RC18: the performance pass and the HEADLIGHTS and ironsight wheel-zoom fixes. It is an experimental prerelease; in-game verification remains Testing. Performance improvements have not been measured in game.

- HEADLIGHTS: restores the game's native keyboard handler, which PlayerBody previously suppressed without sending a replacement toggle. The dashboard switch and rebound headlight action keep their native behavior.
- First-person wheel zoom: requires an active weapon whose own AimDownSights FSM has reached its raised state. Empty hands, melee weapons and lowered sights cannot activate the extra zoom. Lowering or holstering the weapon resets it immediately. Existing scope support is retained.
- Core and HUD: runs WW IMGUI only when a registered GUI job or the settings window needs it; stops the native map camera rendering while the map is closed; reduces repeated compass hierarchy/layout/text updates and caches HUD bindings and inventory reads.
- NPC: spreads brain, idle, detection and maintenance work across frames; slices expensive exit selection; caches navigation move masks and routing fields; reduces repeated physics, component and discovery work for NPCs, animals and female characters. Includes the existing navigation-equivalence tool changes and static audit notes.
- Patrols and POI: budgets location discovery, caches passenger and vehicle lookups, reduces repeated traffic/map work, and lowers the frequency of proximity and recovery checks.
- PlayerBody and Guns: caches pose, equipment, vehicle and weapon lookups; reduces transient allocations; updates projectile simulation at a fixed 60 Hz with interpolated drawing.
- Talk/Quests compatibility: spreads maintenance jobs and adds optional caches for follower lookups, raycasts, directions, giver discovery and dialogue updates. External Talk/Quests DLLs remain unchanged.

No new configuration entries or default changes are introduced. Optional Talk, Quests, ChaseCamera and Engine Sim remain separate installs. Extract the ZIP into BepInEx/plugins; existing RC18 assets are retained.

Test after restarting:

1. Confirm `Loading [Wild Wasteland 0.1.22]` and the expected module startup summary without job errors or patch failures.
2. While seated, press HEADLIGHTS repeatedly in both camera views. Click the dashboard switch, then press the key again. Each action should toggle the lights once; repeat after rebinding HEADLIGHTS.
3. In first person, scroll with empty hands, melee and a lowered gun: no extra zoom. Raise the gun's sights fully and scroll: zoom should work. Lower sights, reload, holster or switch to melee: extra zoom should stop. Repeat with hold/toggle aiming and a scoped weapon. With general VerboseLog enabled, accepted scrolls emit `[Guns] Ironsight zoom target:`.
4. Open/close the map and ESC/settings menus, check compass markers, quest tracker/journal and dialogue, then save/load. The map should render when opened and stop its camera while closed.
5. Compare the same S1-S5 scenarios against RC18 using the Performance page and PlayerBody Perf lines, especially a camp fight's Brain/Idle/Nav/Senses worst times and GC counts. Check navigation exits, cover, followers, traffic, merchant parking, projectiles and War Rig recovery. Performance acceptance remains pending these in-game measurements.

Validation: Windows Release build with Deploy=true passed with zero errors and eight warnings. Built, deployed and packaged DLL MD5: FB582EC2684915B084C4F299E6CC6559. The archive retains all 172 unchanged RC18 asset entries. Archive SHA256: 7f9907753d219b8d4fe1d81eb80b15314ebef35958395821178ed59f03a817d4. Agents do not run the game.
