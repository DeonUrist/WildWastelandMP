# Wild Wasteland 0.1.13 RC10

Testing release for #127: optional Engine Sim 1.3.0 compatibility. In-game and performance verification remain pending with Denis.

**Engine Sim is optional. Wild Wasteland works normally without it; the bridge stays idle. Install it separately only if you want Engine Sim's features.**

## Engine Sim compatibility

- Install [Engine Sim 1.3.0](https://github.com/Wad67/Apocalypter-enginesim/releases/tag/v1.3.0) separately, including its native library and impulse responses. It is not bundled or replaced.
- WW schedules the original manager Update/FixedUpdate once in their corresponding phases. Native simulation/audio threads, driveline/vehicle-part physics, engine selection, settings and saved data stay with Engine Sim.
- Installation/scene-boundary seeds plus vehicle/sound enable callbacks maintain an engine/listener cache. Newly enabled vehicle subtrees use 500 microsecond slices; no repeated whole-scene scan runs during ordinary play. Original source scans still select and reconcile engines every 0.5 seconds.
- With experimental simulated drivetrain on, the existing Ignition key (including alternate binding) follows the source click/hold/release behavior: short click toggles the key, hold past 0.25 s cranks. Dashboard mouse use remains. Existing vehicle hints and CleanHUD's engine-status icon reflect the coupled engine.
- Patrols' startup, restart, running checks and stops go through the simulated switch while coupled, retaining fuel/load/condition effects. Player takeover cancels pending AI cranking.
- Version/interface checks roll back unsupported hooks; manager/cache exceptions restore standalone callbacks. The compatibility module can be disabled without stopping Engine Sim's own sessions.
- New config: only `[EngineSimCompat] Enabled=true` in WW settings.cfg. Engine Sim keeps its own config and Apocasetter page. No new save data.

## Other pending change included at Denis's request

CleanHUD title/pause menus use LoadingScreens/BG_MAIN.png with aspect-preserving cropping. The texture is cached across scenes and released on module shutdown; numbered loading-screen selection remains unchanged. The image is included in this archive.

Pending CleanHUD updates are also included: ChaseCamera telemetry can be captured in first and third person, with a ChaseCam HUD switch in WW's HUD settings; the source instruments stay hidden while the replacement is visible. Patrol and location compass controls now offer Off / Ambiguous / On, with ambiguous markers shown as white question marks. These changes add the corresponding ambiguity settings and retain the original source ChaseCamera HUD setting.

## Installation and validation

Unzip into BepInEx/plugins, preserving the WildWasteland folder. Existing assets from RC9 are retained. Optional Engine Sim and ChaseCamera must be installed separately.

Windows verification: `dotnet build WildWasteland.csproj -c Release -p:Deploy=true` succeeded with zero errors (the initial full build reported seven existing warnings; the final incremental build reported none). Built and deployed DLL MD5: `5F067B190CC23B52ACEDD3DEA9278BC5`. Agents do not run the game; Engine Sim was not installed during compilation. This release does not mark #127 Verified or close it. Native worker CPU and unsliced source operations are outside WW's scheduler counters; no measured performance gain is claimed.

In game, test audio with simulated drivetrain off, then enable it and test click/hold ignition using keyboard/mouse and alternate bindings, first/third person, pause/focus, cold starts/stalls, patrol/merchant startup and stops, engine/exhaust swaps, range changes, save/load and compatibility off/on. Expected WW log: `[EngineSimCompat] Engine Sim 1.3.0 scheduled; 500 us world cache, player/crew ignition bridge ready; native simulation and audio threads retained`; Engine Sim still logs `Simulating ...`. No failed hooks, fallback warnings, conflicts or disabled jobs should appear. Full steps: notes/compat-EngineSim.md.
