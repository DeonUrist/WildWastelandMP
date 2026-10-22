# Plan — `Performance` module: make vanilla Apocalypter run much faster

Status: **first pass implemented, verification incomplete** (`a8f189f`, 0.1.26, 2026-10-15). This document retains the original design/estimates from 0.1.24 RC21 `a3b44a9`; it is not an implementation checklist. Current mechanisms, runtime restrictions and evidence are in `notes/perf-handover.md` and `notes/perf-preparation.md`. Owner: Denis decides scope/order; agents implement one batch per issue (AGENTS.md §0.1).

Follow-up: Denis authorized copied-save headless verification and the Patrols build queue/car snapshots/NPC rig queue and registration caches. Baseline i reached native save/title/reload; sampled FS4 source/reflection parity passed, while PM2 found 60 lifecycle receiver mismatches. The separately approved registry timing fix is in FsmRegistry.cs. Current run results/build hashes are recorded in docs/perf-log.md; no plan timing estimate is promoted to a measured FPS gain.
Scope: the **vanilla game** (PlayMaker runtime, game FSMs, rendering, physics, MapMagic, NWH, GC). WW's own code was already audited in `notes/perf-audit-2026-10-15.md` and implemented (docs/perf-log.md "Perf pass 2026-10-15") — not repeated here.
Evidence: static analysis of the full offline decompile `V:\Repos\Apocalypter\Sources\` (code + all 8129 FSMs + scene/prefab data). Detailed research with file:line citations:
- `notes/perf-research/A-playmaker-runtime.md` — PlayMaker runtime (event dispatch, lifecycle, reflection, idle FSM cost, FSM sleep design).
- `notes/perf-research/B-fsm-workload.md` — game FSM content (census, polling, culling sweeps, hot actions, candidates C1–C15).
- `notes/perf-research/C-engine.md` — rendering, lights, shadows, LOD/culling, terrain, post-FX, physics, NWH, MapMagic, GC, presets.
**No number in this plan is measured.** Every ms figure is an estimate to rank work; Phase 0 exists to replace them with measurements.

## 1. Where the time goes (static diagnosis)

| # | Cost centre | Evidence (see research) | Est. cost | Kind |
|---|---|---|---|---|
| 1 | **Shadowed point lights** — 169/178 prefab point lights cast shadows (6 cube faces each), incl. 0.15 m gauge lights, 2 m fire/muzzle lights, 200 m headlight spots; Forward + Ultra pixelLightCount 20 adds a pass per lit object per light | C §0–1 | 2–8 ms GPU + 1–4 ms CPU in camps (Camp_7: up to 114 shadow passes) | GPU/CPU steady |
| 2 | **Idle FSM tick** — ~3–4k live FSMs, ~70% idle, each still runs `PlayMakerFSM.Update → Fsm.Update` (+ Unity's native Update call) | A §4, B §0–1 | 1–2.5 ms CPU steady | CPU steady |
| 3 | **Targeted SendEvent scans all FSMs** — 4471 SendEvent(GameObject/GameObjectFSM) copy + scan `PlayMakerFSM.FsmList`; sendToChildren walks every FSM's parent chain | A §1.1, B §3.6 | 0.1–1.5 ms per send; POI entry 20–60 ms; save/load 1–5 s; 32 KB garbage per send | CPU spikes + GC |
| 4 | **Culling sweeps not sliced, phase-locked** — 4 GameManager/NewGO_ArrayList sweeps walk whole lists in one frame, aligned every 10 s; fsmList.Remove is O(N) per disabled FSM; >10k entries silently disables culling | B §3.2, §3.8; A §1.6 | 10–40 ms spike every 5–10 s | CPU spikes |
| 5 | **Cars never culled** — every car within 5 km runs ~80 per-frame actions (reflection, 2 raycasts), NWH fully simulated to 2000 m, wheels never sleep | B §1.2–1.3, C §0 | 0.2–0.4 ms/car FSM + 0.1–0.3 ms/car physics | CPU steady |
| 6 | **Render culling/LOD** — 10 km far plane, 3×3 tiles of 5 km, 60 LODGroups for 11k renderers, 11k shadow casters (51% under 1 m), lodBias 2, sun shadows 200 m × 4 cascades @VeryHigh | C §1, §2.3–2.4 | 2–6 ms CPU culling/draw + 1–3 ms GPU | CPU/GPU steady |
| 7 | **Dead post-FX** — PPU ScreenSpaceShadows at intensity 0 (3 blits + 2 RTs + Shader.Find/frame), TAA with zero blending (cost, no AA), Sharpen unused RT | C §0 | 0.5–1.5 ms GPU | GPU steady |
| 8 | **GC** — ~1 full GC / 10 s on a ~400 MB heap (WW log); sources: MapMagic matrices, PlayMaker event lists/EventData, FsmProperty boxing (346 per-frame Set/GetProperty), Azure WeatherController reflection (~2200 boxes/s), HUD strings, FloatingOrigin FindObjectsOfType | A §1, B §3.3–3.5, C Tier 3 | stop-the-world pauses (tens of ms) | spikes |
| 9 | **MapMagic tile crossing** — cores−1 generation threads, 500 objects instantiated per frame (each with FSMs → reflective action loading 3–6 ms per vehicle-sized prefab) | C §0, §2.6; A §1.6 | 20–200 ms spikes | spikes |
| 10 | **Polling content** — vehicle hinge child-count polls (~12k exec/s), Pedal/RpmGear/Handbrake reflection, LightFlicker ×60, 22 camera MousePick pairs with 13 masks (~40 raycasts/frame), DayTime polling Azure by reflection every frame (bug), Coyote BroadcastAll on every hit, HUD FSMs building ~20 strings/frame | B §2–3 | 0.5–1.5 ms + spikes | CPU |
| 11 | **PlayMakerGUI.OnGUI** walks all FSMs on every IMGUI event; 47 useless PlayMakerOnGUI components; BecameVisible/Invisible events nobody handles | A §1.7 | 0.3–2 ms | CPU steady |
| 12 | Smaller: physics maxDeltaTime 0.333 (16-step spiral), 105 prewarmed particle systems per POI spawn, duplicate Rigidbody AddComponent errors on every part (re)activation, Player capsule height written by reflection every frame, terrain pixelError 1 | B, C | small–medium | mixed |

Expected total (Balanced preset, all phases): **CPU main thread −5 to −12 ms/frame in camps/driving, GPU −3 to −10 ms in POIs, 10–200 ms spikes reduced to < 5 ms**, fewer full GCs. Verify everything.

## 2. Architecture

New WW module **`Performance`** ("Performance & optimization"), `src/Modules/Performance/`, namespace `WildWasteland.Modules.Performance`, `DefaultEnabled = true` once Phase 1 is verified (false until then). Follows AGENTS.md §5 exactly:
- Frameworks (subfolders): `Telemetry/` (Phase 0), `PlayMaker/` (runtime fixes: Registry, EventDispatch, FsmSleep, Property, Loading, Gui), `Content/` (FSM rewrites and C# replacements: Culler, Vehicles, Hinges, Lights, Hud), `Render/` (lights, shadows, LOD/culling, post-FX, terrain, particles, presets), `Physics/` (NWH LOD, CCD, origin), `World/` (MapMagic).
- **Every optimization is a switch with three modes `Off / Shadow / On`** in tools.cfg (`Performance / <Area>`), plus a player-facing preset in settings.cfg: `[Performance] Preset = Vanilla | Low | Balanced | Max` (+ per-knob overrides on F7 for A/B tests). Shadow = run vanilla for real, compute the new result without side effects, compare, count mismatches (F7 page + Log.Warn first 5 with GameObject path, FSM, state, event). A change goes `On` by default only after Denis has played with zero shadow mismatches.
- **Shared Harmony targets via `Core.Hooks`** (§5.6): WW already dispatches `Fsm.ProcessEvent`, `Fsm.EnterState`, `SendEvent.OnEnter/OnUpdate`, `GetButtonDown.OnUpdate`, `SetFsmFloat.OnEnter`, `ActivateGameObject.OnEnter`, `CreateObject.OnEnter`, … New runtime targets owned only by this module (`Fsm.SendEventToFsmOnGameObject`, `Fsm.BroadcastEventToGameObject`, `Fsm.BroadcastEvent`, `PlayMakerFSM.OnEnable/OnDisable/OnDestroy`, `Fsm.Update`, `PlayMakerGUI.OnGUI`, `FsmProperty.GetValue/SetValue`, `ActionData.LoadActions`, `ActionHelpers.DoMousePick`) use `Core.Patches`; any that a second module (or ApocaFsmInspector, Apocapocket, ApocalypterAI, Engine Sim) also patches moves to Hooks. List each in `docs/harmony.md` with call frequency and early-out.
- **Content edits** are applied from one postfix on `PlayMakerFSM.Awake`/`Start` keyed by (FSM name, owner-root prefab name, state, action index + action type check). Every edit records the original (action Enabled, field values, Wait times, FSM enabled) and Shutdown restores it. An edit whose target doesn't match the expected action type logs once and is skipped (game update safety). Data-driven table `Content/Edits.cs`, one row per edit with a citation into Sources.
- **Settings edits** (Quality, Physics, Time, cameras, lights, terrain) are applied on scene load / world ready / `TerrainTile.OnTileApplied` / after the graphics menu (`GraphicSettingHelperMethods.ChangeQualitySettings` postfix) / after the `SetClippingPlanes` FSM; originals stored and restored in Shutdown. Prefab-asset edits (lights, particles, LOD injection) go through a "prefab pass" over `Resources.FindObjectsOfTypeAll` once per scene load (sliced, ≤1 ms/frame).
- Budget for the module itself: ≤ 0.3 ms/frame average, zero steady-state allocation (docs/perf-log.md rules); all scans `Scheduler.Sliced`.
- Compatibility: other mods that iterate `PlayMakerFSM.FsmList` keep working (the list is untouched; the registry is a side index). Mods that mutate FSM actions/delayed events directly must call `Services.Performance.WakeFsm(fsm)` (new service) or the FSM sleep falls back safely (§4.3 wake list). ApocaFsmInspector, Apocapocket (sets RestartOnEnable=false on custody items), ApocalypterAI and Engine Sim are named regression targets.

## 3. Phase 0 — measure first (prerequisite for everything)
Issue P0 (Telemetry), DefaultEnabled tools only, no game change.
1. **Frame telemetry** (`Telemetry/Frames.cs`): unscaled-dt ring buffer → avg, p95, p99, frames > 33 ms tagged with context (tile generating, origin jump, save/load, culling sweep frame, POI spawn). `ProfilerRecorder` for Draw Calls, SetPass, Batches, Shadow Casters, Visible Skinned Meshes, GC Used/Reserved Memory, GC Allocated In Frame (check `.Valid` — some are development-only); `GC.CollectionCount(0)` per minute; `FrameTimingManager` GPU time if available.
2. **FSM census** (`Telemetry/FsmCensus.cs`, every 5 s, sliced): `FsmList.Count`; per (FSM name, active state): count, Σ ActiveActions, sleep-eligible count; SendEvent/s by target type; BroadcastEvent count; fsmList.Remove calls and average scan length; actions loaded per Instantiate; PlayMakerGUI.OnGUI calls/frame; GetGameObjectFsm misses; MousePick raycasts/frame. Sampled timing: Stopwatch around `PlayMakerFSM.Update` per FSM name 1 frame in 30.
3. **Scene census** (F7 page): live lights (shadowed/total, by type), shadow casters, renderers by layer, Rigidbodies awake/asleep, wheels simulating, particle systems playing, cars by distance band, ArrayList_Items/Cars sizes, MapMagic threads and queue.
4. **Report**: F7 "Performance census" page + `BepInEx\config\WildWasteland\Performance\census-<time>.txt` dump button.
5. **Scenarios** (add to `verification/README.md` D as P1–P6, same save, quality Ultra, vsync off, 30 s each): P1 open desert day on foot; P2 Camp_7 at night; P3 100 km/h across a tile boundary incl. origin jump; P4 ≥3 parked cars + player car within 300 m; P5 camp fight 6+ NPCs; P6 save + load on a large world (time both). Plus GPU-vs-CPU check (half resolution).
6. Optional ground truth: development player (copy `win64_development_mono` UnityPlayer.dll/WindowsPlayer.exe from Unity 2020.3.49f1 + `player-connection-debug=1`) for Profiler/Frame Debugger captures. Never shipped.
**Exit:** baseline table in docs/perf-log.md; the ranking in §1 confirmed or reordered by data.

## 4. Phase 1 — exact PlayMaker runtime fixes (CPU spikes + steady, zero behaviour change)
All are semantically exact replacements of PlayMaker internals (research A §2 has the equivalence arguments). Each ships with Shadow mode.
| Id | What | Target | Est. gain | Risk |
|---|---|---|---|---|
| PM1 | **FSM registry**: stamp + per-GameObject list of enabled FSMs, maintained in OnEnable/OnDisable/OnDestroy; preserves fsmList order | PMF.OnEnable postfix, OnDisable/OnDestroy prefixes | base for PM2–PM4 | low |
| PM2 | **O(k) targeted events**: `SendEventToFsmOnGameObject` and `BroadcastEventToGameObject` resolved via registry (children: `GetComponentsInChildren(true)` filtered + stamp-sorted); keep EventData copy/restore, excludeSelf, snapshot semantics, pooled arrays (re-entrant stack) | Fsm methods (prefix false) | 0.1–1.5 ms → µs per send; removes POI/save/load stalls and ~32 KB/send garbage | low |
| PM3 | **Fast fsmList removal**: reference-compare + RemoveAt, skip the guaranteed-miss OnDestroy scan | transpile PMF.OnDisable/OnDestroy | 10–25× per disable; vehicle destroy ~20 ms → ~1 ms | very low |
| PM4 | **BroadcastEvent** pooled snapshot (keep per-target EventData) | Fsm.BroadcastEvent, PMF.BroadcastEvent | less garbage on 13 senders | low |
| PM5 | **GUI loop**: PlayMakerGUI.OnGUI iterates only FSMs with an OnGUI-overriding active action; disable the 47 PlayMakerOnGUI components whose FSMs only contain ActivateGameObjects; skip unhandled BecameVisible/Invisible events | PlayMakerGUI.OnGUI transpile, PMF visibility prefixes | 0.3–2 ms/frame | low |
| PM6 | **Trigger proxies**: lazy TriggerName, skip unhandled system trigger events | Fsm.OnTriggerEnter/Exit… | allocation per trigger callback | low |
| PM7 | **Shared camera pick**: one RaycastAll per frame with the union mask, each (mask, distance) query answered from it | ActionHelpers.DoMousePick | ~40 → 1 raycast/frame | low |
| PM8 | **GetButtonDown/Up/GetButton key lookup**: name→key dictionary invalidated on rebinding (coordinate with WW's existing GetButtonDown dispatcher + Apocasetter InputBlocker) | via Core.Hooks | small, 665 FSMs | low |
| PM9 | **Get/SetFsm* lookup**: non-allocating GetComponents, warning rate-limited (merge with Core `QuietFsm`), per-action resolved-variable cache (never cache misses) | ActionHelpers.GetGameObjectFsm, actions | small steady + GC | low |
| PM10 | **ConvertFloatToString/ConvertIntToString**: reuse the previous string when value+format unchanged | action OnUpdate prefix | GC (191 everyFrame) | very low |
**Exit:** census shows SendEvent cost gone, P6 save/load time and P3 spike reduced; zero shadow mismatches over a long session.

## 5. Phase 2 — engine quick wins (GPU/CPU steady, near-zero visual change)
Preset-driven (C §3 table is the source of values). Re-apply after the graphics menu and SetClippingPlanes.
| Id | What | Est. gain | Risk |
|---|---|---|---|
| EN1 | **Light shadow policy**: prefab pass turns off shadows on all point lights and spots < 1 m; headlights 60–120 m (shadow only on the player's car, optional); LightBudget job (0.25 s) gives shadows to the N nearest qualifying lights (Balanced 2 ≤ 25 m @256 px); distance-cull lights (and their LightFlicker FSM) beyond k×range; pixelLightCount 6 | 2–8 ms GPU, 1–4 ms CPU in POIs | low (visual: fewer soft lamp shadows) |
| EN2 | **Dead post-FX**: disable PPU ScreenSpaceShadows when intensity ≤ 0.001; Sharpen without the unused RT + cached Shader.Find; TAA (zero blending) → SMAA (or real TAA blends) on PlayerCamera + 14 vehicle cameras; stopNaN off | 0.5–1.5 ms GPU; better image | very low |
| EN3 | **Sun shadows**: distance 120, 2 cascades, High; per-layer shadow cull distances (Item 30, Food 20, Weapon 20, Hinge 40, Actor 60); skinWeights 2 | 1–3 ms | low |
| EN4 | **Terrain**: pixelError 5, baseMapDist 600, detail 70 m / 0.8 via `terrainSettings` + `ApplyTerrainSettings()` | 0.5–2 ms GPU | low |
| EN5 | **Particles**: prewarm off (105), Pause culling for looping systems without sub-emitters, collision quality Low, lights module off | POI spawn hitch, 0.2–1 ms | low |
| EN6 | **Time/physics**: maximumDeltaTime 0.1; Physics.sleepThreshold 0.14 from load (vanilla gets it after the first origin jump anyway); FloatingOrigin jump with cached lists | no 16-step spiral; origin hitch | low |
| EN7 | **Azure**: WeatherController.FixedUpdate at 5 Hz, TimeController.FixedUpdate every 2nd step | GC ~60–100 KB/s | low |
| EN8 | **GC slice** tuning per preset (incremental already on via boot.config `gc-max-time-slice=3`) | pause smoothing | very low |
**Exit:** P2 (Camp_7 night) GPU/CPU numbers; visual comparison screenshots per preset.

## 6. Phase 3 — FSM sleep and reflection removal (biggest steady CPU win)
| Id | What | Est. gain | Risk |
|---|---|---|---|
| FS1 | **FSM sleep** (research A §4.2): set the FSM's own `ManualUpdate` when settled (started, active state entered, no ActiveActions needing OnUpdate, no delayed events, finished/blocked), wake on ProcessEvent, Event, SwitchState/SetState/Start/OnEnable/Stop, DelayedEvent (sender), FsmStateAction.Finish, physics/animator/GUI callbacks, scene load, `Services.Performance.WakeFsm`. Clear serialized ManualUpdate on Awake/OnAfterDeserialize. Shadow mode asserts "nothing changed" for would-sleep FSMs. WW's ProcessEvent/EnterState dispatchers run before wake — they must not depend on Update having run. | 1–2.5 ms/frame (managed part of idle ticks) | medium (needs long shadow run) |
| FS2 | **Compiled FsmProperty** (Get/SetProperty 4142 uses, 346 every frame; VehicleController/WheelController/Rigidbody/Joint): cached member chain + compiled getter/setter (DynamicMethod), typed float/bool/int/enum, same struct write-back | 0.2–0.5 ms + GC | low (shadow compare) |
| FS3 | **CallMethod** compiled delegates (107) | small + GC | low |
| FS4 | **Faster action loading on Instantiate**: compiled constructors + field setters per action type, skip empty byteData copy | 3–6 ms → ~1 ms per vehicle spawn; POI spawns | low (exact values) |
| FS5 | **Remove Unity's native Update for sleeping FSMs** — research only (Update-less PlayMakerFSM subclass at prefab level); do not start before FS1 is measured | 0.5–1.5 ms | high |
**Exit:** census: sleeping share ≥ 50% of live FSMs; P1/P4 FSM-layer time ≤ 1 ms.

## 7. Phase 4 — rewrite hot game content (FSM edits and C# replacements)
Every row cites the FSM in Sources (research B §2–4). Each is an `Edits.cs` row or a small C# component; vanilla FSM disabled only where replaced 1:1 (same variables written, same events sent).
| Id | What | Est. gain | Risk |
|---|---|---|---|
| CT1 | **Sliced, de-phased culler** replacing GM `DeactivateFarAwayItems`/`DeleteFarAwayItems`/`DeleteFallingItems` + NewGO `DeleteDestroyedItems`: 64 entries/frame, sqrMagnitude, 950–1050 m hysteresis, ≤ 8 SetActive/frame, correct null compaction, honours "perma base", no 10k loop cap, same lists/rules (layer 22 exempt, 5 km destroy, y ≤ −10000) | 10–40 ms spikes → < 1 ms/frame; fixes silent culling stop | low–medium |
| CT2 | **Car simulation LOD**: beyond 50 m disable the per-car "presentation" FSM set (Pedal, HingeFollow, gearChange, RpmGear, Handbrake, Start, TireWear, Speed, CrashDamage, FuelConsumption off-state, Shake); DistanceKinematic 2000 → 250 m; while kinematic & not driven `WheelController.AutoSimulate=false` + VehicleController disabled; NWH StateSettings LOD 15/40 m; never the player's car/trailer pairs; re-enable before isKinematic=false. Later option: SetActive(false) beyond 800 m (re-activate all before save, like vanilla items) | 0.3–0.7 ms per remote car | medium |
| CT3 | **Event-driven hinges**: a component on each hinge using `OnTransformChildrenChanged` sends the same next/back events; disable the per-frame child-count polls (vehPart_Attach partAttached, checkWheel, getEngine, getRadiator, GetChild, BumperCheck, WindshieldType, checkSuspension, HingeAdjustVisible, Shake child checks) | ~35 actions/frame/car | medium |
| CT4 | **Vehicle reflection FSMs → C#**: Pedal/RpmGear/Handbrake read/write NWH fields directly (player's car every frame, others 5 Hz or off by CT2) | 0.1–0.2 ms + GC | low |
| CT5 | **Throttle "wait until" polls** (CheckTag tag watch, Condition/LiquidAmount clamps, AirCooling/ExhaustSmoke idle, Sell board, NPC Sound3/Detection notDetected …) via per-action throttle; never states with perSecond accumulation | 0.2–0.5 ms | low |
| CT6 | **LightFlicker manager** (C#, 20 Hz, ≤ 200 m) replacing ~60 per-frame FSMs | 0.1–0.2 ms | very low |
| CT7 | **HUD/GameManager FSMs**: Ammo/UI_Manager/ArmorBackpack only while PlayerSheet open (else 2 Hz), strings only on change; **DayTime** reimplemented in C# at the intended 0.2 s (vanilla bug: polls Azure by reflection every frame) | 0.1–0.3 ms + ~1 KB/frame GC | low |
| CT8 | **Coyote broadcast storm**: FactionRelation_Coyotes re-broadcasts BroadcastAll on every hit → broadcast only on friendly→enemy, hits only reset the 600 s timer | 1–3 ms per hit in coyote fights | low |
| CT9 | **Duplicate Rigidbody AddComponent** on part (re)activation (CheckTag) → return existing; RestartOnEnable=false on the ~460 pure-data item FSMs (ID, ItemName, Value, imageUI, weaponType…) | log/error spam + reactivation storms | low |
| CT10 | **Player capsule height** written by reflection every frame (Movement Standing) → only while tweening | small + physics capsule rebuild | low |
| CT11 | **Hidden option canvases**: 12 UiToggleGetIsOn polls only while their canvas is open | small | very low |
| CT12 | **Save/load path**: `SaveGameActivateItems` re-activation storm + per-item sendToChildren (PM2 fixes the scan); ES3 actions using File location re-parse the save per call (398 actions) → route through one cached ES3File per save/load (coordinate with Apocasaver and SaveBus) | seconds on large worlds | medium (save integrity: separate issue, backup saves) |
**Exit:** P1/P4/P5 FSM-layer time; long session (1 h) without culling regressions; attach/detach/drive/refuel/trade/save/load regression list (verification README) passes.

## 8. Phase 5 — render culling and world streaming
| Id | What | Est. gain | Risk |
|---|---|---|---|
| RC1 | **Small-caster shadows off** (world half-extent < 0.15 m Balanced / 0.35 m Low; exclude car frames) | 1–3 ms | low |
| RC2 | **Injected cull-only LODGroups** on prefab roots without one (screen height by size), lodBias = view-distance slider (1.2 Balanced) | 2–6 ms CPU | medium (pop-in; skip vehicles/weapons/player rig/nested LOD) |
| RC3 | **Camera layerCullDistances** (spherical): Item 150, Food 80, Weapon 150, Hinge 300, Actor 600, Car 2500 | 0.5–2 ms | low |
| RC4 | **Far plane / fog** in Low preset (6000/5500), applied after SetClippingPlanes | GPU/CPU in open desert | low |
| RC5 | **MapMagic**: maxThreads clamp(cores/4,1,3) at BelowNormal priority, objectsNumPerFrame 500 → 40, apply budget 2 ms; never mainRange 0 | removes 20–200 ms tile spikes, lower peak heap | low |
| RC6 | **Animator/IK culling**: NPC root Animators CullUpdateTransforms; FastIK skipped when invisible or > 40 m | 0.2–1 ms | low–medium (bone-mounted sensors) |
| RC7 | **Physics CCD sweep**: resting/slow items Continuous → ContinuousSpeculative, Continuous only > 8 m/s | 0.3–1 ms | low |
| RC8 | Textures (Low preset): masterTextureLimit 1, aniso Enable | VRAM | very low |
**Exit:** P1/P3 CPU and draw-call/shadow-caster counters; visual review of pop-in per preset.

## 9. Phase 6 — experiments (only after 0–5 are measured)
- FS5 native-Update removal for sleeping FSMs.
- Deferred rendering path trial (many lights become cheap; check AzureFogScattering, transparent materials, MSAA/AA, PPU effects) — high risk, big potential at night/POIs.
- PlaySound pooling (PlayClipAtPoint creates/destroys a GameObject per call; 1070 uses).
- GetChild allocation-free rewrite + validated cache (3479 uses; runs on every re-enable).
- Item SetActive culling replaced by "FSM disable + renderer/collider off" (keeps SaveGame semantics; avoids RestartOnEnable re-init) — needs saves analysis.
- Job-system/Burst not available (Mono, 2020.3 player without Burst assemblies) — do not plan on it.

## 10. Order of work (issues, one per row, AGENTS.md §0.1)
1. P0 Telemetry + census + scenarios (no game change). **Denis captures baselines.**
2. PM1+PM2+PM3 (registry, O(k) events, fast removal) — Shadow first, then On.
3. EN1+EN2 (lights, dead post-FX) — biggest GPU win, trivially reversible.
4. CT1 (sliced culler) + CT8 (coyote) + CT9 (AddComponent) — spike killers.
5. FS1 FSM sleep (Shadow for at least one long session, then On).
6. EN3–EN8, RC5 (shadows, terrain, particles, time, Azure, MapMagic).
7. CT2–CT4 (vehicles), FS2–FS4 (reflection, loading).
8. PM5–PM10, CT5–CT7, CT10–CT11.
9. RC1–RC4, RC6–RC8 (culling/LOD) with visual review.
10. CT12 (save path) as its own carefully-tested issue; Phase 6 experiments.
After each: docs/perf-log.md entry (template), docs/harmony.md targets, docs/modules.md row, wiki page (docs/WIKI-FORMAT.md), F7 page listing every switch with its mode and shadow-mismatch count.

## 11. Acceptance targets (Balanced preset, Ultra base quality, same saves)
- FSM layer (census): ≤ 1 ms/frame on foot (P1), ≤ 1.5 ms in fights (P5).
- No frame > 16 ms caused by culling, POI spawn, origin jump or tile crossing (P3) on Denis's PC; save + load time halved (P6).
- Steady-state managed allocation: < 1 KB/frame from vanilla paths covered by this module; full GC count per 10 min halved.
- GPU: Camp_7 at night (P2) ≥ 30% faster.
- Zero shadow mismatches for every switch before it defaults On; every switch restorable to vanilla at runtime (module off → vanilla behaviour, verified by census).

## 12. Risks and rules
- PlayMaker internals are patched game-wide: every prefix starts with a cheap filter, no allocation (§5.6). Patches survive only for this PlayMaker build — verify method bodies (IL hash) at Init and fall back to vanilla on mismatch after a game update.
- Content edits are keyed and type-checked; a game update that changes an FSM disables that edit, never mis-applies it.
- Prefab-asset edits are visible to other mods and to later instances (ES3 restores) — restore on Shutdown.
- Saves: never add components to saved objects; never change what is saved; CT12 is isolated and needs save backups.
- Interactions to retest each phase: WW NPC (ProcessEvent/EnterState dispatchers, Senses), Patrols (vehicles, CT2), PlayerBody (camera, ADS), CleanHUD (HUD FSMs CT7), Apocapocket (RestartOnEnable guards), Apocasaver, ApocaFsmInspector, Engine Sim (vehicle START), ApocalypterAI, Talk/Quests (FsmList users).
