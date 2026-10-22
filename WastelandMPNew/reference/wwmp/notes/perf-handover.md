# Wild Wasteland performance — handover for the next session (#133 and follow-ups)

Written 2026-10-15 at the end of the #133 first-pass session. Read this file, `AGENTS.md`, `notes/plan-Performance.md` and `notes/perf-ww-lag.md` before changing anything.

## 0. Current state (updated after the Patrols prefab-cache follow-up)

**Latest 2026-10-16 request:** 750 m traffic range, slower construction (50 ms between assembly stages, two seconds between assemblies), and merchant cap two are deployed in build **m**, MD5 `63FAC347149992B654184F48C4986287`. Denis confirmed slower construction (not fewer encounters) and game closure. Release/Deploy=true passed; existing tools.cfg/settings.cfg values updated with backups. See `notes/perf-spawn-cadence.md` for the new copied-save cadence run; the l results below are historical and do not by themselves validate the new cadence.

Build m's `cadence-on-m` headless copied-save run completed **42 passed / 0 failed**: settings 750/two, minimum sampled stage spacing ~50 ms (95 samples), minimum inter-vehicle gap 2.050037 s, native save/title/reload and active/pending scene cancellation. Original ES3 hashes unchanged; player self-quit and temporary sandbox driver disabled. No scheduler exceptions or crash/patch-conflict signatures. First construction still reached 123 ms in the instrumented run; pacing does not divide that step.

**Previous follow-up:** Denis authorized the next Patrols/NPC batch and an isolated headless driver, then approved the PM2 lifecycle and PM7 comparison fixes. That staging build **0.1.26l** had MD5 `E3AB9ABE0E08D6986A608EFC42A61733`; numeric version stays 0.1.26. About 240 source lines across ten files implement one scheduler-owned vehicle assembly queue, reused car/NPC discovery snapshots, one NPC rig attachment per frame, corrected reentrant FSM registry timing and Shadow float-noise tolerance. No Core/config/save-format changes in that batch. Denis subsequently requested all current WW changes committed and pushed; this batch and the spawn-cadence follow-up are recorded together as `Fix #133: pace vehicle builds and queue NPC rigs`.

Latest Shadow run (`verification/runtime-perf/sandbox/Game/VerificationRuns/queue-shadow-l/`) completed **43 passed / 0 failed**, including native copied-save/title/reload, crew physics/melee/ram guard/bailout and queued scene cancellation. FS4 sampled 1,264 fields with zero source/reflection differences; FS1/PM2/PM7/FS2/PM10 reported zero mismatches. Assembly peak is one vehicle, rig peak one attachment/frame (baseline i: three vehicles, nine attachments/frame). Original ES3 hashes unchanged. This is bounded headless coverage, not full visual/long-session acceptance. Current enabled-mode results and exact limits: `notes/perf-queue-verification.md` and `docs/perf-log.md`. Native construction still produced approximately 100–123 ms indivisible steps in instrumented runs. CopyTexture is deferred at Denis's request.

Enabled-mode run `queue-on-l` also completed **38 passed / 0 failed**, with native save/title/reload and cancellation of one active plus two pending builds. Compiled property and string-reuse counters were exercised; no scheduler exceptions. Both players self-quit, original ES3 hashes remain unchanged, temporary sandbox driver disabled. Next performance target is the measured first body construction/activation step (~105 ms with optimizations On), followed by 15–21 ms seating calls; see the detailed probes before choosing a fix.

- Claude's first pass is `a8f189f`. The approved follow-up is recorded as `Fix #133: cache Patrols prefab lookups by registration generation`, including the documentation cleanup and four Patrols files (109 changed lines); no other module/Core source changed. Check git status/log for any later work. The older file inventory below describes the first-pass commit.
- Previous installed DLL was the prefab-cache follow-up (staging label 0.1.26i), MD5 `D9E505A3132D042514522C21C84752A4`. Windows Release build with Deploy=true passed with zero errors and ten warnings in unchanged code. No config entries changed. The previous h MD5 was `612B2F7B3288311D1AE6C5BF49156152`.
- Existing interactive logs show 42/42 features started and FS4 compiled 1,471 constructors / 8,489 setters in 2,584 ms. This establishes compiler readiness, not field parity or a completed gameplay checklist.
- Both the h baseline and the installed prefab-cache follow-up reached title-menu attachment in bounded 30-second headless runs. The follow-up reports Patrols on with seven patches and 42/42 Performance features active; no managed exception, failed patch/feature, conflict or native crash signature was found. Its logs are in ignored `verification/runtime-perf/20261015-225823-prefab-cache/`. Null graphics produced shader errors. The runner stopped its own process at the bound; no world load, clean-exit, gameplay or graphics pass is claimed.
- Next work and exact remaining gates: `notes/perf-preparation.md`. Denis requested headless runs without monitor automation. Issue #133 is already closed; no board status was changed here.

## 0.1 Original handover snapshot (historical)

- **Repo:** `V:\Repos\Apocalypter\WildWasteland`, branch `main`, HEAD `f262cd5` (0.1.25 RC22).
- **Nothing from this work is committed.** The working tree holds all of it uncommitted, including the version bump to `0.1.26` in `src/Core/Plugin.cs`.
- **Before editing:** run `git log --oneline | head` and `git status`. Other sessions edit CleanHUD in parallel, so take the files from the current tree, never from older copies. Commit only when Denis asks. The message format is `Fix #133: ...` plus the Co-Authored-By / Claude-Session lines.
- **Installed build:** `0.1.26h`, md5 `612b2f7b3288311d1ae6c5bf49156152`, at `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWasteland\WildWasteland.dll`.
  - **Not yet verified in game:** FS4 as a prefix copy, the FS2/FS3 background batches, seated crew without contacts, and the ESC cursor fix.
  - **Already run in game:** 0.1.26e. It loaded saves and Denis reported fewer combat spikes.
- **Uncommitted changes:**
  - **Changed files:** `WildWasteland.csproj`, `build.sh` (both now reference `MonoMod.Utils.dll`); `docs/harmony.md`, `docs/modules.md`, `docs/perf-log.md`; `src/Core/{Hooks,Plugin,QuietFsm}.cs`, `src/Core/UI/CorePages.cs`; `src/Modules/Patrols/{PatrolsModule.cs, Crews/Crew.cs, Crews/Ram.cs, Crews/Rider.cs, Spawning/Patrol.cs, WarRig/NativeStormProtection.cs}`; `src/Modules/CleanHUD/CleanHudModule.cs`.
  - **New files:** `src/Modules/Performance/**` (45 files, about 8.9k lines); `src/Modules/CleanHUD/PauseCursorFix.cs`; `notes/perf-ww-lag.md`; this file.

## 1. Runtime facts (all verified, each cost a crash or a bug)

1. **Reflection.Emit does not exist in this game.** `Apocalypter_Data/Managed/mscorlib.dll` is stripped: every `System.Reflection.Emit.DynamicMethod` constructor is a `Unity.ThrowStub` (PlatformNotSupportedException). Never use `DynamicMethod`.
2. **`Expression.Compile()` is the LightCompiler interpreter** (System.Core, `LambdaExpression.Compile` → `Interpreter.LightCompiler`). It is slower than reflection and allocates per call. Never use it on hot paths. NPC, EngineSimCompat and CaveDarkness still use it (see §5).
3. **Code generation that works:**
   - **MonoMod `DynamicMethodDefinition`** (BepInEx core, what HarmonyX itself uses). Each `Generate()` writes and loads one in-memory assembly, costing several ms each. Player.log prints `Fallback handler could not load library ...data-XXXX.dll` per load.
   - **Many methods should use one assembly:** use `FastReflect.Batch` (`src/Modules/Performance/PlayMaker/FastReflect.cs`) or `BatchCompiler` (same folder).
   - **What Batch does:** it emits through `MonoMod.Utils.Cil.CecilILGenerator(...).GetProxy()`, loads with `MonoMod.Utils.ReflectionHelper.Load(module)`, then calls `MonoMod.Utils.Extensions.SetMonoCorlibInternal(asm, true)`.
   - **Private members:** that last call is what lets generated code touch private members. Mono ignores `IgnoresAccessChecksTo`; MonoMod's DMD post-build does the same thing.
   - **Attach the method first:** a `MethodDefinition` must be added to its type before emitting into it, because CecilILGenerator imports through `Method.Module`.
   - **Tested on plain mono:** private fields, a property that throws (wrapped in TargetInvocationException), struct write-back in a box, and an invoker. Use `MONOMOD_DMD_TYPE=cecil` to mimic the game.
4. **HarmonyX `AccessTools.FieldRefAccess<T,F>` returned garbage here.** 0.1.26c crashed natively in `Fsm.OnDestroy` → `List.GetEnumerator` on a bogus `subFsmList`. Use `FastReflect.Getter/Setter<TOwner,TValue>` instead.
5. **Do not transpile `ActionData.LoadActionField`.** It produced `ActionData: Missing LoadActionField for type: System.Boolean` (73k errors, null action fields, save load stuck). The method has many try/catch/leave blocks and short branches; the exact cause is not isolated.
   - **What FS4 does now:** a prefix that always returns false and runs `ActionDataPort.Load`, a mechanical copy of the decompiled method (`Sources/code/PlayMaker/HutongGames.PlayMaker/ActionData.cs` L807-1116).
   - **How the copy reaches private members:** open-instance delegates for the private `Get*` / `LoadArrayElement` methods, and FastReflect getters for the private fields.
   - **Rule:** for big vanilla methods, replace them with a copy behind a prefix rather than transpiling.
6. **No Unity scene APIs during `Plugin.Awake`.** `RenderSettings.sun` crashed natively while called from the BepInEx chainloader. `PerformanceModule.Init` now waits for the first loaded scene (`_deferred`) before starting features.
7. **PlayMaker GUI lifecycle.** PlayMakerGUI is enabled only while paused, plus 2 s after the ESC menu closes (`__GameManager__` "Menu" FSM: "pause" runs `EnableGUI true`; "play" sends `Deactivate` to the "GUI" FSM with delay=2). While enabled, it re-locks the cursor on every Repaint. `CleanHUD/PauseCursorFix.cs` disables it on entering "play". In normal play PlayMakerGUI does not run.
8. **The game's raycasts hit triggers** (QueriesHitTriggers). WW Guns counts a trigger as a body only if its GameObject has a "Bodypart" FSM (`Guns/Tracers/Tracers.cs Counts()`). Every NPC root and head has one (`Sources/data/hierarchy/prefabs/L/Lugnut.txt`).
9. **ES3 saves the components of registered objects.** Never add components to saved objects. Rendering features restore their edits before `SaveLoadGame` enters `wait` (`Rendering/RenderShared.cs` SaveGuard).
10. **Patching rules.** A target patched directly by two WW modules logs `[Patches] CONFLICT`. Shared targets go through `Core.Hooks` dispatchers. New this session: `Hooks.FsmEnable` (PlayMakerFSM.OnEnable postfix); Patrols' TrackDust and Performance moved onto it.
11. **Logging.** Verbose log lines with the console open are slow. Always measure with `[Debug] VerboseLog` off, and guard string building with `if (Core.Log.Verbose)`.

## 2. Build, deploy, verify

- **Build in the cloud:**
  1. Stage the repo (`src`, `build.sh`, `*.csproj`) and the game's `Apocalypter_Data/Managed` + `BepInEx/core`.
  2. Run `MANAGED=<Managed> BEPCORE=<core> sh build.sh <outdir>` (mcs 6.8, C# 7.3).
  3. `CS0252` warnings in `ActionDataPort.cs` are intentional: reference compares like vanilla's `(object)fieldType == typeof(X)`.
- **Deploy:**
  1. The game must be closed. Check that `BepInEx/LogOutput.log` mtime has stopped moving, or ask Denis.
  2. Copy the DLL with a fresh staging name (`WildWasteland_<ver><letter>.dll`) and md5 the file on disk.
  3. Check that `Loading [Wild Wasteland x.y.z]` appears in `LogOutput.log`.
- **Logs:**
  - `BepInEx/LogOutput.log`.
  - Unity's `%USERPROFILE%\AppData\LocalLow\SawyerK Games\Apocalypter\Player.log` (native crash stacks, "Fallback handler" assembly loads, Unity errors). Folder access was granted once; request it again if needed.
- **In game:**
  - F3 opens the settings window (not F7). It has a separate **Performance** tab showing the preset, Off/On/Shadow per feature, telemetry (frame avg/p95/p99, spikes tagged `save/load/tile/compile`) and a census button.
  - Config lives in `BepInEx/config/WildWasteland/tools.cfg` under `[Performance / ...]`.

## 3. Performance module map (`src/Modules/Performance`)

- **`Framework/`:**
  - `Feature.cs`: base class (PlanId/Key/Category/UsesPreset/SupportsShadow; OnEnable/OnDisable must restore everything; `Knob` = preset value with a tools.cfg override, -1 = follow the preset).
  - `PerformanceModule.cs`: features found by reflection, deferred start, `TileAppliedEvent`.
  - `Telemetry.cs`, `PerformancePage.cs`.
- **`PlayMaker/`:**
  - `FsmRegistry` (PM1, per-GameObject FSM index in FsmList order) and `EventDispatch` (PM2 targeted events, PM4 broadcast).
  - PM3 fast removal (inside FsmRegistry), `GuiLoopFeature` (PM5), `TriggerProxyFeature` (PM6), `MousePickFeature` (PM7), `StringConvertFeature` (PM10).
  - `FsmSleepFeature` (FS1, `Fsm.ManualUpdate` + wake hooks; public `Wake(Fsm)` / `WakeAll()`).
  - `FastPropertyFeature` (FS2) and `FastCallMethodFeature` (FS3). Both queue accessor builds to `CompileQueue`, which runs one `FastReflect.Batch` per drain on a thread-pool item; the first batch runs on the main thread to warm the MonoMod IL proxy. Vanilla reflection is used until a build lands.
  - `FastActionLoadFeature` + `ActionDataPort` (FS4): a background `BatchCompiler` of all FsmStateAction ctors and field setters (≈1,086 ctors and 5,736 setters, ≈0.8 s offline), then a prefix copy of LoadActionField.
- **`Content/`:**
  - `FsmEdits` (verified FSM action edits + undo), `FsmPause` (ManualUpdate shared by owner bit), `PollSleep`, `ProxyAction`.
  - CT1 `Culler`, CT3/CT5/CT11 `PollFeatures`, CT4 `VehicleReflection`, CT6 `LightFlicker`, CT7 `HudFsms`, CT8 `Coyotes`, CT9 `ItemFsmFixes`, CT10 `PlayerCapsule`.
  - `PatrolsBridge` (reflection into Patrols; replace it with a service, §5).
- **`Rendering/`:** `RenderShared` (sliced prefab pass `RenderScan`, `QualityGuard`, `SaveGuard`, `GameCameras`); EN1, EN2, EN3, EN4, EN5, RC1, RC2, RC3, RC4, RC6, RC8.
- **`World/`:** CT2 `CarLod`, `WorldSettings` (RC5 MapMagic budget, RC7 CCD, EN6 time/sleep and origin jump, EN7 Azure, EN8 GC slice).

## 4. Open items in the Performance module (do in this order)

1. **Verify 0.1.26h in game:**
   - FS4 log line `[Performance] FS4: compiled N constructors and M field setters` after the second scene load.
   - No `Missing LoadActionField` in Player.log.
   - FS2 line on the tab: `Compile queue ... batches (... ms, background)`.
2. **Shadow runs.** Set FS1, PM2, PM7, FS2 and PM10 to Shadow. Play camps, cars, doors, saves/loads and fights. Expect 0 `shadow mismatch` lines. Any mismatch from FS1 means a missing wake source.
3. **FS4 parity check.** Add a debug-only Shadow mode: for N spawns, run vanilla reflection into a scratch action and compare field values with the copy's result.
4. **CT5:** throttle the polls that keep running (now they only sleep while waiting). **CT12:** cached ES3File per save/load. This needs Apocasaver coordination and save backups; it is a separate issue.
5. **Phase 6 (plan §9):**
   - FS5: drop Unity's native Update for sleeping FSMs.
   - PlaySound pooling (1,070 uses).
   - GetChild without allocations (3,479 uses).
   - Item culling via FSM, renderer and collider off instead of SetActive (needs a saves analysis).
6. **Docs:** completed during preparation: FS4 and shared-hook inventory corrected in `docs/harmony.md`, c–h history/evidence appended to `docs/perf-log.md`, runtime constraints recorded in `AGENTS.md` §7.3. Verification remains partial as recorded above.

## 5. Wild Wasteland lag work (from `notes/perf-ww-lag.md`)

**Done in 0.1.26g/h:** seated drivers, passengers and turrets have no contacts.
- `Rider.NoContacts` (all colliders trigger, all Rigidbodies kinematic) plus `Rider.KeepNoContacts` (a cheap re-check of the occupant's own colliders) replace every per-pair `Physics.IgnoreCollision` in `Patrol.SeatDriver/SeatOccupant/Takeover`, `Rider.Seat` and the periodic checks in `Crew.cs` / `RiderGuard`.
- A car's own CarAttack trigger (layer 10) would otherwise ram-damage its own crew, so `Ram.BeforeSetFsmFloat/BeforeSendEvent` skip CarAttack targets with the same transform root.
- Pairs remain only for one-off cases: the fresh mob that jumps off (`Rider.cs` SpawnMob), the thrown lance (`IgnoreOwn`), and the ejected corpse vs the car.
- **Test:**
  - Shoot drivers through the windshield (headshots too), passengers and turrets.
  - Ram at speed with the own crew aboard.
  - Walk into a crewed parked car.
  - Use Engine Sim crews.

**Next, by impact:**
1. **Prefab lookup.**
   - `Patrol.cs` `Prefabs.Exact` (`Resources.FindObjectsOfTypeAll<GameObject>` + LINQ) and `Prefabs.Scan` (`FindObjectsOfTypeAll<PlayMakerFSM>`, which deserializes asset actions) rescan on misses every 30 s; template validation (`Templates.cs:64-84`) re-checks every 5 s.
   - Build the exact and case-insensitive indexes once per scene during load. A miss stays a miss for that scene; invalidate on ModVehicles/ES3 registration.
2. **Global build queue.**
   - `Patrol.Build` is a plain coroutine sliced by item count (4 parts / 4 cargo / 1 occupant per frame), and builds overlap (Convoy and WorldTraffic start one every 0.7 s).
   - Replace it with a single `Scheduler.Sliced` queue with a 3–4 ms budget, one build at a time, checking `Scheduler.FrameMs`.
   - Add `Telemetry.Mark("patrol-build")` through a Core service.
3. **Settle frame.**
   - `SetPartConditions`, `Headlights`, `RaiderMusic` and `Crew.Init` each run `GetComponentsInChildren<PlayMakerFSM>(true)` over the whole car.
   - Collect FSMs per part at Attach, or use the FsmRegistry service below.
   - `Suspension.Describe` and per-part strings are built even with VerboseLog off.
4. **Per occupant:**
   - `Passenger.cs` reflection (`ForceRangedOnly` `GetFields`, `IndexTargetSources` + `FieldInfo.GetValue` at 10 Hz), `FindMeleeWeapons`, `Rider.AutoSpot`: cache per prefab and action type, and use `FastReflect` getters built in a `Batch`.
   - `Paint.VehicleTextureKey`: up to 4 `Directory.Exists` per material. Resolve once in `Preload`.
5. **NPC:**
   - Rig build (`Brain.Make → ApBody.Attach`): PlayableGraph + AvatarMask + `GetComponentsInChildren<Component>` inside the SetVelocity prefix / `RegisterPending`, all in one frame. Queue it, at most one per frame.
   - `Senses.Make`: cache per prefab.
   - Gungirl glTF: prewarm at world-ready.
   - `Nav.BeginBake`: pool the 600×600 arrays.
   - `Brain.NearestHide`: `FindGameObjectsWithTag` every 10 s. Cache per scene.
6. **Fight time:**
   - `Pilot.OnCollisionStay` on every raider car allocates a Collision per contact per step. Add it only in the Charge/Travel states.
   - `LightsTick` (dusk/dawn): slice it.
   - `Explode`: about 8 full-hierarchy scans per wreck.
7. **Expression.Compile on hot paths:** `NPC/Detection/Senses.cs:539`, `NPC/Detection/CaveDarknessBridge.cs:40`, `NPC/Animals/DogCompanionCompat.cs:124`, `EngineSimCompat/EngineSimCompatModule.cs:274-292`. Replace them with `FastReflect` (single DMD at init, or a Batch). Measure first.

**Infrastructure to add, so WW can lean on the Performance module:**
- `Core.Services.Perf` (provided by Performance, null-checked):
  - `Mark(tag)`.
  - `IList<PlayMakerFSM> FsmsOn(GameObject)` (FsmRegistry.On).
  - `Wake(PlayMakerFSM)` / `WakeTree(GameObject)` (FsmSleepFeature).
  - `PrewarmActions(GameObject prefab)` (queue FS4 / FS2 builds for template and crew prefabs at world-ready).
  - `Batch NewBatch()` (FastReflect for other modules).
- `Core.Services.Culling { bool Protected(GameObject); void BeforeFarDelete(GameObject); bool IsActivelyDriven(GameObject car) }`. It replaces `Content/PatrolsBridge.cs` reflection and lets CT2 CarLod handle parked, setup-only and wrecked raider cars. Today `World/CarLod.cs` skips any car with a Patrols component.
- Move Crew / PassengerGuard / Pose / RiderGuard updates and the build coroutines under Scheduler jobs, so F3 attributes them and the frame budget applies.

## 6. Verification checklist for any change

- Build: 0 errors.
- No `[Patches] CONFLICT` and no `could not start` in LogOutput.log.
- No new Unity errors in Player.log.
- Load a save; save; reload; quit to menu and load again.
- Convoy spawn, a fight with riders, bail-outs, a wreck, perma base, driving, refuelling.
- Compare F3 telemetry against preset Vanilla on the same save, with VerboseLog off.
