# Frame-time / framerate-coupling audit — 2026-10-15 (0.1.19 RC16 + uncommitted tree)

This is a read-only static audit across all modules. Six parallel passes covered NPC Brain/Detection, NPC Nav/Animals/Hazards/Women, QuestsCompat with the decompiled Talk/Quests source, Patrols, Core with PlayerBody, and CleanHUD/Guns/HUD/POI/EngineSim. Nothing was changed and nothing was run.

Items marked **[verified]** were re-checked in code by the coordinator. The rest are the sub-audits' reading of the code and are not measured. Line numbers are approximate (±3).

Runtime evidence available: `LogOutput.log` PlayerBody Perf lines show the following:

- 75 fps cap.
- WW's PlayerBody cost is about 0.55 ms/frame.
- About one full GC (Boehm, stop-the-world) every 10 s, with a heap of about 400 MB.
- `cameras: PlayerCamera MapCheat_Camera(rt 1024x1024)` appears in every window.

Earlier NPC measurements:

| Job | Worst frame |
|---|---|
| Brain | 61.9 ms |
| Nav | 18.6 ms |
| Senses | 9.6 ms |
| Idle | suspected of a 268 ms spike |

Process note: AGENTS.md §0.1 bans perf tweaks inside issue fixes. Each batch below should therefore be its own issue: one module, ≤ ~50 lines, measured on the F7 Performance page before and after.

---

## Tier 1 — biggest expected wins, low risk, small changes

### T1. Native map camera renders its 1024² RT every frame while the map is closed (CleanHUD/QuestMap)

**Evidence:**
- `Perf.Write` lists only `isActiveAndEnabled` cameras, and `MapCheat_Camera` is listed in every window, including the first one after load.
- `QuestMap.cs:49` finds it with `GameObject.Find`, which only finds active objects.
- `QuestMap.ApplyEnabled` toggles only the MapCheat FSM, never the camera. So the camera keeps rendering even with Map = Off.

**Fix:**
- Every frame, set `_camera.enabled = MapEnabled && fsm enabled && state == "on"`.
- Call `_camera.Render()` once on the open edge.
- Restore the original value on shutdown and scene load.
- `WorldToViewportPoint` keeps working on a disabled camera, so QuestMap, MapFog and TrafficMap are unaffected.

**Check first:**
- Whether the game's FSM toggles `camera.enabled` itself.
- That no other UI samples the RT.

**Benefit:** one full extra scene render per frame, on CPU culling, the render thread and the GPU. Possibly the single largest win in the whole pass. About 20 lines.

### T2. NPC thinks bunch into the same frame after an alert **[verified]**

**Where:** `Brain.cs:222-224, 1672` and `Idle.cs:188, 228-261, 377`.

**Cause:**
- `NextTick = now + interval` and `NextThink = now + 0.2f`.
- The stagger is applied only at creation, and is only 0–90 ms (Brain) or 0–180 ms (Idle).
- Disengaged NPCs don't advance their timer. When a shot alerts a camp, every overdue NPC thinks in that same frame.
- From then on they stay phase-locked. `InvestigateThink` (`+0.1f`) re-syncs the group again.

**Cost:** a Brain think is about 10–35 physics queries plus `Nav.Next`. K NPCs means K× that in one frame. This matches the 61.9 / 268 ms worst frames better than single routing calls do.

**Fix:**
- On (re)engage, set the next time to `now + Random.value * interval`.
- Use `next += interval`, clamped to `now`.
- Cap thinks per frame at `ceil(engaged * dt / interval) + 1`. Overflow NPCs think next frame.
- Rotation and pedal stay per frame.

**Benefit:** the average cost is unchanged, the worst frame drops by about K×. About 25 lines.

### T3. `ScaleWithActors` defaults to false **[verified]**

**Where:** `NpcModule.Config.cs:113` and `Brain.cs:188`. AGENTS.md describes load scaling as the design, but by default every NPC thinks at 0.1 s.

**Fix:** change the default (existing configs keep their value), or let Denis just turn it on in F3 Debug → Pathfinding now to test.

**Benefit:** 2–5× fewer Brain thinks in raids. One line.

### T4. Patrols camp/POI scene scan never stops and allocates on every node **[verified]**

**Where:** `Patrols/Traffic/TrafficLocations.cs:50-75`.

**What runs:** every frame, 200 transforms. The scan restarts 15 s after it finishes, which in a large world means it is effectively always running. Per node it does:
- `t.name` twice (2 string allocations);
- `RememberPoi`, which leads to `PrefabName`, which is `Regex.Replace` + `Trim` (`TemplateExporter.cs:208`);
- `ObserveRoot` (`GetComponents`) on roots.

**Fix:**
- Read the name once.
- Gate the regex behind a cheap first-character / `StartsWith` ordinal check.
- Don't descend into vehicles, NPCs or items (anything with a Rigidbody, VehicleController or FSM at its root).
- Rescan on MapMagic `OnTileApplied` or scene load (already observed elsewhere) instead of every 15 s.
- Use `Scheduler.Sliced` with a µs budget instead of a fixed 200.

**Benefit:** about 0.2–0.6 ms/frame, plus probably a large share of the steady garbage. About 30 lines.

### T5. `Nav.PickOutExit` runs to completion in one think (the 268 ms class)

**Where:** `Nav.cs:1002-1037`, from `OutNext`. One call does:
- a whole-area Dijkstra keyed to the NPC's exact cell (a walking NPC rarely hits the cache);
- a sort of about 26k keys;
- up to 6000 `OutClear` line walks.

**Fix (A, exact):** a per-structure `PickJob` resumed from the Nav job under about 1 ms/frame. Until it finishes, `OutNext` returns `handled=false` (the existing fallback path). One job at a time, de-duplicated by (structure, cell, 8 m goal bucket). About 60 lines.

**Fix (B, later):** one shared exit field per goal bucket instead of a pick per NPC. Bigger win, but the chosen exit is not bit-identical.

Combine either option with a per-frame routing budget (at most one field Build per frame).

### T6. CleanHUD compass forces canvas rebuilds every frame **[CH-1 verified]**

1. **CH-1 [verified]:** `Hud.cs:775, 793` call `SetAsLastSibling()` on every shown marker and on the caret every frame. That is a hierarchy change, which means a canvas re-sort and rebatch. The pool order already equals the sort order.
   - Fix: put the markers in their own container after the ticks, with the caret last; remove both calls.
2. **CH-2:** the HUD root has no nested `Canvas` (`Hud.cs:124-128`), so every compass, log or tracker change rebatches the game's whole root canvas, including the hidden vanilla HUD.
   - Fix: add a `Canvas` to `_root` and a second one on the compass group, with overrideSorting off so draw order is unchanged.
3. **CH-3:** compass tick and letter fades write `Graphic.color` every frame while turning (`Hud.cs:726, 736`). That regenerates the text meshes, including their Shadow and Outline.
   - Fix: use `canvasRenderer.SetAlpha()`.
4. **CH-6:** vanilla hide runs twice per frame (`Hud.cs:523` and `willRenderCanvases`).
   - Fix: drop the Tick call and set `cullTransparentMesh = true` on the hidden vanilla graphics so they aren't drawn at all.

**Size:** all small. **Benefit:** high while turning or driving.

### T7. Quests/Talk bridge (QuestsCompat), cheap fixes

1. **`Hire.NearestByName`** walks the full `PlayMakerFSM.FsmList` and reads `.name` on every Health FSM, every frame, for up to 60 s after a load or while the hire is unloaded. It is not in the cached-list transpiler set.
   - Fix: add it to the PeopleScan transpiler list (`QuestsCompatModule.cs:96`), plus an optional 4 Hz throttle.
   - Size: about one line. Benefit: about 0.3–1 ms/frame in that window.
2. **Quests `OnGUI`** runs on every IMGUI event (Layout, Repaint and every input event). It only uses `GUI.Label`/`DrawTexture`.
   - Fix: run it on `EventType.Repaint` only, and skip it entirely while CleanHUD owns the tracker/journal and there are no toasts.
3. **Directions `Aim` recompute** (every 4 s per active quest, inside the 0.5 s Tick, in one frame): `FindItems`/`FindEnemies` over every ItemName/People FSM, with `Util.Norm` StringBuilders per FSM.
   - Fix: a memo prefix on `Game.Matches` (per FSM id + token), at most one cache miss per Tick, and a longer TTL.
4. **`Takeover.GiverTick`** calls `Resources.FindObjectsOfTypeAll<GameObject>()` twice, about every 30 s, while a return-to-giver's giver is missing. That is a tens-of-ms hitch.
   - Fix: a memo of `InactiveExists`/`ScanForPrefab` with a long TTL.
5. **Split Quests `Tick` across frames:** gate `NeutralizeTick` (`FindPois` over all MapMagic tile children every 2 s once any camp has been taken), `GiverTick`, `SyncMapMarkers` and `ReputationTick` with prefixes. Drive each from its own Interval job with a random phase. Their internal timers keep behaviour unchanged.

---

## Tier 2 — decouple from framerate (per-frame work that only needs N Hz)

| # | Where | Now | Proposed | Risk |
|---|---|---|---|---|
| D1 | `NPC Idle.Drive` (`Idle.cs:563-584`) | 1–5 CapsuleCasts + `IsName("run")` per walker per frame, also on map routes | Feeler fan at 10 Hz per walker (or when `want` turns >15°); rotation/velocity per frame; `shortNameHash` compare | low (0.25–0.5 m at walk speed vs a 1.2 m feeler) |
| D2 | `Guns Tracers.Tick/Step` (`Tracers.cs:1149-1224`) | ray/sphere cast per bullet per frame, scaling with fps | fixed 50–60 Hz step with an accumulator; extrapolate the visual bolt per frame | low (≤1 tick hit timing; swept, so no tunnelling) |
| D3 | `Patrols PassengerGuard.CombatAllowed` (`Passenger.cs:72-79, 251-268`) | about 8 GetComponent + 2–3 list scans per seated shooter per frame | cache refs at Attach; result cached 0.25 s with random phase | low |
| D4 | `War Rig GroundRecovery` (FixedUpdate) | about 7 terrain raycasts, `Terrain.activeTerrains` array, `GetComponentsInChildren<Collider>` per physics step (5 Hz even when disabled) | 10 Hz enabled / 1–2 Hz disabled; cache terrains/colliders | low–medium (re-test fall-through rescue) |
| D5 | `Senses` 1 s maintenance (`Senses.cs:128-163`) | all agents + ghosts + spider-light item walk in one frame (the measured 9.6 ms) | round-robin 1/8 of agents per frame; don't clear `_prefabOf` at 4096 when the walk fills it | low |
| D6 | `Senses` look interval | `NextLook = now + interval + Stagger` adds stagger every look (bug: rates 0.15–0.26 s); NPC-vs-NPC O(n²) at full rate regardless of distance | stagger only at creation; ×2 beyond 80 m, ×4 beyond 150 m; per-frame look cap | low |
| D7 | `Idle` think rate | fixed 0.2 s regardless of camera distance or load | reuse Brain's `TickScale` + load factor; scale LegThink patience equally | low if patience is scaled |
| D8 | `Nav` discovery sweep (`Nav.cs:181-223`) | 250 nodes/frame for the whole session (most of Nav's 0.195 ms avg) | 15 s pause unless the player moved >30 m; `StartsWith` Ordinal | low |
| D9 | `NPC Women` discovery (`WomenFeature.cs:29`, `Gungirl.cs:214-249`) | whole-scene walk every 5 s, `.name` per transform | once on world ready, plus the existing spawn events; no descent into Rigidbody subtrees | low (check save restore) |
| D10 | `NPC DogAnimations.TickDog` | `SampleAnimation` + pose save/restore per dog per frame, Animator forced `AlwaysAnimate`; `DamageReady` uses `GetComponents` + LINQ every frame when the FSM is missing | cache target collider / Damage FSM; skip sampling when off-screen and >40 m | low–medium (pose pop at LOD edge) |
| D11 | `Patrols Merchant Park()` 2×/s | reflection `GetMethod`/`Invoke`, `GetComponentsInChildren<PlayMakerFSM>` over the whole bus, unguarded verbose concat | cache FSMs/methods; edge-trigger | low |
| D12 | `Patrols Nwh.SetInput` | reflection `SetValue` + boxing per pilot decision (10–20 Hz per car) | typed `NWH.VehiclePhysics2` access (MotorcycleBalance already compiles against it) | very low |
| D13 | `Patrols RiderGuard` / `WarRigUnit.GateAttacher` | per-frame `GetComponents<ConfigurableJoint>` + LINQ; `name.Split` per frame; `FindObjectsOfType<VehicleController>` 1 Hz | 2–4 Hz, cached verdicts, vehicle registry | low |
| D14 | `Patrols TrafficActor` (one Update per crew member) | O(N²) target search every 0.6 s each, `FindFsmInt("OnOff")` by name | one sliced Interval job over Actors | low |
| D15 | `PlayerBody ThirdPerson` aim raycast (`ThirdPerson.cs:300`) | 100 m ray per frame (part of the measured 0.15 ms PreCull) | every 2–3 frames, or reuse the crosshair eye-ray hit | low |
| D16 | `PlayerBody Equipment.Tick` | inventory read + 4–5 `Transform.Find` + unchanged renderer writes per frame | 10 Hz or on slot/drawn change; write on change | low |
| D17 | `CleanHUD` compass distance text | new string + Text (Shadow/Outline) rebuild whenever the metre value changes (about 20/s per marker while driving) | 5 Hz per view, quantized, pre-built string table | none (cosmetic) |
| D18 | Core `Runner.OnGUI` | IMGUI Layout+Repaint+events every frame for about 16 Gui jobs even when nothing draws | separate GUI MonoBehaviour enabled only when a Gui job wants to draw, or `useGUILayout=false` | low |

---

## Tier 3 — GC pressure (feeds the ~10 s full GC)

All of these allocate in steady state.

**PlayerBody (weapon drawn, every frame):**
- `GunPose.Own` builds `weapon+"|"+clip` up to 3× per clip.
- `GunPose.Abs` creates `new float[6]`.
- The `GunPose.Status` concat.
- `AimLift.Get` concats and `double[]` in `Elbow`/`D3`.
- `Game.DrawnWeapon` returns `Transform.name` 8–10×/frame.
- `Props.KindOf` calls `ToLowerInvariant`.
- `Loco.SetPoseList` key with enum boxing.
- `GetCurrentAnimatorClipInfo` array.
- `"mixamorig:"+bone` per mount.
- `_xhairWhat` debug string.
- Core `ApocasetterBridge.InputBlocked` uses `FieldInfo.GetValue` (boxing) and is called by Hotkeys every frame.

**NPC:**
- `WeaponOf`: `GetComponentsInChildren<Transform>` + name/`Substring` per bone, re-run on every `ActivateGameObject` of a rigged NPC (likely muzzle flashes).
  - Fix: cache the hand bones and filter the dirty flag by parent.
- `Senses.Shot`/`Noise` per NPC ray: sort lambda capturing `pos` + `new HashSet`; also a per-shooter 0.3–0.5 s throttle.
- `VanillaEvents.BeforePlayerShot` per player shot.
- `Brain._pending` keeps every inactive CreateObject result forever. At 200 entries new NPC spawns get dropped, which is a correctness bug.
- Nav field cache: clear-all at 32 entries causes a rebuild storm; pools of 8 mean ~117 KB arrays become garbage.
  - Fix: LRU eviction + pools of 32.
- Origin `Shift` drops all grid-relative fields needlessly.
- `Passthrough` re-applies every IgnoreCollision pair within 30 m in one tick every 3 s.

**QuestsCompat / Hire:**
- About 5 `GetComponents<PlayMakerFSM>()` per frame.
- `IsRangedNpc` → `FindDeep` BFS for melee NPCs every frame.
- `Physics.RaycastAll` per frame while there is a target.
- `TalkingNow` via `FieldInfo.GetValue` every frame.

**Patrols:**
- Car removal is unsliced, with O(n) save-list passes per car.
- `Prefabs.Exact` case-mismatch causes a periodic `FindObjectsOfTypeAll`. Memo the case-insensitive hit: 1 line, no risk.
- `Crew` collider refresh allocations.
- `WorldTraffic.Tick` LINQ.
- Map re-ink every 1 s while open; re-ink on revision change instead.

**CleanHUD:**
- `SceneCache.Discover` reads `t.name` twice + `GetComponents` per node on every tile apply. It is sliced correctly (one node per step), so the sub-audit's "unsliced" claim was wrong; only the garbage remains.
- `List.Sort(Comparison)` wrapper per frame.
- `QuestMap.FindMap` `GameObject.Find` each second while missing.

**NPC compute-only (pure, bigger refactor):**
- N2: precompute per-cell 8-direction move bitmasks after the bake. That makes Build, `OutClear`, `PickOutExit` and `GridSight` 3–6× faster with identical results. About 100 lines, +2 bytes/cell.

---

## Must stay per-frame (do not touch)

- **NPC:**
  - Brain/Idle rotation, velocity and pedal writes.
  - The SetVelocity/Rotate/LookAt/SmoothLookAt/Raycast override hooks.
  - `ApBody.Frame` for engaged or near gunmen.
  - Post-Animator LateTicks (Burrow, Pounce, DogBite, visible dogs).
- **Talk/Quests:** `GetKeyDown` input (talk, log, dismiss), Escape consumption, cursor unlock, `Hire.LateUpdate` seat pose, turn Slerps.
- **Patrols:**
  - `MotorcycleBalance.FixedUpdate`.
  - `Crew` → `Pilot.Step` (already 10–20 Hz decisions).
  - Rider anchor/pose LateUpdates.
  - Passenger aim.
- **PlayerBody:**
  - ThirdPerson PreCull view/projection and the camera SphereCast.
  - The body pose after the Animator while visible.
  - `RigApply` while weight > 0.
  - Hotkey polling.
- **CleanHUD:** compass x-placement, vanilla hide in `willRenderCanvases`, and MessageLog animation while moving.
- **Guns:** bolt/tracer drawing, WeaponTransition, Ironsights.

## Measure first (cheap instrumentation)

- Add "GC allocated in frame" and MapCheat camera state to the PlayerBody Perf line.
- Read `Hooks.*.Calls` per second during a raid (LosUpdate3D, ActivateGameObjectEnter, SetVelocityDo) to confirm the NPC hook items before changing them.
- F7 Performance before/after per batch: S2 camp fight, S5 convoy, and a session with active Fetch/Kill quests, a hire and a taken camp (the current log has none of these).

## Suggested batch order (one issue each)

1. CleanHUD: T1 map camera, plus T6 compass (CH-1/2/3/6).
2. NPC: T2 think phase/cap, T3 default, D6 stagger bug.
3. Patrols: T4 location scan, plus the `Prefabs.Exact` memo.
4. QuestsCompat: T7.1 `NearestByName`, T7.2 Repaint-only, T7.4 giver memo.
5. NPC: D1 `Idle.Drive` fan, D5 Senses round-robin, `_pending` fix.
6. NPC Nav: T5 sliced `PickOutExit` + routing budget, field-cache LRU, Shift fix.
7. PlayerBody GC batch, then the Patrols D3/D11/D12 batch, Guns D2, QuestsCompat T7.3/T7.5.
8. Larger: Nav move bitmasks (N2), Core GUI runner (D18).
