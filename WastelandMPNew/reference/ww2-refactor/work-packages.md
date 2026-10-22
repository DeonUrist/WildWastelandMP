# Refactor work packages (parallel agents)

How to run: a coordinator (Denis, or one lead Claude session) starts the agents of one wave in parallel, each with the brief below copied as its task. Each agent works on its own branch in its own git worktree. The coordinator merges finished branches into `refactor/main`, runs the gates, and only then starts the next wave.

```
git worktree add ..\WW-<package> -b refactor/<package> refactor/main     # one per agent (Denis's PC, Windows)
```
On Denis's PC agents build with `dotnet build WildWasteland.csproj -c Release -p:Deploy=false` (never deploy from a refactor branch). Claude cloud sessions use `build.sh` with staged game DLLs (AGENTS 7.2) and run `tools/config-harness` when a wave touched config.

## Execution environment (D11-D16, overrides the Windows notes above)

- Repo: the coordinator's git repo `/home/claude/ww2` (branch `refactor/main`), mirrored to `V:\Repos\Apocalypter\WildWasteland2` at checkpoints. Each agent works in its own worktree `/home/claude/wt/<package>` on branch `refactor/<package>` (the coordinator creates it). Never touch another worktree or `refactor/main`.
- Compile: `sh tools/refactor/compile.sh` from your worktree (about 5 s; must print `COMPILE OK`). No dotnet, no deployment, no game.
- Commit identity: `git -c user.name=Denis -c user.email=denis.c@everworker.ai commit ...`; message `refactor(<package>): <what>` and end with the two attribution lines given by the coordinator.
- **State file (D15) - mandatory.** Create `docs/refactor/state/<package>.md` as your first action (copy the step list of your brief as a checklist: `- [ ]` / `- [x]` / `- [~] in progress`, plus sections "Last good compile (commit)", "Next action", "Blockers / questions for Denis", "Notes for a resuming agent"). Update it and commit (code + state together) after EVERY completed step, and before any long operation. A resuming agent first reads the state file and `git log` of its branch, then continues from "Next action".
- **Questions:** if something is ambiguous or a decision is Denis's (behaviour change, deleting a feature not listed, contract change), do not guess: write it under "Blockers / questions for Denis" in your state file AND your request file, continue with other steps, and mention it in your final reply.
- D12 fresh start: delete legacy migrations/compat for WW1 and standalone mods as listed in README section 0 (D12) in files you own. Keep current save/config formats.
- **Assets (D13):** non-.cs files under `src/Modules/<Old>/` (Embedded, Icons, sounds, models, json defaults...) are moved by the Wave B owner of the module that uses them. In Wave A, each source agent writes `docs/refactor/assets/<package>.tsv` (tab-separated: `source path` / `target module` / `target path` / `code references (file:line, resource or disk-path strings that must change)`) covering EVERY non-.cs file in its source folders, and does not move them. In Wave B the target owner `git mv`s the rows whose target is its module (it may delete/move exactly those source paths), updates resource-name and disk-path strings in its own code, and removes the csproj/build.sh special RuntimePath remaps for its files in `docs/refactor/requests/<package>.md` (Wave C applies csproj/build.sh changes).

---

## 0. Rules every package follows (paste above every brief)

1. Read first: `docs/refactor/README.md` (all), `docs/refactor/core-modapi.md` (all for W0/B-Core, sections 1-5 + your domain rows otherwise), your rows in `docs/refactor/file-map.md`, the inventory sections named in your brief, AGENTS.md sections 3, 5, 6, 7 (skip the dated history at the top and in section 0; it is being moved out).
2. Ownership: you may create/modify/delete ONLY paths matched by your package in `docs/refactor/ownership.json` (plus your report `docs/refactor/reports/<package>.md`, request file `docs/refactor/requests/<package>.md`, state file `docs/refactor/state/<package>.md`, Wave A asset manifest `docs/refactor/assets/<package>.tsv`, and in Wave B the source paths of asset-manifest rows targeting your module). Before finishing run `python tools/refactor/check_ownership.py <package> refactor/main`. If you need a change elsewhere (a contract, another module's code), write it to your request file and work around it with a `// REFACTOR-REQUEST <id>` marker; never edit the other file.
3. Contracts in `src/Core/ModApi/Contracts/**` and `src/Core/ModApi/Events/**` are frozen after W0. Implement/consume them as they are.
4. Behaviour preservation: no gameplay/visual/timing change unless README sections 7-9 list it for your package. Keep save keys, sidecar formats, prefab ids, saved GameObject names, saved component type names (README 10). Keep config entries readable (P1).
5. Old public members that other modules still use: mark `[Obsolete("refactor: use <new API>")]`, keep them working (forward to the new code). Do not delete them before Wave C.
6. Every function you write or move gets a `///` summary (README 11). Run `python tools/refactor/check_comments.py --package <package>` (Wave B: must pass for all files you own; Wave A: only files you created).
7. Wave B and C: `python tools/refactor/check_module_refs.py --package <package>` must report 0 violations in files you own.
8. Gate: `sh tools/refactor/compile.sh` prints COMPILE OK (warnings: no new ones). Then stop; do not loop on extra checks. No game launch, no harness/mocks/test projects (AGENTS 0.3).
9. Report `docs/refactor/reports/<package>.md`: files moved/created/deleted (with evidence for deletions), contracts provided/consumed, `[Obsolete]` members left for Wave C, behaviour changes (expected: none), config/save notes, Denis test steps, open questions. Commit on your branch with message `refactor(<package>): <summary>` + attribution lines; do not push unless the coordinator asks.

---

## Wave 0 - foundation (2 agents in parallel)

### W0-A Core kernel
- Owns: `src/Core/ModApi/**`, `src/Core/Boot/**`, `src/Core/Modules/**`, `src/Core/Scheduling/**`, `src/Core/Harmony/**`, `src/Core/Log/**`, `src/Core/Persistence/**`, `src/Core/Game/**`, `src/Core/Input/**`, `src/Core/UI/SettingsShell.cs`, every existing file directly in `src/Core/` that the steps below change (Plugin, Runner, ModuleBase, ModuleHost, ModuleConfig, Scheduler, WorldWork, LoadingProgress, Hooks, Patches, Services, QuestServices, Log, SaveBus, SaveTypes, GameRefs, Origin, Hotkeys, Assets), new module skeleton folders `src/Modules/{Debug,Presentation,NPCAI,Gunplay,Vehicles,Loot,Tools,Developer,Compatibility,Saves}/<Id>Module.cs` only, `tools/refactor/**`.
- Read: inventory/core-performance.md sections 0, 3, 5.1, 7, 9; inventory/quests-compat.md 10b, 10e, 10f.
- Do:
  1. Move kernel files into the `Core/<Area>/` folders of README 3.3 (namespace stays `WildWasteland.Core`, folders do not change namespaces).
  2. Implement `ModApi`, `ModEvents`, `EntityId/Entities`, `IEventTransport`, `BroadcastAttribute` (core-modapi.md 1-3). ModuleHost.Cleanup calls `ModApi.RemoveAll` and `ModEvents.RemoveAll`.
  3. Write ALL contracts and events of core-modapi.md 8-9 as compile-ready interfaces/structs in `Core/ModApi/Contracts|Events/<Domain>.cs`. Existing interfaces from Services.cs/QuestServices.cs move there unchanged (same names, namespace `WildWasteland.Core` kept via `namespace WildWasteland.Core` so call sites compile). `Services.*` becomes `[Obsolete]` forwarders to `ModApi`; old `Services` events keep working and are also raised from the new event publishers once those exist.
  4. `Log` levels/generic overloads (core-modapi.md 4) keeping `Log.Info/Always/Warn/Error/Verbose` as `[Obsolete]` wrappers (Info = level 1, Verbose = L1); remove the `SaveSystem.Fail` side effect from `Log.Error` and add `SaveBus.ReportFailure` (SaveSystem keeps working: wire its existing failure path to the new channel). `Trace` stub. `[Debug] VerboseLevel` entry bound in Core for now (Debug module takes it over in B).
  5. Scheduler: `Job.Removed` set by RemoveAll; phase-keeping Interval; job name prefix check (warn once, do not fail). Hooks: table-driven registry, FsmEnable removal fix, Calls for all kinds, per-handler timing behind `Trace.On`, new dispatchers listed in README 4.1 (unused until Wave A/B subscribers move to them).
  6. (D12) IModule additions: `ProvideApis()`, `Settings(SettingsShell)` with ModuleBase defaults. NO StorageId/LegacySections/MigrateMissing/MoveEntry. Make `ModuleConfig.Migrate` and `Legacy.*` `[Obsolete]` no-ops (Wave A deletes their callers); stop seeding settings.cfg from the old main cfg; keep `ModuleConfig.Player/Tool/Hotkey` binders. Config sections are `[<Id>]` / `[<Id> / <Sub>]` of the NEW module ids from Wave B on.
  7. (D12) Delete the `SaveTypes` old-assembly aliases (keep the ES3 type lookup hook only if something else needs it; otherwise delete the file and its install call). Add the Sidecars registry + `SaveBus.Key` helper + typed SaveBus events.
  8. `SettingsShell` API (CorePages keeps drawing as today; it will read contributions once B agents register them - add the contribution path next to the existing hand-built rows).
  8b. Set `Plugin.VERSION = "2.0.0"` (D11).
  9. Skeleton modules (registered in `ModuleRegistry` after the existing ones, `DefaultEnabled=true`, empty Init, StorageId per P1) for Debug, Presentation, NPCAI, Gunplay, Vehicles, Loot, Tools, Developer, Compatibility, Saves. Existing modules unchanged. Their `[<Id>] Enabled` entries are new; nothing reads them yet. Default `Assets.ModuleDir(Id)`/`ModuleConfigDir(Id)` use the new Id (D13).
  10. `tools/refactor/*.py` already exist (from the plan); keep them working against the new layout (update path patterns if needed).
- Must not: change any gameplay behaviour; move Save* framework files (A1 does); touch `src/Modules/**` except the skeleton entry files.
- Done when: build 0 errors; a quick read of the startup summary path shows the new skeleton modules; report lists every new contract.

### W0-B Core utilities
- Owns: `src/Core/Util/**` (new files only), `src/Core/Game/{WorldIndex,WorldTiles,FsmIndex,FsmPause,PrefabCatalog,ItemSpawner,Factions,Clock,Names,Layers,CameraArbiter,PlayerState}.cs` (new files; coordinate the file list with W0-A: W0-A must not create these).
- Read: inventory/quests-compat.md 10d; inventory/world.md 5 (utility copies); inventory/core-performance.md 5.3.
- Do: create ONE implementation each, as a superset of the existing copies (do not delete the copies; Wave A switches callers and deletes them):
  - `Json` (Parse/Serialize; union of the 8 copies' behaviour incl. CarTemplateFile/EditorData StoreJson writer quirks; round-trip identical output for existing files), `Glb` (static mesh; gas-station superset; MUST produce identical vertices/mesh names - the gas-station mesh-name SHA feeds NPC nav-bake signatures), `Gltf` (skinned; PlayerBody/Women copy), `Bindposes`, `Audio` (WAV decoder = Core VoiceAudio behaviour + Guns/Women Wav + Quests ReadWav + Rider Wav; RMS `Level`; `LoadAsync(path, AudioType, callback)` replacing 5 coroutines), `Textures` (`Load(path|resource, linear, mipmaps, readable)` with cache + Destroy on module shutdown), `Access` (one accessor generator: merge NativeAccess, OptionalAccess, Performance FastReflect/BatchCompiler/CompileQueue APIs behind one facade; keep the old classes as thin wrappers until Wave B), `KeyMap` (PlayerBody/Quests copy), `GameBindings` (cached KeyAction per InputStorage).
  - Game services: `WorldIndex` (sliced root walker + registrations; CleanHUD SceneCache feature set), `WorldTiles` (single MapMagic Applied/Finalized/Moved subscription + TerrainEdited from Vistas through a registration hook; per-handler try/catch; immutable handoff rule from AGENTS: capture in Finalized, consume in Applied), `FsmIndex` (public API over the Performance PM1 registry: ByOwner(go), ByName(name); implemented by registering a provider from Performance in Wave A; until then falls back to FsmList scan), `FsmPause` (owner bits), `PrefabCatalog` (ES3 ref manager + Resources, warmed at world ready, sliced), `ItemSpawner.RegisterSaved(go, baseName)`, `Clock` (game hours/day), `Names.Base` (non-allocating compare variant), `Layers` constants, `Factions` (relations API; empty data until Quests/Patrols feed it), `CameraArbiter` (projection/FOV owner with priority + onPreCull single writer; not used until Wave B), `PlayerState` (superset of PlayerBody Game.cs player queries, Patrols PlayerRef, Quests Game.cs player bits).
- Must not: change callers (Wave A does), touch W0-A files, create `Core/Util/Assets.cs` (B01 moves the existing `Core/Assets.cs` there in Wave B).
- Done when: build 0 errors; every utility has `///` comments; report lists, per utility, which copies it replaces and any behaviour differences found between copies (with the chosen behaviour).

Coordinator gate W0: merge W0-A then W0-B; build; run `check_module_refs.py --baseline > docs/refactor/baseline-refs.txt` (records today's violations so Wave B can show progress); freeze contracts.

---

## Wave A - decomposition by SOURCE module (8 agents in parallel)

Common rules for Wave A:
- Destination of every moved part: `src/Modules/<Target>/<SubArea>/_from<Source>/<File>.cs` (for Core targets `src/Core/<Area>/_from<Source>/<File>.cs`). `<SubArea>` is given in the table below for Presentation and NPCAI; otherwise mirror the source subfolder (e.g. `Vehicles/WarRig/_fromPatrols/Factory.cs`).
- Namespaces do NOT change in Wave A. Code keeps compiling against the old types.
- SPLIT files: split by whole classes or by `partial` class files; never rewrite logic. A method that must move between targets but belongs to one class goes as a `partial` part of that class.
- Old module entry (`<Old>Module.cs`, `<Old>Settings.cs`, `<Old>Pages.cs`): split into `partial` files per target placed in that target's folder (`src/Modules/Vehicles/_fromPlayerBody/PlayerBodyModule.Vehicles.cs` with `partial void InitVehicles(); partial void ShutdownVehicles(); partial void BindVehicles(ModuleConfig)`, statics and pages that belong to Vehicles). The old core entry file keeps calling the partial methods. This lets Wave B agents take lifecycle over without touching each other's files.
- Replace calls to local duplicate utilities with the Core/Util ones and delete the copies (rows marked DELETE/MERGE-INTO Core in file-map.md). If W0-B documented a behaviour difference, keep the old behaviour via an option on the Core call.
- Delete dead code listed for your files in README 7 / file-map.md (only the listed items).
- Embedded folders do not move in Wave A.
- Gate: build 0 errors; `python tools/refactor/filemap_coverage.py --wave A --package <package>` shows every file you own as moved/deleted per the map; no behaviour change.

Sub-area table (Presentation and NPCAI, because two Wave B agents share each):
| target | sub-area | goes there |
|---|---|---|
| Presentation | `Character/` | PlayerBody Animation/*, Body/* (except Projectiles/PickAssist), GameBindings-free parts of Game.cs player-anim inputs, FemalePain; NPC Women/*, Animals/DogAnimations + visual parts of Burrow/Pounce/DogBite, Brain/ApBody, Brain/ReloadSounds, Brain visual parts (Drive/Crouch/Aim freeze), Idle `Anim.Play`/RunInPlace; Patrols CrewAppearance, PlayerAnims; Quests Hire recoil/arm poses; Guns WeaponTransition `Pose()` |
| Presentation | `Player/` | PlayerBody BodyRunner, KeyMap-free player input adapters, Crawling input parts |
| Presentation | `Camera/` | PlayerBody Camera/* (except Perf.cs, ChaseCameraCompat), Body/PickAssist, ThirdPerson camera parts |
| Presentation | `Traversal/` | PlayerBody Climbing/* |
| Presentation | `Seats/` | PlayerBody Vehicles/CarSeat, WheelArms solver parts; Patrols Crews/Pose, BusPassengerPose store, RiderAnchorGuard, Rider graph/pose parts, Passenger/QuestFollowers pose parts |
| Presentation | `VehicleSkins/` | Patrols Vehicles/Paint |
| NPCAI | `Behaviour/` | NPC Brain/*, Detection/*, Animals/* behaviour, Hazards/*, NpcApi, NpcModule*, Debug pages behaviour rows; Guns Bosses; Patrols PassengerGuard/SeatedLosGuard/PatrolVisibility/TrafficAgent sensing/TrafficActor/Crew.MuteAi replacement point/Rider behaviour; Quests Hire follower AI, QuestFollower, Takeover walkers |
| NPCAI | `Population/` | Towns InteriorPopulation, NativePoiPopulation, TownPopulation actor parts |
| NPCAI | `Navigation/` | NPC Navigation/*, PoiNav runtime (PoiNav, PoiNavTraverse, PoiNavData), Towns TownNavigation, TownApproach, TownOverlap, TownBakeData, TownBakeBridge, TownWalkGraph consumer parts, TownPerimeter walkers, TownOverlayPlan LotPatch, PoiDressing nav prefix/Exit, TownTowerKit bake step |

### A1 Core + Performance + developer
- Owns: `src/Core/**` except W0-A/W0-B owned folders' NEW files (it may move files out of `src/Core/`), `src/Modules/Performance/**`, destinations `src/Core/Optimization/**`, `src/Core/Fixes/**`, `src/Core/Bridges/**`, `src/Core/UI/**` (except SettingsShell.cs), `src/Modules/Saves/**/_fromCore/**`, `src/Modules/Compatibility/**/_fromCore/**`, `src/Modules/Compatibility/**/_fromPerformance/**`, `src/Modules/Debug/**/_fromCore/**`, `src/Modules/Debug/**/_fromPerformance/**`, `src/Modules/HUD/**/_fromCore/**`, `src/Modules/Tools/**/_fromPerformance/**`.
- Read: inventory/core-performance.md (all).
- Do: Performance -> `Core/Optimization/{Framework,PlayMaker,Content,Rendering,World}` (keep `[Performance / ...]` sections; PerformanceModule stays the registered entry in Wave A, now living in `Core/Optimization/Framework/` as the same class); Telemetry + measurement page parts -> Debug; GeneratedLodExport -> Tools; SigmaFsmStates, VistasWorldGen, ApocasaverExclusion, SaveCompatibility Apocasaver pose part -> Compatibility; Save* framework, WorldHistory, SaveLibraryPage -> Saves (`_fromCore`); drawing parts of SaveWarning/SaveRuntime.DrawSaving/ReplacementGuard notice -> HUD (`_fromCore`); fixes -> `Core/Fixes`; CorePages split into partial files per target (`CorePages.Presentation.cs` etc. placed in `src/Core/UI/_pages/` - owned by B01 in Wave B); register Performance PM1 as the `FsmIndex` provider; `PatrolsBridge` stays until B.
- Must not: change Optimization behaviour or feature keys.

### A2 PlayerBody
- Owns: `src/Modules/PlayerBody/**` (except `Embedded/`), destinations `src/Modules/*/**/_fromPlayerBody/**`, `src/Core/**/_fromPlayerBody/**`.
- Read: inventory/playerbody.md (all).
- Do: per file-map. Presentation sub-areas as above; Vehicles: Car ignition/cassette/cockpit, IgnitionControl, WheelArms steering patch + returnToCenter; HUD: VehicleHud, InventoryModel, portrait pictures, Car hints/OnGUI; Gunplay: Projectiles; Developer: WheelGripEditor, GunPose/AimLift editor parts, PlayerBodyPages PoseEditor/ArmButtons/Steering; Compatibility: ChaseCameraCompat, WeaponsPackShadows, Apocapocket part of EquipmentInventory; Core: Game.cs player queries -> `src/Core/Game/_fromPlayerBody/` (B01 merges into PlayerState), GameBindings/KeyMap callers switched to Core/Util; delete Perf.cs, Json/Gltf/Bindposes copies, dead items (README 7).

### A3 NPC
- Owns: `src/Modules/NPC/**` (except `Embedded/`), destinations `.../_fromNPC/**`.
- Read: inventory/npc.md (all).
- Do: per file-map. Women of Wasteland whole -> `Presentation/Character/_fromNPC/Women/` (D9), duplicates deleted, Level -> Core Audio; behaviour/navigation sub-areas per table; PoiNavEditor/PoiNavView display/PoiNavBake/PoiNavSetup/NavMapTools/NavMapHighlight -> Tools; DebugOverlay + Nav dump/trace parts + NpcPages timings -> Debug; WeaponRanges/VanillaEvents ray extension/Aim spread/magazine table -> Gunplay; DogCompanionCompat(+Seating), CaveDarknessBridge, WeaponsPackCompat -> Compatibility; Hazards/Storm query part -> Survival; delete ChainsawLimbs, `Brain.LateTick`, `LearnLadderSide`, `MasksNow`.

### A4 Patrols
- Owns: `src/Modules/Patrols/**` (except `Embedded/`), destinations `.../_fromPatrols/**`.
- Read: inventory/patrols.md (all; section 1a for Patrol.cs).
- Do: per file-map. Vehicles: Motorcycles/*, MotorcycleIntegration vehicle part, WarRig/* (except PoiEncounter/WorldSpawning notice part/NativeStormProtection storm part/DustStormBridge), ModVehicles registry part, Suspension tune, TrailerSupport, VehicleAttachments, Explode wreck part, CarTemplateFile structure, VehicleBuildCache, Patrol.cs assembly/Nwh/InputControl/ExitSpeed; Presentation: Pose, PlayerAnims, RiderAnchorGuard, BusPassengerPose store, Paint (D10), CrewAppearance (D10), Rider/Passenger/QuestFollowers pose parts; NPCAI: PassengerGuard + combat parts of Passenger/QuestFollowers, SeatedLosGuard, PatrolVisibility sensing, TrafficAgent sensing, TrafficActor/PlayerHostile, Rider behaviour; Gunplay: lance projectile; POI: PoiEncounter, WorldSpawning notices, TrafficLocations discovery, TrafficCamps state; Loot: Cargo, PickupCatalog, LootProfile/LootItem, MerchantCatalog.Goods; HUD: TrafficMap drawing, TrafficMapInk; Tools: Patrols editor (TemplateMenu, Ledger, EditorSelection, PatrolEditorPage, RecorderPage + TemplateExporter recorder part); Developer: TurretSpotter, MerchantEditor, BusPassengerPoseEditor, TurretSpot/BusPose/MerchantSpot pages; Survival: NativeStormProtection storm part, DayLight; Compatibility: DustStormBridge, MotorcycleMod lookup, ModVehicles assembly scan, Suspension PartAdjustment part, WarRig SpawnerIntegration, PickupCatalog Apocaspawner cfg read; Core: Prefabs, Register, PlayerRef, TrafficSpace, PrefabName/CloneSuffix, TrafficCodec (-> Json), ShellRecycle, AudioVoices setting, QuietFsm switch; delete MeleeWheels + dead members.
- Note: Patrols keeps Id `Patrols`; `PatrolsAndLoot.json` stays where it is.

### A5 Guns + HUD + CleanHUD
- Owns: `src/Modules/{Guns,HUD,CleanHUD}/**` (except `Embedded/`), destinations `.../_fromGuns/**`, `.../_fromHUD/**`, `.../_fromCleanHUD/**`.
- Read: inventory/guns-hud.md (all).
- Do: Guns -> Gunplay (Tracers split by class/method per the map; Bosses -> NPCAI/Behaviour; WeaponDamageText -> HUD; BloodFxCompat/WeaponsPackSound/ApocaspawnerDamage -> Compatibility; Wav -> deleted for Core Audio; `PlayerRig` -> Core GameRefs); HUD + CleanHUD -> HUD (VanillaHits detection part -> Gunplay; theme GlyphText/AccentTheme/panel textures/DialogueWidgets palette -> `src/Core/UI/Theme/_fromCleanHUD/`; SceneCache -> Core WorldIndex; PauseCursorFix -> Core/Fixes; Json -> deleted; compat files -> Compatibility; MapFog Immersive Map import part -> Compatibility; Menu save/load parts -> Saves; LoadingScreen stays HUD).

### A6 Towns + POI
- Owns: `src/Modules/{Towns,POI}/**` (except `Embedded/`), destinations `.../_fromTowns/**`, `.../_fromPOI/**`.
- Read: inventory/world.md sections 1-5, 8, 9.
- Do: per file-map. POI target gets planning/building/geometry/markers registration/save records; NPCAI/Navigation gets routing/overlays; NPCAI/Population gets population rules; Loot gets PoiSpawnCatalog, item parts of PoiDressMarkers, spawner budgets/progress, gas-station loot spawners; Tools gets PoiDresser, PoiPropCatalog editor parts, marker kinds, test pad tab; HUD gets TownMarkers label/icon part; Debug gets TownLoading timing; delete the dead files/members (README 7); GasStation Glb/Json -> Core/Util (identical-vertex rule).

### A7 Survival + Weather
- Owns: `src/Modules/{Survival,Weather}/**` (except `Embedded/`), destinations `.../_fromSurvival/**`, `.../_fromWeather/**`.
- Read: inventory/world.md sections 1-9 (Survival/Weather rows).
- Do: Weather -> `Survival/Weather/_fromWeather/` (WeatherModule stays the registered entry in Wave A); WeatherDiagnostics + diagnostic snapshot -> Debug; StewPot NaturalLoot -> Loot; SpawnerBridge -> Compatibility; Glb/Json copies -> Core/Util.

### A8 Quests + QuestsCompat + EngineSimCompat
- Owns: `src/Modules/{Quests,QuestsCompat,EngineSimCompat}/**` (except `Embedded/`), destinations `.../_fromQuests/**`, `.../_fromEngineSimCompat/**`.
- Read: inventory/quests-compat.md sections 1-9.
- Do: delete QuestsCompat; EngineSimCompat -> Compatibility/EngineSim (entry stays registered in Wave A); Quests split per map (Hire -> Quests/NPCAI/Presentation/Gunplay/Patrols-crew parts; Takeover -> Quests/NPCAI/POI; Game.cs -> Core/Quests; QuestWorld -> Core WorldIndex; KeyMap -> deleted for Core; Voice `ReadWav` -> Core Audio; FactionDump/QuestLogger -> Debug; TalkHook/NativeConversation Apocatalker parts -> Compatibility; IMGUI fallback UI -> HUD).

Coordinator gate A: merge all eight (any order; ownership is disjoint); build 0 errors; `filemap_coverage.py --wave A` green for all; Denis smoke test on a copy of his saves: new game + load old save, drive, fight, town, storm, quest; compare startup summary (same modules on, same patch counts). Behaviour must be identical.

---

## Wave B - unification by TARGET module (18 agents in parallel)

Common rules for Wave B:
- You own your target folder(s) including every `_from*` subfolder in them, plus the old entry partial files placed there in Wave A, plus the old entry core file assigned to you below.
- Goal per package: (1) the new module entry owns lifecycle/config binding/pages for everything in your folder (move from the old entry partials; leave the old partial methods empty), with `StorageId`/`LegacySections`/`AbsorbedModules` per P1; (2) flatten `_from*` folders into the final structure; (3) unify duplicated systems assigned to you (README 6); (4) provide your contracts via `ModApi.Provide` and publish your events; (5) replace every cross-module call in your files with ModAPI/events (old members used by others stay `[Obsolete]`); (6) performance items of README 8 assigned to you; (7) logging to levels; (8) `///` comments on everything you own; (9) bugs of README 9 assigned to you.
- Hooks: subscribe via Core dispatchers; move direct patches on shared targets (README 4.1 list) to the new dispatchers.
- Namespace: switch your files to `WildWasteland.Modules.<NewId>` ONLY for types nobody outside your folder references any more (check with `check_module_refs.py`); otherwise leave it for Wave C. For saved component types add `SaveTypes.Moved(...)` lines to your module's `RegisterSaveAliases()` (called by Core before load) when you rename.
- Gate: build 0 errors; `check_module_refs.py --package` 0 violations in your files; `check_comments.py --package` green; report written with Denis test steps for everything you touched.

| id | package | owns | old entry core file | inventories | key work (README section) |
|---|---|---|---|---|---|
| B01 | Core-Kernel | `src/Core/**` except `Optimization/**`, `ModApi/Contracts/**`, `ModApi/Events/**` | - | core-performance 0-9 | integrate `_from*` (PlayerState, theme, fixes), CorePages -> SettingsShell (delete module references, keep layout identical), Access facade replaces Expression.Compile sites in Core, GameRefs/Origin menu-time lookups, ApocasetterBridge (+ Towns SettingsUI patches), ReplacementGuard notice via HUD notice API; 4.1, 4.2, 4.4, 8#7, 8#12 |
| B02 | Core-Optimization | `src/Core/Optimization/**` | `PerformanceModule` (in Optimization/Framework) | core-performance 1.3, 3.2-3.4, 5, 6 | 4.3: explicit feature list, no module hop (`[Performance] Enabled` -> `[Performance / Preset] Enabled`), Core dispatchers for double-patched targets, FsmPause shared with FS1, ICullingProtection/IVehicleOwnership queries (delete PatrolsBridge, namespace sniffing), LightPolicy/SunShadows scans removed, IStatsSource for Debug |
| B03 | Debug | `src/Modules/Debug/**`, `developer/**` | - | core-performance 4, 10; npc 4; patrols 4; guns-hud 4 | section 5: VerboseLevel + migrations of all old switches, level policy pages, hook/event/job timing pages, FrameStats, probes (port developer Instrumentation through Core.Patches), A/B capture/export, census, overlays (NPC DebugOverlay, nav dumps), FactionDump, WeatherDiagnostics; delete `developer/` project after porting (keep its capture JSON readable); QuietFsm/Vistas/PlayMaker warning policy by level |
| B04 | Saves | `src/Modules/Saves/**` | - | core-performance 1.1 Save rows, 9 | 6.14; sidecar registry consumers, typed save events, combat deferral via events, failure channel, presentation events for HUD, perf (cached FsmEvent refs, WorldHistory caching); keep every file format |
| B05 | Presentation-Character | `src/Modules/Presentation/{Character,Player}/**`, `src/Modules/Presentation/PresentationModule*.cs` | `PlayerBody/PlayerBodyModule.cs` | playerbody (all), npc 1 Women/Animals/Brain rows | 6.1 single driver (temporary `Legacy player driver` switch, README 13), model/appearance/voice unification, animals visuals, weapon props/poses, perf 8#3 |
| B06 | Presentation-World | `src/Modules/Presentation/{Camera,Traversal,Seats,VehicleSkins}/**` | - | playerbody Camera/Climbing/Vehicles rows; patrols Pose/Rider/Bus rows | 6.1 camera rig via CameraArbiter, unified traversal lease + ladder events, SeatPose for player/crew/bus/riders/followers, vehicle skins (Paint) with Loot key data; register Presentation partial `PresentationModule.World.cs` (create it in your folder) for lifecycle |
| B07 | NPCAI-Behaviour | `src/Modules/NPCAI/{Behaviour,Population}/**`, `src/Modules/NPCAI/NpcaiModule*.cs` | `NPC/NpcModule.cs` (+ `NpcModule.Config.cs` remnant) | npc (all), patrols Passenger/Rider/TrafficAgent rows, quests-compat Hire/QuestFollower/Takeover rows, world Population rows | 6.2: one Mover for Brain/Idle, followers (IFollowers), seated combat, perception query, population rules, Women roll removed (Presentation decides), Storm via Survival query, perf 8#2, logging switches -> levels |
| B08 | NPCAI-Navigation | `src/Modules/NPCAI/Navigation/**` | - | npc Navigation/PoiNav rows; world 8a | 6.2 one `INavApi` (grid baseline + PoiNav graph + town overlays/waypoint providers, no reflection), LadderSpeed reset bug, WalkerTick job, remove dead nav code |
| B09 | Gunplay | `src/Modules/Gunplay/**` | `Guns/GunsModule.cs` | guns-hud Guns rows + 7 design note; npc WeaponRanges/Aim rows | 6.3 weapon catalog + IWeaponStats modifiers, unified Hurt/ProjectileHit pipeline, Ironsights/WeaponTransition splits, overflow merge, perf items |
| B10 | HUD | `src/Modules/HUD/**` | `HUD/HudModule.cs`, `CleanHUD/CleanHudModule.cs` | guns-hud HUD/CleanHUD rows | 6.4 merge entries (`[CleanHUD] Enabled` -> feature toggle), renames, providers IMapMarkers/ICompassMarkers/ILocationMarkers/INotifications/IQuestPresentation, Saves-driven loading/saving screens, traffic map layer, Keys via Hotkeys, perf |
| B11 | Patrols | `src/Modules/Patrols/**` | `Patrols/PatrolsModule.cs` | patrols (all) | 6.5 ModAPI-only crews/towns/NPC, persistence via SaveBus key + sidecars, cleanup participant, IVehicleRouting + IRouteHintProvider, ICullingProtection, named split jobs, perf 8#8, 8#11 |
| B12 | Vehicles | `src/Modules/Vehicles/**` | - | patrols Vehicles/WarRig/Patrol.cs 1a rows; playerbody Car/Ignition rows | 6.6 IVehicleFactory + build cache (absorb into WorldWork), motorcycle/War Rig, ignition/controls, shelter registration, saved type aliases |
| B13 | POI | `src/Modules/POI/**` | `Towns/TownsModule.cs`, `POI/PoiModule.cs` | world (Towns/POI rows, 8b) | 6.7 one POI entry (Towns+POI), ITownsApi/IWorldLocations/IPoiApi, town templates + TownStore v7, delete dead code, settings out of player file, perf |
| B14 | Survival | `src/Modules/Survival/**` | `Survival/SurvivalModule.cs`, `Weather/WeatherModule.cs` | world Survival/Weather rows | 6.8 one entry (`[Weather] Enabled` -> feature toggle), storm identity/shelter/exposure/day light APIs, WeatherHazards gating, NaturalLoot rule registration |
| B15 | Loot | `src/Modules/Loot/**` | - | world Loot rows; patrols Cargo/PickupCatalog/EditorData rows | 6.9 data model + ILootTables/ILootSpawning, PatrolsAndLoot.json loot part reader + copy-missing migration to Loot/LootTables.json, spawner progress key unchanged |
| B16 | Tools + Developer | `src/Modules/Tools/**`, `src/Modules/Developer/**` | - | npc PoiNav editor rows; world PoiDresser rows + 8b; patrols Editor rows; playerbody editor rows | 6.10 + 6.11: Navigation editor, POI editor (rename PoiDresser), Patrols editor + Template tab (D8: tab visible when seated in a supported vehicle and when editing a template), new Towns editor (4 types, walls checkbox, allowed POIs incl. custom, external parking POIs) on ITownsApi templates, LOD export; Developer editors on Presentation/Patrols authoring APIs |
| B17 | Compatibility | `src/Modules/Compatibility/**` | `EngineSimCompat/EngineSimCompatModule.cs` | quests-compat 10c; inventories' compat rows | 6.12 one folder per mod, toggles + migration, implement contracts (IVehicleIgnition, IInventorySlots, IDrivingTelemetry, language menu, Weapons Pack single bridge, Apocasaver bundle, Vistas facts/events into WorldTiles/TerrainEdited/RoadLines, SigmaMod only when loaded) |
| B18 | Quests | `src/Modules/Quests/**` | `Quests/QuestsModule.cs` | quests-compat 1-9 | 6.13 contract-only Quests, typed ES3 save I/O (same key/format), follower/seat/pose/gun parts consumed via ModAPI, dead code removal, sidecar API for crew history |

Suggested batching if 18 parallel agents is too many: batch 1 = B01, B02, B03, B04, B09, B10, B14, B15, B17 (smaller/independent); batch 2 = B05, B06, B07, B08, B11, B12, B13, B16, B18. Contracts are frozen, so batches do not depend on each other except through `[Obsolete]` forwarders.

Coordinator gate B: merge all; build; `check_module_refs.py` repo-wide shows only `[Obsolete]`-forwarder references left; Denis full test (verification A-C + touched scenarios) and Debug A/B perf capture vs 1.0.9.

---

## Wave C - integration (1 agent)

- Owns: whole repo.
- Do:
  1. Delete `[Obsolete]` members and old `Services` fields/events after confirming no references; delete empty old module folders/entries (PlayerBody, NPC, Guns, CleanHUD, Towns, Weather, Performance, EngineSimCompat, QuestsCompat) and remove them from ModuleRegistry; old `Enabled` switches already migrated (P1).
  2. Final namespaces `WildWasteland.Modules.<Id>` everywhere; SaveTypes `Moved`/`MovedNamespace` for every saved type (list from all Wave B reports + inventory/patrols.md 9 + quests-compat 10f), longer prefixes first; write the alias list into docs/modules.md.
  3. Move `Embedded/` folders into the new module folders with a resource-name alias table (old logical names keep resolving; csproj/build.sh RuntimePath remaps updated; runtime install paths unchanged per P1).
  4. Update `build.sh`, `tools/build-module.sh` (stub list of the 16 modules), `tools/config-harness` (all modules, legacy sections).
  5. Docs: rewrite AGENTS.md section 5 for the new layout, move the dated history (top of AGENTS.md and the dated bullets inside sections 0, 3, 4, 7, 11) to `notes/agents-history.md` (P11); regenerate docs/harmony.md from `Patches.Records`/Hooks summary; update docs/modules.md (modules, services, dependencies, migrations), docs/decisions.md (D1-D10, P1-P11).
  6. Repo-wide gates: `check_module_refs.py` 0, `check_comments.py` 0, `filemap_coverage.py --wave C` 0, config harness pass, build 0 errors.
- Report `docs/refactor/reports/C.md` + release notes draft (player-facing: "no gameplay changes; performance; settings preserved").

## Wave D - verification (Denis) and fixes
Denis runs verification/README.md A-C, the scenario list from all reports, old-save loads (1.0.9 saves, and saves from standalone-mod era), config preservation check (compare settings.cfg/tools.cfg values before/after), and the Debug A/B capture. Fix tasks are normal AGENTS section 0 issue fixes against the new module layout.
