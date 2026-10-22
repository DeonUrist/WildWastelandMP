# Wild Wasteland full refactor plan (2026-10-22, based on WW 1.0.9 / HEAD 2e98d0b)

Status: PLAN, approved scope by Denis on 2026-10-22. Nothing in `src/` has been changed by this plan.

Files in `docs/refactor/`:

| file | what it is |
|---|---|
| `README.md` (this) | Decisions, target architecture, Core/ModAPI/Debug design, removal and performance lists, rules |
| `core-modapi.md` | C# 7.3 sketches of the new Core kernel: ModApi registry, event bus (multiplayer-ready), EntityId, Log/Trace, settings contributions, sidecar registry, config legacy sections, SaveTypes aliases; full contract and event catalog |
| `work-packages.md` | The executable task list: waves, one brief per agent, file ownership, merge order, acceptance gates |
| `file-map.md` | Every one of the 482 `.cs` files (Core, 14 modules, developer/) with its owner agent, target module(s) and action |
| `filemap.json`, `ownership.json` | Machine-readable versions used by `tools/refactor/*.py` |
| `inventory/*.md` | Evidence: 7 inventories (file:line) written by the analysis agents. Read the matching one before touching an area. |

Tools in `tools/refactor/`: `check_ownership.py` (a branch only changed files its package owns), `check_module_refs.py` (no module references another module's types), `check_comments.py` (every method/property/constructor has a `///` summary), `filemap_coverage.py` (every source file is mapped, nothing left behind).

---

## 0. Amendments 2026-10-22 (second round) - these override anything below

| # | decision (Denis) | consequence |
|---|---|---|
| D11 | The refactor is built in a NEW local repo `V:\Repos\Apocalypter\WildWasteland2` (WW2), imported from WildWasteland HEAD 2e98d0b. Version 2.0.0, same plugin GUID `com.denis.apocalypter.wildwasteland` (WW2 replaces WW1; never installed together). | WildWasteland (WW1) stays untouched. |
| D12 | **Fresh start, no migration.** Players start new games with WW2. No compatibility with WW1/standalone-mod configs, saves, sidecars or custom data. | P1 is SUPERSEDED: module Ids, namespaces, config sections, `config/WildWasteland/<Id>/` and `plugins/WildWasteland/<Id>/` folders, embedded resource names all follow the NEW modules. DELETE: `SaveTypes` aliases for old assemblies, `ModuleConfig.Migrate`/`Legacy` first-run migrations, every `LegacyConfig.cs`/`*Migration.cs`, WW1 legacy sidecar readers and fallback folders (Apocapatrol/NPCAI/Apocaraider/Apocaunloader/Apocapocket/ApocalypterQuests_saves/QuestsCompat/ImmersiveMap import), readers for old binary/text schema versions (keep only the current version of each format), legacy asset folder migrations (LegacyPluginDir, LegacyPortraits, legacy templates import). KEEP: current save formats/keys (no reason to change them; new keys may use `WildWasteland.<Module>.` prefixes), ReplacementGuard/ConflictsWith (standalone mods can still be installed next to WW2), the AGENTS 5.10 rule "never add components to saved objects". |
| D13 | **Assets and local files are restructured too.** Every embedded/runtime asset lives in the folder of the module that uses it (`src/Modules/<Id>/Embedded/...`), installed to `plugins/WildWasteland/<Id>/...`; config/data/sidecars under `config/WildWasteland/<Id>/`. The csproj/build.sh RuntimePath special remaps are removed (plain `<Id>/<path>`). | Assets move in Wave B by the TARGET owner using the asset manifests written in Wave A (see work-packages.md "Assets"). Release packaging (`tools/package-release.py`) is updated in Wave C. |
| D14 | Camera rig -> Presentation (P2), vehicle assembly + build cache -> Vehicles (P4), town routing -> NPCAI (P6), weapon catalog in Gunplay (P7): confirmed. | |
| D15 | **Resumability.** Every agent keeps `docs/refactor/state/<package>.md` up to date (checklist of steps with done/next, current blockers, last good compile) and commits on its branch after every completed step. The coordinator keeps `docs/refactor/STATUS.md` (waves, branches, merges, open questions). Any agent can be restarted with: "resume package <id> from docs/refactor/state/<id>.md". | |
| D16 | Build verification for agents: Claude cloud workspace, `sh tools/refactor/compile.sh` (mono mcs against the game's reference DLLs copied read-only from the game folder to `/home/claude/refs`). Denis builds the final DLL with `dotnet build` on Windows. | |
| D17 | **Win32 error 1175 on save replace (fixed in WW 1.0.10 for MapFog).** Mono `File.Replace` / `File.Delete`+`File.Move` over an existing file fails on Windows with IO error 1175 and latches the save transaction as failed. | Every write that replaces an existing file (saves, sidecars, caches, editor data, templates, nav maps, road cache) MUST go through `Core AtomicFile.Replace(temp, target[, backup])` (same-directory MoveFileExW REPLACE_EXISTING\|WRITE_THROUGH). B01 adds the optional backup overload. Known offenders at import: POI PoiModule.cs:245, NPC Senses.cs:2080, NavMaps.cs:292, PoiNavData.cs:197, Quests CrewHistory.cs:62, Patrols BusPassengerPose.cs:46, EditorStore.cs:53-54, TrafficRoads.cs:378, Persistence.cs:554-555, Core SaveFiles.cs:103-105 - each owning Wave B package fixes its files; a Wave C grep must find no `File.Replace` and no `File.Move` onto an existing target outside AtomicFile. MapFog already fixed on refactor/main (WW 1.0.10 port). |

## 1. Goals and non-goals

Goals (Denis, 2026-10-22):
1. Fewer, clearly bounded modules (list in section 3). No spin-offs: one implementation per concern.
2. Core = true bridge: scheduler, frame budget, Harmony dispatchers, config, lifecycle, ModAPI (typed services + event bus). Modules talk ONLY through Core ModAPI. Third-party mods are reached ONLY through the Compatibility module, which itself talks only to Core ModAPI.
3. Higher performance, less stutter: fix the measured/identified hot spots (section 8), share scans, make all cost visible (Debug).
4. Multiplayer preparation: every mod-relevant event is a struct published through the Core event bus, with stable entity ids and world (not scene) positions, so a future transport can broadcast it.
5. Readable code: every function (method, constructor, property accessor with logic) carries a `///` summary comment; files hold one responsibility; dense ported style is allowed inside methods, not in structure.

Non-goals / hard constraints:
- Behaviour must not change except where this plan says so (bug fixes in section 9, explicit deletions in section 7). The refactor is not a feature pass.
- (D12) No compatibility with WW1 saves/configs. Within WW2, save formats stay as they are today (current versions only).
- Game runtime constraints from AGENTS.md 7.3 (no Reflection.Emit, no `AccessTools.FieldRefAccess`, no transpiling `ActionData.LoadActionField`, no Unity scene API in Plugin.Awake) still apply.
- Agents cannot run the game. Verification = Windows `dotnet build` zero errors + the lint scripts; in-game tests and perf captures are Denis's (AGENTS 0.3).

Authority: for branches named `refactor/*`, this plan explicitly overrides AGENTS.md section 0.1 (scope limits, "no refactors/renames") for the files a work package owns. Everything else in AGENTS.md (save rules, Harmony rules, performance rules 6, build 7, deploy 8) still applies. Section 0.4 auto-deploy does NOT apply to refactor branches: build only, deploy when Denis asks.

---

## 2. Decisions

### 2.1 Taken by Denis (2026-10-22)
| # | decision |
|---|---|
| D1 | Target modules: Core, Presentation, NPCAI, Gunplay, HUD, Patrols, Vehicles, POI, Survival, Loot, Tools, Compatibility, Debug (13 from the brief). |
| D2 | Core absorbs the Performance module (reviewed for redundancy, hooked into the Core bridge). |
| D3 | Debug module owns ALL logging with one `VerboseLog` level 0/1/2 (0 nothing, 1 main actions, 2 deep) and permanent performance tracing that shows which code costs frame time. No other log switches. |
| D4 | Quests (native Quests 1.8.2 port) stays its own module. Followers via NPCAI ModAPI; dialogue/journal/tracker UI in HUD. |
| D5 | Weather merges into Survival ("Survival & environment"); each feature keeps its own toggle. |
| D6 | New Saves module owns the save framework now in Core (named slots, journaled saves, autosave, backups/recovery, library page, combat-safe saving, cleanup). Core keeps only SaveBus lifecycle + sidecar API. Tiny vanilla fixes stay in `Core/Fixes`. |
| D7 | New Developer module (deprecation candidate) holds the live pose/placement editors: steering-wheel grip editor, weapon pose / aim-lift editor, bus driver/passenger pose pages, turret spot editor, merchant item placement. |
| D8 | Template recorder becomes part of the Patrols editor (Tools): a "Template" tab appears when the player sits in a supported vehicle to save all template properties; the same tab opens when editing an existing template. |
| D9 | Women of Wasteland (female skins/voices) is part of Presentation. |
| D10 | Faction car skins (Patrols `Paint`, crew appearance) are part of Presentation. |

Final module list (16 including Core): Core, Debug, Presentation, NPCAI, Gunplay, HUD, Patrols, Vehicles, POI, Survival, Loot, Tools, Developer, Compatibility, Quests, Saves.

### 2.2 Proposed defaults (taken by this plan; Denis may veto before Wave A starts)
| # | default | why |
|---|---|---|
| P1 | **On-disk names stay, code names change.** New module Ids/namespaces (`WildWasteland.Modules.Presentation`, `.NPCAI`, `.Gunplay`, `.HUD`, `.POI`, ...). Every module declares `StorageId` + `LegacySections`, so existing config sections (`[PlayerBody / ...]`, `[NPC / ...]`, `[Guns / ...]`, `[CleanHUD / ...]`, `[Towns / ...]`, `[Weather / ...]`, `[Performance / ...]`), `config/WildWasteland/<OldId>/` folders, `plugins/WildWasteland/<OldId>/` asset folders and embedded resource names keep working with zero value migration. Only absorbed modules' `Enabled` switches get a real one-time migration into feature toggles. | No user value can be lost; config harness keeps checking collisions. Renaming on disk buys nothing for players. |
| P2 | Camera rig (third-person camera, occlusion cutaway, cave camera, pick assist, FP eye shift) goes to **Presentation/Camera**; Core gets a small `CameraArbiter` that owns projection/FOV writes and mouse-wheel ownership (today ThirdPerson, Guns Ironsights and CleanHUD Binoculars each write the projection in their own onPreCull). | Rig is bound to body bones; arbitration is cross-module. |
| P3 | Player climbing/ladders/low crawl: one shared **traversal engine in Presentation** (`Presentation/Traversal`, player and NPC use the same lease, so ladder foot IK works for NPCs too). The player input layer that starts a climb sits next to it (`Presentation/Player`). NPCAI decides *when/where* an NPC climbs (PoiNav), Presentation executes it. | Matches "how to climb" (NPCAI) vs "climb animation" (Presentation). |
| P4 | Template-driven vehicle assembly (Patrol.Build pipeline, `VehicleBuildCache`, `TemplateFile` structure, `InputControl`, `Nwh` helpers) goes to **Vehicles** as the vehicle factory; Patrols keeps rosters, crews, cargo rolls, journeys. | Used by Patrols, POI (War Rig encounter), Tools (recorder), future towns. |
| P5 | World routing core (`TrafficRoads`, `RoadPlanner`, `VehicleRoute`, `ElevationGrid`) stays in **Patrols** and is exposed as `IVehicleRouting`. Towns' Harmony patches on it become a `IRouteHintProvider` registration. | Only vehicles route on roads; no second owner. |
| P6 | Town routing (`TownNavigation`, `TownApproach`, `TownOverlap`, `TownBakeData`, walker gates) moves to **NPCAI/Navigation** behind `INavApi` overlays/waypoint providers; POI only publishes town geometry (outline, gates, street graph, parking). | Towns patches NPC internals by reflection from 4 files today. |
| P7 | One shared **weapon catalog** contract in Core (`IWeaponCatalog`: kind, caliber, base damage, range, pellets, magazine, muzzle) with data owned and provided by **Gunplay**. Replaces 6 name classifiers in 3 modules. Base for the future gun-attachments addon (`IWeaponStats` modifiers). | Section 6.4. |
| P8 | Core `Factions` relations service (fed by Quests' factions.txt and Patrols rosters); Core `IItemSpawner` (save-registered item creation, 6 copies today); Core `PrefabCatalog`, `WorldIndex`, `FsmIndex`, `WorldTiles`. | Shared lookups listed in section 6.1. |
| P9 | `MeleeWheels` (Patrols), `ChainsawLimbs` (NPC, `#if false`), QuestsCompat, Towns dead files and `PlayerBody/Camera/Perf.cs` are deleted (section 7). | Dead or duplicated. |
| P10 | The standalone `developer/` profiler DLL is folded into the Debug module (one DLL); its capture format stays readable. | D3. |
| P11 | AGENTS.md is restructured in Wave C: the 140-line dated changelog at the top moves to `notes/agents-history.md`; AGENTS.md keeps rules + architecture (rewritten section 5 for the new layout). | 263 KB of mostly history costs every agent context and hides the rules. |

---

## 3. Target architecture

### 3.1 Modules and ownership
| module | owns (summary) | provides (Core contracts) | today's sources |
|---|---|---|---|
| **Core** | Boot/runner, ModuleHost, ModuleConfig (+legacy sections), Scheduler, WorldWork, LoadingProgress, Harmony dispatchers + Patches, ModApi registry + event bus + EntityId, GameRefs/PlayerState/Origin/WorldTiles/WorldIndex/FsmIndex/PrefabCatalog/ItemSpawner/Factions/Clock/Names/Layers/CameraArbiter, input (Hotkeys, KeyMap, GameBindings), SaveBus + SaveTypes + sidecar registry, utilities (Json, Glb, Gltf, Bindposes, Audio, Textures, Access, AtomicFile, DiskResources), Optimization (ex Performance), Fixes, UI shell + theme (ex CleanHUD GlyphText/AccentTheme), Apocasetter bridge, ReplacementGuard detection | everything in `core-modapi.md` 3 | Core/*, Performance/*, utility copies from 8 modules |
| **Debug** | Log level 0/1/2 + migration of all old switches; Trace: hook/job/frame timing, method probes, A/B capture, census; debug overlays and dump tools (nav dumps, faction dump, weather diagnostics) | `ILogPolicy` (level), `ITraceControl` | developer/*, Performance Telemetry/PerformancePage measurements, CorePages logging/perf/harmony pages, NPC DebugOverlay/NpcPages timings, Towns TownLoading, Quests FactionDump/QuestLogger, Weather diagnostics |
| **Presentation** | One character pipeline for player, human NPCs, female NPCs, animals (dogs/quadrupeds): models (glb/native), skins and voices (Women of Wasteland, faction outfits), animation driver (ModCharacter+CharPlan as the single model), weapon props/poses/aim lift, visible equipment, ragdoll, traversal engine (climb/ladder/crawl), seat poses (player car, crew, bus passengers, riders, followers), FP weapon motion (sway, aim transition, weapon lowering), camera rig, faction vehicle skins | `ICharacterPresentation`, `ICharacterAppearance`, `ITraversal`, `ISeatPoses`, `IWeaponPose`, `IVehicleSkins`, `ICameraRig` | PlayerBody (most), NPC Women/DogAnimations/ApBody/ReloadSounds + visual LateTicks of Burrow/Pounce/DogBite, Patrols Pose/PlayerAnims/BusPassengerPose(store)/RiderAnchorGuard/CrewAppearance/Paint + Rider graph, Guns WeaponTransition pose, Quests Hire recoil/arm poses |
| **NPCAI** | Senses, Brain, Idle (one movement driver), decisions (shoot/run/cover/reload timing), grid navigation + PoiNav graph + town routing overlays, animal behaviours (dog bite/jump, pounce, burrow, spider nests/webs), population rules (interior population, native POI population), followers (from Quests Hire), seated combat (PassengerGuard), simple convoy bandits, crew orders, boss health | `INpcApi` (extended), `INavApi`, `IFollowers`, `ISeatedCombat`, `IPopulation` | NPC (behaviour), Towns navigation/population, Patrols PassengerGuard/TrafficAgent sensing/SeatedLosGuard/PatrolVisibility, Quests Hire follower AI/QuestFollower/Takeover walkers, Guns Bosses |
| **Gunplay** | Weapon catalog data, ranges, damage, ballistics, projectiles, tracers (models, crossbow bolts stuck-in), melee (incl. tire cutting), ammo/magazines/unload/overflow, ironsights/zoom gameplay, weapon switch timing, unified hit detection | `IWeaponCatalog`, `IWeaponStats` (+modifier hook for attachments), `IWeaponState`, events WeaponFired/ProjectileHit/Hurt | Guns, HUD VanillaHits detection, NPC WeaponRanges/VanillaEvents ray extension/Aim spread/magazine table, PlayerBody Projectiles, Quests Hire gun stats |
| **HUD** | Everything drawn for the player: hit feedback, crosshair, CleanHUD (vitals, weapon panel, compass, map, fog, markers, inventory style, menus, loading/saving screens, notices, quest UI widgets, damage direction, binocular captions), traffic map drawing, vehicle status, inventory model pictures | `IMapMarkers`, `ICompassMarkers`, `ILocationMarkers`, `INotifications`, `IQuestPresentation`, `IUiTheme` consumer | HUD, CleanHUD, Patrols TrafficMap drawing/TrafficMapInk, PlayerBody VehicleHud/InventoryModel/portrait pictures, Quests IMGUI fallback, Core SaveWarning/saving overlay/replacement notice drawing |
| **Patrols** | Patrol/convoy/merchant/battle/foot journeys, spawning rolls, driving AI (Pilot), crews (seating, bail-out, return, bus defence, POI assault), ram, explode rules, traffic world model + persistence, town parking behaviour, merchant bus, road routing core, Patrols editor data model | `IPatrols` (queries), `IVehicleRouting`, `ICampStatus`, `ICullingProtection` (with Vehicles), events Journey*/Crew*/Ram*/Merchant* | Patrols (most) |
| **Vehicles** | Motorcycle, War Rig tanker, mod-vehicle factory registry, template-driven assembly + build cache, trailers, suspension tune, NWH/input helpers, ignition/cockpit controls (from PlayerBody), wreck darkening, storm shelter registration for rigs, ground recovery | `IVehicleFactory`, `IVehicleIgnition` (contract; EngineSim implements in Compatibility), `IVehicleControls`, events VehicleSpawned/Exploded/PartHit/Ignition | Patrols Vehicles/Motorcycles/WarRig/Spawning build pipeline, PlayerBody Car/IgnitionControl/WheelArms steering patch |
| **POI** | Custom POIs (gas station), POI registry/generators, towns: siting, planning, building, walls, dressing, markers registration, town save records, town templates (for the Towns editor) | `IPoiApi`, `ITownsApi` (geometry, parking slots, roles), `IWorldLocations` (camps/POIs registry) | POI, Towns (most), Patrols TrafficLocations/TrafficCamps discovery, War Rig PoiEncounter |
| **Survival** | Stew pot; weather: native storm replacement, hurricanes, sky/atmosphere/smoke/dust/debris, thunder, audio, storm hazards; shelter registry; day light query | `IWeatherRegions` (+`StormExposure`, `IsStormRoot`), `IShelterRegistry`, `IDayLight` | Survival, Weather, NPC Hazards/Storm (query part), Patrols NativeStormProtection storm part, DayLight |
| **Loot** | Loot lists/profiles assignable to POIs (incl. custom) and towns; spawn-area markers' item groups; spawner budget/progress; natural loot substitution (stew pot); cargo loot; pickup catalog; merchant goods lookup | `ILootTables`, `ILootSpawning` | Towns PoiSpawnCatalog/PoiDressMarkers item part/TownPopulation spawner budgets/TownFactory progress, Survival NaturalLoot, Patrols Cargo/PickupCatalog/LootProfile/MerchantCatalog.Goods |
| **Tools** | Navigation editor (PoiNav editor/view/bake/setup, nav map tools), POI editor (ex POI dresser + prop catalog + marker palette), Patrols editor (+ Template tab = recorder), Towns editor (new), LOD export | consumes only | NPC PoiNav editor files, Towns PoiDresser/PoiPropCatalog/marker kinds, Patrols Editor/*, Performance GeneratedLodExport |
| **Developer** | Steering-wheel grip editor, weapon pose / aim-lift editors, bus driver/passenger pose pages, turret spot editor, merchant item placement | consumes only | PlayerBody editors, Patrols TurretSpotter/MerchantEditor/BusPassengerPoseEditor/pose pages |
| **Compatibility** | One folder per third-party mod: Apocasaver, Vistas World Gen, Engine Sim, ChaseCamera, BloodFX, Weapons Pack (one bridge, 4 consumers), Dog Companion, Apocapocket, ApocaLanguage, Apocaspawner, Immersive Map import, MotorcycleMod, ApocaDustStorm, SigmaMod, CaveDarkness, Apocatalker, Part Adjustment | implements Core contracts (e.g. `IVehicleIgnition` for Engine Sim, `IInventorySlots` for Apocapocket, `IDrivingTelemetry` for ChaseCamera) | EngineSimCompat, Core VistasWorldGen/ApocasaverExclusion, bridges scattered in 8 modules |
| **Quests** | Quest engine, content, journal data, talk/interaction, rewards, reputation, takeover outcome, hire contract, voices content | `IQuestApi`, `IQuestConversation` | Quests (minus follower AI, UI, compat) |
| **Saves** | Named slots, journaled transactions, autosave + combat deferral, exit save, backups/recovery, library page, cleanup on save, world history, save terrain hold, single-player saving flow | `ISaves` (state, sidecar participation hooks, presentation events) | Core Save* files, WorldHistory, SaveLibraryPage |

### 3.2 Dependency rules (enforced by `tools/refactor/check_module_refs.py`)
1. A module may reference `WildWasteland.Core.*` and its own namespace only. No `WildWasteland.Modules.<Other>` identifiers, no reflection strings naming another module's types (`"WildWasteland.Modules.X..."`), no Harmony patches on another module's methods.
2. Core references no module type. The only exception is `Core/Boot/ModuleRegistry.cs` (constructs the module entries).
3. Compatibility references third-party assemblies only by reflection/OptionalAccess, and WW only through Core ModAPI.
4. Cross-module calls: `ModApi.Get<T>()` queries (null when the provider module is off - every consumer handles null), or events through `ModEvents`. Settings rows for shared pages through `SettingsShell` contributions. Persistent data through Core sidecar/ES3 key helpers registered with Saves.
5. Module-to-module *behavioural* dependency (e.g. HUD DamageDirection needs Gunplay `Hurt` events) is documented in `docs/modules.md` 1.3 and degrades gracefully when the provider is off.

### 3.3 Source layout after the refactor
```
src/Core/
  Boot/          Plugin.cs, ModuleRegistry.cs, Runner.cs
  Modules/       IModule.cs, ModuleBase.cs, ModuleHost.cs, ModuleConfig.cs, LegacyConfig.cs, TunedSettings.cs
  Scheduling/    Scheduler.cs, Job.cs, WorldWork.cs, LoadingProgress.cs
  Harmony/       Hooks.cs (table-driven), HookTargets.cs (forwarders), Patches.cs
  ModApi/        ModApi.cs, ModEvents.cs, EntityId.cs, Transport.cs, Contracts/<Domain>.cs, Events/<Domain>Events.cs
  Game/          GameRefs.cs, PlayerState.cs, Origin.cs, WorldTiles.cs, WorldIndex.cs, FsmIndex.cs, FsmUtil.cs, FsmPause.cs,
                 PrefabCatalog.cs, ItemSpawner.cs, Factions.cs, Clock.cs, Names.cs, Layers.cs, CameraArbiter.cs
  Input/         Hotkeys.cs, KeyMap.cs, GameBindings.cs
  Persistence/   SaveBus.cs, SaveTypes.cs, Sidecars.cs, SaveKeys.cs, AtomicFile.cs, DiskResources.cs
  Util/          Json.cs, Glb.cs, Gltf.cs, Bindposes.cs, Audio.cs, Textures.cs, Assets.cs, Access/ (one accessor generator)
  Log/           Log.cs, Trace.cs
  Optimization/  Framework/, PlayMaker/, Content/, Rendering/, World/      (ex Performance, preset + per-feature switches)
  Fixes/         HealthStacks, ITweenGuard, QuietFsm, ColorGrading, InventorySupplies, VehicleLoadPlacement, PauseCursorFix, DuplicateRigidbody
  Bridges/       ApocasetterBridge.cs, ReplacementGuard.cs
  UI/            ToolWindow, ToolPage, Ledger, Skin, EditorSession, SettingsShell, Theme/ (GlyphText, AccentTheme, PanelTextures)
src/Modules/<Id>/   <Id>Module.cs (entry), <Id>Settings.cs (all binds), <Id>Api.cs (ModAPI provider), feature subfolders
```
Every module entry follows the existing `ModuleBase` contract (AGENTS 5.3) plus: `StorageId`, `LegacySections`, `ProvideApis()`, `Settings(SettingsShell)`.

---

## 4. Core design (summary; code sketches in `core-modapi.md`)

### 4.1 Kernel changes
| area | change | evidence |
|---|---|---|
| ModApi | `ModApi.Provide<T>(owner, impl)` / `Get<T>()` / `TryGet<T>()`; provider entries cleared automatically when the owner shuts down; `Services.*` fields become `[Obsolete]` forwarders during Wave B and are deleted in Wave C | Services.cs 402 lines of mixed fields/raw statics (`PlayerLadder`, `LadderClimbers`, `PoiNavSetup` volatile bool) |
| Events | `ModEvents.Subscribe<T>(owner, handler)` / `Publish<T>(ref T)` for `struct` events; zero allocation dispatch through a generic static handler array; per-handler try/catch counted toward the module error rule; per-handler timing when Debug tracing is on; `[Broadcast(Authority.Host/Owner)]` attribute + optional `IEventTransport` for a future multiplayer mod | `Services.ClearEvents()` never called (Services.cs:81); `Action<GameObject,...>` events carry Unity objects |
| EntityId | `EntityId` (long) for every replicated thing: ES3 reference id when the object is saved, otherwise a WW-assigned id (`Entities.Assign(go)`); `Entities.Of(go)` / `Find(id)`; positions in events are `Origin.World` | MP readiness |
| Scheduler | job names enforced `<ModuleId>.<Name>`; `RemoveAll` marks `Job.Removed` (bug); `Interval` keeps phase (`NextAt += Interval` with catch-up clamp); named tick methods required (Patrols registers 19 lambdas); Debug reads per-job counters | Scheduler.cs:152-159, :195; CompileQueue.cs:36, GuiLoopFeature.cs:83 |
| Hooks | table-driven registry (one list drives Apply/RemoveAll/Summary - fixes missing `FsmEnable.Remove`); `Calls` counted for all kinds; optional per-handler Stopwatch (Debug level 2 or "Trace hooks"); new dispatchers: `SetAudioClipEnter`, `UiTextSetTextEnter`, `DoMousePick`, `FsmAwake`, `FsmInnerEnable`, `FsmDisable`, `FsmDestroy`, `FsmPhysicsCallback`, `ExplosionShouldApplyForce`, `GetGameObjectFsm` | Hooks.cs:235-243; direct double patches listed in `inventory/quests-compat.md` 10b |
| WorldWork | becomes the single main-thread heavy-work arbiter: absorbs Patrols `BuildQueue` and `VehicleBuildCache`'s own 12 ms loading loop; categories become registered owners instead of constants; Patrols `EventBudget` is renamed `EncounterCap` (it is not a frame budget) | inventory/patrols.md 5 |
| LoadingProgress | adds a "hold" registration (Towns `BeforeHandoff`, Patrols `PatrolLoadPreparation`, VehicleLoadPlacement share one load barrier) | inventory/patrols.md, world.md |
| Config | `ModuleConfig` gets `StorageId`, `LegacySections` (old section prefix aliases), `MigrateMissing(entry, file, section, key, convert)` (replaces Quests/Survival/CleanHUD/NPC-Women copies), and WW-to-WW `MoveEntry(oldSection, oldKey)` that runs every start until the new entry is set; Towns stops binding with `file.Bind` directly | inventory/quests-compat.md 10e |
| Settings shell | `CorePages` loses all ~40 module references: PLAYER/WORLD/HUD/SYSTEM pages are laid out by Core from `SettingsRow` contributions each module registers; Core draws, modules own entries | CorePages.cs:5-8, ~40 sites |
| SaveBus | stays the only SaveLoadGame watcher; gains typed events (`SaveBegin`, `BeforeSerialize`, `AfterSerialize`, `SaveEnd`, `LoadBegin`, `WorldReady`), `SaveBus.Key(name)` ES3 key helper (write cache + StoreCachedFile + verify; 6 copies today), `Sidecars.Register(owner, relativePattern)` (replaces the hard-coded list in SaveFiles.cs:183-190 and the Towns special case SaveSystem.cs:264) | |
| SaveTypes | per-type alias table + **same-assembly aliases** (`WildWasteland.Modules.Patrols.X, WildWasteland` -> new namespace) because saves written by WW 1.x contain WW's own type names; longer prefixes first; covers the 27 saved Patrols component types and any other moved saved type | SaveTypes.cs:31-36; inventory/patrols.md 9 |
| Log | `Log` stays a Core facade (modules never reference Debug): `Log.Level` (0/1/2), `Log.L1`/`L2` guards, `Log.Main(owner, msg)`, `Log.Deep(owner, msg)`, generic overloads so callers never build strings when the level is off, `Warn`/`Error` always (rate-limited). `Log.Error` no longer aborts saves (it calls `SaveSystem.Fail` today); Saves gets an explicit `SaveBus.ReportFailure` channel | Log.cs:23-24 |
| Access | one accessor generator (`Core/Util/Access`) from NativeAccess + OptionalAccess + Performance FastReflect + BatchCompiler + CompileQueue; replaces 30 `Expression.Compile` sites (interpreted on this runtime) incl. `ApocasetterBridge.InputBlocked` read every frame | inventory/core-performance.md 5.3 |

### 4.2 Shared game services moved into Core (P8)
`GameRefs` (+ `PlayerState`: in car, velocity, move state, standing height, car root, jump - from PlayerBody `Game.cs`, Patrols `PlayerRef`, Quests `Game.cs`), `WorldIndex` (one sliced root walker: CleanHUD SceneCache, Quests QuestWorld, EngineDiscovery, Nav/Gungirl/Nests walks), `WorldTiles` (one MapMagic subscription: 7 today), `FsmIndex` (Performance PM1 registry made public: 9 full `FsmList` scans today), `FsmPause` (one `ManualUpdate` owner with owner bits: FS1 and FsmEdits fight today), `PrefabCatalog` (Patrols Prefabs, Towns catalogs, Quests FindPrefab/ScanForPrefab), `ItemSpawner.RegisterSaved` (6 copies), `Clock` (DayTime FSM, 5 readers), `Names.Base` (6 `(Clone)` strippers), `Layers` constants, `Factions`, `CameraArbiter`.

### 4.3 Optimization (ex Performance)
All 43 features move to `Core/Optimization/*` unchanged in behaviour; settings keep `[Performance / ...]` sections (P1). Changes:
- Explicit feature list instead of `Assembly.GetTypes()` discovery (PerformanceModule.cs:62); no module switch hop: `[Performance] Enabled` migrates to `[Performance / Preset] Enabled`.
- Patch-target duplicates inside Performance go through the new Core dispatchers (FsmRegistry+GuiLoop on OnDisable/OnDestroy, FsmRegistry+FsmSleep on Fsm.OnEnable, TriggerProxy+FsmSleep on physics callbacks, FsmEdits+FsmSleep on Awake).
- FS1 FsmSleep and PollSleep/FsmEdits share one `FsmPause` owner-bit service.
- `PatrolsBridge` (reflection into Patrols) deleted; Culler asks `ICullingProtection`. `CarLod` namespace sniffing replaced by `IVehicleOwnership` query (Patrols/Vehicles/Compatibility answer).
- `LightPolicy` 10 s `FindObjectsOfType<Light>` (LightPolicy.cs:190) and `SunShadows` 5 s scan removed (RenderScan incremental + world-ready pass).
- `SigmaFsmStates` moves to Compatibility and is only patched when SigmaMod is loaded (today it postfixes `PlayMakerFSM.FsmStates` for every call).
- `Telemetry`/measurements move to Debug; `GeneratedLodExport` to Tools.
- `WeatherVisualPolicy` provider stays (Core Optimization -> Survival weather renderer).

### 4.4 Fixes
`Core/Fixes`: HealthStacks, ITweenGuard, QuietFsm (switch moves from `[Patrols / Debug]` to Debug level policy), ColorGrading, InventorySupplies (supply drops; HUD draws), VehicleLoadPlacement (driven by SaveBus load events), PauseCursorFix (from CleanHUD), DuplicateRigidbody (CT9 from Performance). Each fix: one file, own `Fix.Install/Remove`, no settings unless it already had one.

---

## 5. Debug module (D3)

### 5.1 One log level
`[Debug] VerboseLog` becomes an int 0/1/2 (`AcceptableValueList`), new entry key `VerboseLevel` in the same section so the old bool stays readable. One-time migration: old global `VerboseLog=true` -> 1; any old per-module/per-feature log switch true (list below) -> 2; otherwise 0. Default for new installs and releases: 0. Every other log switch stops being bound (never purged).

Switches retired (source: inventories 4): `[NPC / Debug] VerboseLog` (+ aliases BrainLog/SensesLog/NavLog), `Burrow Log`, `Dogs Log`, `Nests Log`, `Pounce Log`, `NavDump` (becomes a Debug action button), `[Patrols / Debug] Hide missing-FSM warnings` (level <2 hides), Quests `QuestLogger`, Weather diagnostics switches, developer `[Logging] *`, `[Performance / Telemetry] Periodic census` (becomes a Debug measurement toggle, not logging), CleanHUD `Log.Always` misuse (19 sites at level 1/2 now). `ShowNavigation` and other *visual* debug overlays stay as Debug overlay toggles (not logging).

Level semantics:
- **0**: nothing except Warn/Error (rate-limited) and the startup summary.
- **1 (main actions)**: module start/stop, state changes, spawns/despawns of groups, journeys, towns built, saves done, compatibility bridges resolved, perf summaries every 30 s (top 10 jobs/hooks ms/s).
- **2 (deep)**: per-decision traces (Brain/Senses/Nav/Pounce/Burrow/Nests/Dogs), per-frame diagnostics, FSM warnings, migrations detail, Vistas own log, PlayMaker performance warnings, spike traces with lifecycle tags.

Rules: no string building at a disabled level (`if (Log.L2) Log.Deep(this, ...)` or generic overloads); every message is prefixed `[<ModuleId>]` by the facade, not by callers; no `Debug.Log` / own `ManualLogSource` anywhere except the documented vanilla-identical message in ActionDataPort.

### 5.2 Performance tracing (always able to say what is slow)
| capability | implementation |
|---|---|
| Scheduler jobs | Core keeps counters in `Job`; Debug derives ms/s per job and per module once per second (developer Measurements logic) |
| Harmony hooks | Core `Hooks` per-handler timing (owner module + target) when Debug "Trace hooks" is on or level 2; this is the missing piece today (fps-regression-1.0.5 review: PoiNav cost hidden inside hooks) |
| Events | per-handler timing in `ModEvents` with the same switch |
| Frames | one 8192-frame ring with p50/p95/p99, spikes tagged by lifecycle bitmask (tile applied, save, load, origin jump, compile, scene, world work) |
| Engine counters | one ProfilerRecorder set (today Telemetry and developer run two identical sets), started only while measuring |
| Method probes (level 2) | developer `Instrumentation` installed through `Core.Patches` with owner Debug (no own Harmony id), sliced install, removed when the level drops |
| Feature stats | `IStatsSource` on Optimization features (SendGo, Broadcasts, Sleeps, Wakes, PickQueries...) listed on one page |
| Capture A/B | 30 s capture, JSON/CSV/MD export under `config/WildWasteland/Debug/Captures/` (developer format), settings/build hashes, comparison |
| Census | FSM census + registered collection counters |

Budget gates stay: WW managed avg <= 0.5 ms/frame, worst <= 2 ms (AGENTS 5.4); the Debug page shows the budget line.

---

## 6. Module notes (what each Wave B agent must achieve)

### 6.1 Presentation
- **Single character driver.** Today the player uses `Body`+`Loco`+`CrawlBody`+`PlayerTraversal`+`FirstPersonJump`, NPCs use `ModAPI.Character` (`ModCharacter`) + `CharPlan` (CharPlan.cs:6-9 says it is the player's logic with inputs swapped). Keep ModCharacter+CharPlan as the one driver; the player becomes a client through a `PlayerInputAdapter`; delete duplicated rig/arm-lift/spine twist/aim lift/turn-in-place/upper-rig clone/avatar mask code in Loco/Body (inventory/playerbody.md table at top). Six PlayableGraph builders become one.
- One model builder (Model/CharacterModels + NPC Gungirl + glb skins), one appearance service (Women of Wasteland, crew appearance aliases, faction outfits), one voice/character audio profile (FemalePain, Women Voice, NPC reload sounds - pooled, no `new GameObject` per sound).
- Animals: DogAnimations clip/IK/bone fade, Burrow/Pounce/DogBite visual LateTicks via "visual root offset" API; NPCAI keeps decisions.
- Seats: one `SeatPose` for player car seat, WheelArms hand IK, crew `Pose`, bus passengers, riders (Rider's own PlayableGraph), quest followers. Saved type `Pose` keeps its old name resolvable (SaveTypes alias).
- Traversal (P3) unified lease; ladder state published as events/queries instead of raw `Services.PlayerLadder*` fields.
- Weapon pose/props/aim lift (GunPose/AimLift runtime parts), FP weapon motion (AimTransition + FirstPersonSway + Guns WeaponTransition `Pose()` lowering).
- Camera rig (P2) under `Presentation/Camera`, writing projection only through `CameraArbiter`.
- Vehicle skins: Patrols `Paint` (faction paintjobs, cargo container texture by Loot key).
- Perf: ModCharacter allocates a Dictionary per frame per character (ModCharacter.cs:351); each NPC attach clones the whole NPC to build its rig (208-260); all NPC characters animate every frame with culling forced off - add distance LOD (update rate tiers) coordinated with Optimization RC6 AnimatorCulling; `Crawling.Active` string compares in every GetButton/FSM-event hook; `GameBindings.Find` per frame; `Props.Build` repeated full scans while the catalog is empty; load clips/bundles/avatars at Init (sliced), not on first use.

### 6.2 NPCAI
- Keep: Senses, Brain, Idle, Ambush, SimpleBandits, CrewOrders, Animals behaviour, Hazards Nests/Webs, Navigation (grid), PoiNav (runtime), population rules, Bosses (from Guns), follower AI (from Quests Hire/QuestFollower/Takeover walkers), seated combat (Patrols PassengerGuard), player perception queries (Patrols TrafficAgent/PatrolVisibility sensing), SeatedLosGuard.
- **One navigation service** (`INavApi`): grid is the baseline; PoiNav graph is canonical for multi-level human movement where data exists; town routing (P6) becomes overlays/waypoint providers (no reflection from POI). Delete `LearnLadderSide` (dead after `return`), `MasksNow`, `PoiNav.cs:31` LadderSpeed reset (overrides user choice every start). Move `WalkerTick` out of the debug view job.
- Brain and Idle are two movement drivers: unify behind one `Mover` (Brain.Drive/Idle.Drive).
- Perf: O(N^2) NPC-vs-NPC sight raycasts (Senses.cs:269-280) -> spatial hash + per-tick budget; `FindGameObjectsWithTag("Hide")` every 10 s from think path -> WorldIndex cache; LINQ on dog register; Senses/Idle/Pounce log helpers build strings before the switch check.
- Women population roll moves to Presentation (D9) - NPCAI publishes `NpcSpawned`; Presentation decides the variant and keeps the saved `Gungirl(Clone)N` names.
- Weather overlap: Hazards/Storm.cs re-reads native storms with a fixed 2500 m circle and treats storms whose `SandPlayer` FSM is disabled as parked, while Weather disables that FSM on every storm it replaces -> NPC storm sight/hearing effectively off with Weather on (plausible, needs Denis's check). Replace with Survival `StormExposure(pos)` query.

### 6.3 Gunplay
- Single hit pipeline: `Hurt`/`ProjectileHit` events from Gunplay (projectile, vanilla FSM damage observer from HUD VanillaHits, melee) consumed by HUD (hit feedback, DamageDirection), NPCAI (knowledge, replaces `INpcApi.Hurt` + `Services.NpcHurt`), Quests (kills), Patrols (VehiclePartHit). Today DamageDirection only works while the NPC module runs (Guns -> NPC -> NpcHurt -> CleanHUD).
- Weapon catalog (P7) replacing `Tracers.Classify`, NPC `WeaponRanges`, CleanHUD weapon table, AmmoBox calibers, hard-coded multipliers in `MakeGun` (crossbow x2.5, redmark, chopped shotguns), PlayerBody Props/Equipment classifiers, ApBody `IsPistol`.
- Attachments-ready: every stat read goes through `IWeaponStats.Get(weaponKey, stat)` with an ordered modifier list (`IWeaponStatModifier` registration) - the future addon registers modifiers, no Gunplay edits.
- Ironsights split: toggle/zoom target -> Gunplay; projection write -> Core CameraArbiter (shared with Binoculars and ThirdPerson).
- WeaponTransition split: switch delay -> Gunplay; lowering pose -> Presentation.
- NPC magazine table and `Aim.SpreadFactor` from NPC -> Gunplay; NPCAI keeps *when* to reload/fire.
- Merge `ArmorOverflowTransfer` into `OverflowTransfer` (line-for-line duplicate).
- Perf: `MetalSparks` GetComponentsInChildren per spark; per-hit GetComponents in Tracers; `BoltSurface` bakes skinned meshes synchronously; first-hit/first-ammo-drop prefab scans mid-combat -> PrefabCatalog warm-up; Bosses scene scan every 60 s -> NPCAI registry.

### 6.4 HUD
- Merge HUD + CleanHUD entries into `HudModule`; two `Enabled` switches -> `[HUD] Enabled` + feature toggle `Clean HUD` (migrated from `[CleanHUD] Enabled`).
- Rename to remove collisions: HUD `Hud` -> `HitFeedbackOverlay` (drawn on the uGUI canvas), CleanHUD `Hud` -> `HudView`, `QuestMap` -> `MapMarkers` (not quest-specific).
- Theme (GlyphText/GlyphFont, AccentTheme, panel textures, `DialogueWidgets.Gold` palette) moves to Core/UI because Core's F3 window, notices and saving overlay use them; HUD consumes.
- Map: TrafficMap drawing + TrafficMapInk from Patrols; Towns' Harmony patches on `LocationMarkers.Observe` / `QuestMap.Tick` replaced by `ILocationMarkers` (exclude root, set icon/label).
- Saves presentation (loading/saving screens, autosave countdown, saving overlay) driven by Saves events, not by polling Core save internals; fog/locations sidecars written once per save through the sidecar API (2-3 synchronous writes per save today).
- Keys.cs lists WW bindings through Core Hotkeys instead of Mono.Cecil scanning WildWasteland.dll.
- Perf: LoadingScreen reloads/decodes images every save; `GameObject.Find` loops in menus (crosshair, map camera); crosshair `NeedsCocking` reads weapon FSMs -> `IWeaponState`.

### 6.5 Patrols
- Split (inventory/patrols.md 1a): `Patrol.cs` (1890 lines, 9 classes) -> Vehicles (assembly, `Nwh`, `InputControl`, `ExitSpeed`), Core (`Prefabs` -> PrefabCatalog, `Register` -> ItemSpawner, helpers), Survival (`DayLight`), Patrols (crew/encounter). `Rider.cs` -> Patrols (seat slots/spawn), NPCAI (behaviour), Presentation (graph/poses), Gunplay (lance projectile), Developer (pose editing). `Passenger.cs`, `QuestFollowers.cs` -> NPCAI/Presentation/Patrols.
- Remove direct NPC control: `Crew.MuteAi` -> `INpcApi.SetSeated`; `PassengerGuard` vanilla attack FSM driving -> NPCAI `ISeatedCombat`; `TrafficActor` sensor tag reflection -> NPCAI; War Rig rider leap/throw/detonate behaviour -> NPCAI; own player sight/hearing in `TrafficAgent`/`PatrolVisibility` -> NPCAI perception query.
- Towns coupling (both directions, 24+12 sites incl. `Pilot.cs:223` per driving decision) -> `ITownsApi` (POI) + `IRouteHintProvider` (P5).
- Persistence: keep the `Apocapatrol.Patrol` key/schema 14/WDR2, but use SaveBus key helper + sidecar registry; remove own quit handler/flush timer duplication (flush scheduled twice). `Convoy.AutosaveSoon` asks Saves, not the retired Apocasaver by reflection.
- Cleanup: four far-object removal systems -> one Patrols cleanup participant + Core culler protection query.
- Jobs: 19 lambdas -> named methods; `WorldTraffic.Tick` exposes its 4 sub-systems as separate jobs; 156 verbose calls build strings before the level check; per-car linear journey search per driving decision -> dictionary; motorcycle prefab lookup repeats assembly scan + reflection per lookup; merchant bus setup scans every loaded object.
- Dead: `DebugSpawn`/`DebugRoll`, `RoadRoute`/`LegSafe`, `CanTemplate`, `ResolveType`, old Apocaspotter `RiderApi` part, `FootAlive`, `MoveTemplate`, `TrafficFoot` synchronous `RoadPlanner.Build` on main thread -> worker.

### 6.6 Vehicles
Factory registry (`IVehicleFactory.Register(kind, builder)`; MotorcycleMod/other mod vehicles registered by Compatibility instead of an all-assembly scan in `ModVehicles`), template assembly + `VehicleBuildCache` (rule kept: consume the prepared instance, never clone it), motorcycle, War Rig (factory, glb, props, layout, unit, ground recovery), trailers, suspension (`SuspensionTune`; Part Adjustment reflection -> Compatibility), wreck darken/deaden from `Explode`, ignition/cockpit controls from PlayerBody `Car`/`IgnitionControl` (+ WheelArms `BeforeSteer` patch and forced `returnToCenter`, which change driving), `ExitSpeed`. War Rig shelter: registers rigs with Survival `IShelterRegistry`. Saved types `MotorcycleBalance`, `WarRigUnit`, `SuspensionTune`, `MotorcycleRearAccess` keep resolvable names.

### 6.7 POI
Towns + POI into one module (Id `POI`, StorageId keeps `Towns`/`POI` folders and `[Towns / ...]`/`[POI / ...]` sections). Publish geometry (`ITownsApi`: outline, gates, street nodes/edges, parking slots, roles/conditions) and world-locations registry (`IWorldLocations`, from Patrols TrafficLocations/TrafficCamps + Towns markers; Quests and NPCAI also use it). Town template data model (inventory/world.md 8b: `TownTemplate`/`LotRule`/`ParkingSlot`, TownStore v7 with `TemplateId`, v1..v6 map to a default template per faction) for the Towns editor. Delete dead files (section 7). Move debug/tuning entries out of player-facing settings.cfg (Towns binds them there with `file.Bind`). Perf: POI prop/spawn catalogs call `Resources.FindObjectsOfTypeAll` during play -> PrefabCatalog at world-ready; `TownAssets.Load` synchronous prefab + .wwnav scan on first tile prepare -> sliced at load; string compares in ProcessEvent/EnterState/Wait hooks (InteriorPopulation, NativePoiPopulation, Towns Wait) -> cached FsmEvent/FsmState references. Gas-station glb merge must produce identical vertices (mesh-name SHA feeds NPC nav-bake signature).

### 6.8 Survival
Stew pot + Weather. Weather renderer stays as is (Denis is still tuning visuals). `WeatherNative` becomes the owner of storm identity (`IsStormRoot`, prefab id 5697600806714206537) used by Vehicles (War Rig) and Compatibility (DustStorm); `IShelterRegistry` replaces the implicit War Rig shelter (today Weather hazards only get it because they call the Patrols-patched `Explosion.ShouldApplyForce`). Perf: `WeatherHazards` runs every FixedUpdate with a huge OverlapBox + 5 km raycast per hurricane column and is always enabled -> gate on active storms near the player, cache ground per column. NaturalLoot -> Loot rule registered by Survival; SpawnerBridge -> Compatibility.

### 6.9 Loot (new)
Seed module with: `LootTable` (id, entries weighted, count range), `LootProfile` assignment (`PoiType`/custom POI type/town template lot -> table id), spawn-area markers' item groups and per-instance progress (ES3 key `WildWasteland.Towns.PoiDressMarkers.v1` stays), spawner budget/progress (from TownPopulation/TownFactory), natural loot substitution table (stew pot rule), cargo loot (Patrols Cargo), pickup catalog, merchant goods lookup. Existing `PatrolsAndLoot.json` (schema 3) holds patrol groups AND loot profiles: Loot reads the loot part from the same file until a migration writes `Loot/LootTables.json` (copy-missing, original retained). No new gameplay in this refactor - only the data model + moving the existing behaviour behind `ILootTables`/`ILootSpawning`.

### 6.10 Tools
Navigation editor (PoiNavEditor, PoiNavView display, PoiNavBake, PoiNavSetup -> `WorldGenerationSuspended` flag via Core ModAPI, NavMapTools, NavMapHighlight), POI editor (PoiDresser renamed `PoiEditor`, PoiPropCatalog editor parts, marker palette), Patrols editor (TemplateMenu/Ledger/EditorSelection + **Template tab** from TemplateExporter, D8), new **Towns editor** (list the 4 town types Animals/Mutants/Scrapyard/Coyotes with Walls on/off checkbox; per type: allowed POIs incl. custom ones, POIs usable as external parking lot; writes town templates through `ITownsApi`), LOD export (RC9). Tools reaches gameplay modules only through ModAPI; editors use `EditorSession`.

### 6.11 Developer (D7)
WheelGripEditor, GunPose/AimLift editor parts (Set/Save/Copy/Paste/Numpad control), PlayerBodyPages PoseEditor/ArmButtons/Steering page, BusPassengerPoseEditor + pose page, TurretSpotter + page, MerchantEditor + MerchantSpotPage. Talks to Presentation/Patrols only through ModAPI (`IPoseAuthoring`, `ISeatPoseAuthoring`, `IMerchantLayouts`). Can be switched off as a whole; nothing else depends on it.

### 6.12 Compatibility
`CompatibilityModule` + one folder per mod (`Compatibility/<Mod>/<Mod>Bridge.cs`), each with: detection (GUID), version-tolerant resolution (AGENTS 5.9: never reject on version), one toggle `[Compatibility / <Mod>] Enabled` (migrated from old `[EngineSimCompat] Enabled` etc.), and it only implements/consumes Core contracts. Weapons Pack becomes one bridge serving four consumers via events (`NpcFrightened`, held-prop shadow query, HUD icons, melee hit sound). Apocasaver: exclusion + loading presentation + hand pose store in one folder. Apocasetter stays in Core (`Core/Bridges`), including the Towns `SettingsUI` patches (moved there).

### 6.13 Quests
Keep engine/content/journal data/reward/reputation/hire contract/takeover outcome/AQ1 format and ES3 key `ApocalypterQuests` (rewrite the save I/O typed instead of reflection over all assemblies). Move out: follower AI -> NPCAI, seat code -> Vehicles/Patrols crew API, recoil/arm poses -> Presentation, gun stats/sounds -> Gunplay, IMGUI fallback UI -> HUD, Apocatalker bridge -> Compatibility, `Game.cs` shared access -> Core, `QuestWorld` -> Core WorldIndex, KeyMap (byte copy of PlayerBody's) -> Core, `ReadWav` -> Core Audio, `FactionDump`/`QuestLogger` -> Debug. `Hire.cs:281` destroys Patrols components by type-name string -> `IFollowerVehicles.Release`. Dead: `Hire.EarlyTeach`/`LogGuard` (called every frame), `IsLoadingState`, `HasNpc`, `FindApi`, old GUID consts, unused `[Quests / Legacy] Apocasetter` bind (stop binding, never purge). Voice playback is hard-disabled (`Voice.cs:16`) - keep the content path, decoder goes to Core.

### 6.14 Saves
Move: SaveSystem, SaveFiles, SaveRuntime (menu/launch/identity), SinglePlayerSaving, SaveCleanup, SaveCombat, SaveTerrain, WorldHistory, SaveLibraryPage, save parts of SaveCompatibility/SaveWarning. Combat deferral consumes `WeaponFired`/`NpcStateChanged`/`VehicleCombat` events instead of `Services.ReportCombat`/`VehicleCombatActive`. Participants come from `Sidecars.Register`. Saving overlay/countdown drawing -> HUD. Perf: SaveCombat/SaveRuntime string compares per FSM event -> cached `FsmEvent` refs; WorldHistory `GetComponentsInChildren` + new List per tile move -> per-prefab cache.

---

## 7. Redundancy and dead code to remove (consolidated)
| item | evidence | action |
|---|---|---|
| QuestsCompat module (3 files) | never registered, Core/Plugin.cs:126-140 | delete; keep CrewHistory fallback read one release |
| `NPC/ChainsawLimbs.cs` (158) | `#if false`, Denis 2026-10-21 | delete |
| `Patrols/Crews/MeleeWheels.cs` (251) | duplicate of Gunplay melee-on-wheels (Tracers.cs:955/1163) | delete |
| Towns `TownRoadBridge`, `TownSurfacePaint`, `TownPavement`, `TownDebug`; `TownPaths.Grid/Build` (244-552); `BindStandalone`; `TownLoading.Report`; disabled gas-station enemy spawners; unused `TownsApi` members; `TownLot.Actors/Loot` saved never read (keep reading for format) | inventory/world.md 5 | delete (keep save fields) |
| `PlayerBody/Camera/Perf.cs` | covered by Scheduler + Debug | delete |
| PlayerBody dead: `GunPose.Poses/Effective(int)`, `ModAPI.AimLiftOf`, array `AimLift.Elbow`, `CarSeat.FindSource`, `Model.ArmsLeft` (+ dead left-arm path in Arms.cs), MP stubs `Crawling.DownedProbe`/`InstallMultiplayer`/`CaptureCrawlMotion`, non-Mixamo fallback path, 0.5/0.6 weapon-grip conversion (after one more release) | inventory/playerbody.md 5 | delete |
| NPC dead: `Brain.LateTick` (empty, unregistered), `PoiNav.LearnLadderSide`, `MasksNow`, `NavMapHighlight` (once the vanilla pack is confirmed complete: 41 maps cover 39 types) | inventory/npc.md 5 | delete |
| Patrols dead: 14 members (`DebugSpawn`, `DebugRoll`, `RoadRoute`, `LegSafe`, `CanTemplate`, `ResolveType`, Apocaspotter `RiderApi`, `FootAlive`, `MoveTemplate`, ...) | inventory/patrols.md 5 | delete |
| Quests dead: `EarlyTeach`, `LogGuard`, `IsLoadingState`, `HasNpc`, `FindApi`, old GUID consts, most of `Quests/KeyMap.cs`, `IFollowerVehicles.ResolveType` | inventory/quests-compat.md 5 | delete |
| Core dead: `Legacy.ParseFloat`, `ModuleHost.Restart` (unused, keep if Tools needs it), `SaveTypes.Resolved`, `Origin.Jumps/LastDelta`, Skin `PlankButton/Frame/PitBox/SlotBox/HazardStrip` | inventory/core-performance.md 5.2 | delete |
| Performance `PatrolsBridge` | reflection into Patrols | delete after `ICullingProtection` |
| Duplicate utilities: 8 JSON parsers, 3 Glb, 2 Gltf, 2 Bindposes, 5 WAV decoders, 5 async audio loaders, 18 PNG loaders, 2 KeyMaps, 9 scene walkers, 7 tile subscriptions, 9 FsmList scans, 6 save-list registrations, 5 clock readers, 6 `(Clone)` strippers, 6 ES3 key writers, 4 accessor generators, 4 frame-stat collectors, 2 INI parsers, 4 collider-bounds copies, 3 nav-grid edge walks, 6 "native POI name" predicates, 2 terrain-probe copies | inventory/quests-compat.md 10d, world.md 5 | one Core implementation each (Wave 0-B), copies deleted in Wave A |
| Two systems for one job: player vs NPC animation; 6 PlayableGraph builders; 2 traversal leases; 4 seat-pose systems; 3 FP weapon motion units; Brain vs Idle movement drivers; 3 route planners + dead main-thread builder; 5 frame-budget mechanisms; Patrols own persistence beside SaveBus; 4 far-object cleanups; FS1 vs PollSleep FSM sleep; 2 ManualUpdate owners; HUD VanillaHits vs NPC VanillaEvents vs Guns Tracers hit observation; Towns routing on top of NPC nav; 3 camera projection writers | inventories | unify in Wave B (owner listed in section 6) |

## 8. Performance work list (highest expected value first)
| # | hot spot | fix | owner |
|---|---|---|---|
| 1 | Harmony hook cost invisible; hooks on every FSM event/state with string compares (SaveCombat, SaveRuntime, InteriorPopulation, Weather ProcessEvent, NativePoiPopulation EnterState, Towns Wait, Crawling.Active in every input/FSM-event hook) | per-handler timing (Debug); cached `FsmEvent`/`FsmState` reference compares; length gates | Core, Saves, NPCAI, POI, Survival, Presentation |
| 2 | Senses O(N^2) NPC-vs-NPC raycasts | spatial hash, per-tick raycast budget | NPCAI |
| 3 | Every NPC character animated every frame, culling forced off, no distance tiers; per-frame Dictionary alloc; full-object clone per attach | animation LOD tiers, cached rig, no per-frame alloc | Presentation |
| 4 | WeatherHazards every FixedUpdate: huge OverlapBox + 5 km raycast per column, always enabled | gate + cache | Survival |
| 5 | Scene scans during play: LightPolicy (10 s), SunShadows (5 s), Bosses (60 s), `FindGameObjectsWithTag("Hide")` (10 s), POI catalogs `FindObjectsOfTypeAll`, Props.Build repeats, first-hit/ammo-drop prefab scans, merchant bus setup, motorcycle prefab lookup | WorldIndex/PrefabCatalog/FsmIndex warmed at load, incremental updates | Core + owners |
| 6 | Synchronous heavy work mid-play: TownAssets.Load, BoltSurface skinned bake, PoseClip avatar build on first ladder, Anims first Get, LoadingScreen image decode per save, TrafficFoot main-thread route builds | sliced at load or worker thread | POI, Gunplay, Presentation, HUD, Patrols |
| 7 | Reflection/boxing on hot paths: PickAssist, WeaponsPackShadows, Car.cs:288, PatrolsBridge, PostFx, ApocasetterBridge.InputBlocked (Expression.Compile interpreted, read every frame), EngineSim `GetComponent` per frame + Expression delegates, Vistas prefix on every BepInEx log call | Core Access delegates, caches, narrower patch | Core, Presentation, Compatibility |
| 8 | Per-car linear journey search per driving decision; Pilot -> Towns provider call per decision | dictionary; cached town geometry | Patrols |
| 9 | Allocation in log calls: Patrols 156, NPC Senses/Idle/Pounce, Towns TownLoading | level guards | all |
| 10 | Duplicate per-frame work: eye ray cast by ThirdPerson + Tracers + MousePick; camera-holder skin scans twice (Game 1 s, Arms 3 s); `RenderScan.AfterCreateObject` dispatched twice; WorldHistory per tile move allocations | share one result per frame | Core, Presentation |
| 11 | Patrols lambda jobs hide cost; `WorldTraffic.Tick` hides 4 sub-systems; double-gated Quests jobs; `Hire.Recoil` on `Application.onBeforeRender` outside the scheduler | named, split jobs | Patrols, Quests |
| 12 | Mono.Cecil key scan includes WildWasteland.dll; GameRefs 7 `GameObject.Find`/s while a ref is missing in menus; Origin `Find("MapMagic")`/s in menus | Hotkeys catalog; per-ref retry; only InPlay | HUD, Core |

Perf acceptance: Denis runs the Debug A/B capture (scenarios in verification/README.md) on 1.0.9 and on the refactor build; the refactor must not be slower on any scenario (avg and p99), and the total of "WW managed ms/frame" must be lower. Record in `docs/perf-log.md`.

## 9. Bugs found during the inventory (fix in the owning package)
| bug | evidence | owner |
|---|---|---|
| `Hooks.RemoveAll` never removes `FsmEnable` handlers: stopped modules keep handlers, each off->on adds another copy (Towns, Weather, Patrols, Performance) | Hooks.cs:235-243 | W0-A |
| `Scheduler.RemoveAll` does not set `Job.Removed`: after Performance off->on, FS2/FS3 compile pump and PM5 proxy guard never come back | Scheduler.cs:152-159; CompileQueue.cs:36; GuiLoopFeature.cs:83 | W0-A |
| Any `Log.Error` during a save transaction aborts the save (incl. autosaves) | Log.cs:23-24 | W0-A + Saves |
| `Services.ClearEvents` never called | Services.cs:81 | W0-A (ModEvents owner cleanup) |
| `PoiNav.cs:31` resets LadderSpeed 1 -> 2 on every start, overriding the user's value | npc.md 9 | NPCAI-Nav |
| Weather disables native `SandPlayer` FSM; NPC Storm treats that as parked -> NPC storm sight/hearing off while Weather runs (needs in-game check) | world.md 5 | Survival + NPCAI |
| `QuietFsm.On` (Core fix) is bound by Patrols settings | PatrolsSettings.cs:120 | W0-A / Debug |
| Core `SaveFiles` hard-codes module sidecar folders; Towns ES3 special case in SaveSystem | SaveFiles.cs:183-190, SaveSystem.cs:264 | Saves |
| `Convoy.AutosaveSoon` checks the retired Apocasaver by reflection, not WW autosave | patrols.md 5 | Patrols |
| docs/harmony.md stale (GetMouseButtonDown dispatcher, Towns Senses patches that do not exist) | quests-compat.md 10b | Wave C |

## 10. Compatibility rules (must hold after every wave)
### 10.1 Saves
- ES3 keys unchanged: `Apocapatrol.Patrol` (schema 14 + WDR1/WDR2), `Apocaunloader.Magazines` (`|1`), `ApocalypterQuests` (AQ1), `WildWasteland.Towns.World.v1`, `.GasStations.v1`, `.PoiDressMarkers.v1`, `WildWasteland.SaveCommit`, `WildWasteland.CleanupProtected`, Apocasaver hinge/pose keys.
- Sidecar paths unchanged (StorageId): `config/WildWasteland/{NPC,Patrols,POI,CleanHUD,Quests,Towns}/Saves/...` + legacy dirs.
- Prefab ids unchanged and still resolved through the GetPrefab dispatcher: 735710202610050001 (motorcycle), 735710202610090001 (War Rig), 7392481500621300201 (stew pot).
- Saved component types: every moved saved type gets a SaveTypes alias from `WildWasteland.Modules.<Old>.<Type>, WildWasteland` and the old standalone names keep mapping (27 Patrols types, Motorcycles.MotorcycleBalance, WarRig.WarRigUnit; `PatrolVisibility` stays as an empty legacy type).
- Saved GameObject names unchanged (`Register.Name`, `Gungirl(Clone)N` identity, native names).
- A save made with the refactor build loads with 1.0.9 for objects the old build knows (no new required keys).
### 10.2 Config
- P1: legacy sections bound verbatim; only `Enabled` switches of absorbed modules migrate (`[CleanHUD] Enabled` -> HUD feature, `[Weather] Enabled` -> Survival feature, `[Towns] Enabled` -> POI feature, `[Performance] Enabled` -> Optimization master, `[EngineSimCompat] Enabled` -> Compatibility/EngineSim). Never purge orphans (AGENTS 5.7).
- Hotkey entries in `[Keys]` keep their names; editors moving to Developer keep their key names.
- `tools/config-harness` must pass at the end of every wave (Claude cloud session) - Wave C adds the new modules to it.
### 10.3 Assets/resources
- Embedded resource logical names (`<Id>.<path>`) and the RuntimePath remaps in csproj/build.sh are untouched until Wave C; Wave C moves `Embedded/` folders only together with a resource-name alias table in `DiskResources`/`Assets` so old names keep resolving.

## 11. Conventions for all agents
- Every method, constructor, operator, indexer and non-trivial property accessor has a `///` summary (one or two sentences: what and why; for hooks also: which game method, how often it runs, its early-out filter). Classes and files have a header summary saying what the unit owns. Checked by `tools/refactor/check_comments.py` for files the package owns.
- One responsibility per file; partial classes allowed for large types; file name = main type name.
- Logging only via `Log` with level guards (section 5.1).
- Jobs: `Scheduler.*(this, "<Id>.<Name>", NamedMethod, ...)` - no lambdas.
- Hooks: shared targets only through Core dispatchers; first line of every handler is a cheap filter (AGENTS 5.6).
- No cross-module references (section 3.2). Use `[Obsolete("refactor: use ModApi I...")]` on old public members still referenced by other modules instead of deleting them in Wave B; Wave C deletes them.
- Never change save keys, saved names, sidecar formats, prefab ids (section 10).
- Performance rules AGENTS section 6 apply to every touched line.
- Each package writes `docs/refactor/reports/<package>.md`: what moved, what was deleted (with evidence), contracts implemented/consumed, behaviour changes (should be none unless listed here), test steps for Denis, open questions.

## 12. Phases (details in `work-packages.md`)
| wave | agents | goal | gate |
|---|---|---|---|
| W0 | 2 parallel (W0-A kernel, W0-B utilities) | Core kernel, all ModAPI contracts/events as compile-ready stubs, new module skeletons, lint tools, utilities | build 0 errors; lints run; no behaviour change |
| A | 8 parallel (by SOURCE module) | Physical decomposition: every file/part moved into its target module folder (`src/Modules/<Target>/_from<Source>/`), old namespaces kept, old entries split into partials, duplicate utilities replaced by Core ones, dead code deleted | build 0 errors; `filemap_coverage.py` green; Denis smoke test (behaviour identical) |
| B | 18 parallel (by TARGET module/sub-area) | Unification: new module entries own lifecycle, ModAPI providers/consumers, dedupe systems, perf fixes, logging levels, comments | build 0 errors; `check_module_refs.py` green for the package; `check_comments.py` green; report written |
| C | 1 | Integration: delete `[Obsolete]` members and empty old modules, namespace finalisation + SaveTypes aliases, resources, config harness, docs (modules.md, harmony.md, AGENTS.md section 5 + history split), whole-repo lints | all lints green repo-wide; config harness passes; build 0 errors |
| D | Denis (+ fix agents per module) | In-game verification A-C + scenario list + perf A/B | not slower; no save/config loss |

Baseline at HEAD 2e98d0b (measured with the tools on the snapshot): `check_module_refs.py` 106 flagged sites (heuristic; the manual sweep in inventory/quests-compat.md 10a counts ~190 reference sites incl. reflection), `check_comments.py` 7,411 members without a `///` summary in 476 files, 482 source files mapped with 0 unmapped. Target after Wave C: 0 / 0 / 0.

The two scanners are regex heuristics, not compilers: a false positive in `check_module_refs.py` is silenced with `// refs-ok: <reason>` on the line (reviewed in Wave C); `check_comments.py` needs each member on its own line, which the "readable code" goal requires anyway.

## 13. Risks
| risk | mitigation |
|---|---|
| Presentation unification (player + NPC driver) changes the look of the player body | Wave B Presentation-Character keeps both paths behind a temporary `[Presentation / Debug] Legacy player driver` switch until Denis accepts; delete in a follow-up |
| Saved type renames break old saves | SaveTypes same-assembly aliases added in W0, alias test list in Wave C report; load an old save before/after (Denis) |
| Parallel branches collide | strict ownership (`ownership.json` + `check_ownership.py`), partial classes for shared entry types, contracts frozen after W0 (changes only through `docs/refactor/requests/<package>.md`, applied by the coordinator between waves) |
| Hidden behaviour coupling through Harmony order | Hooks dispatchers keep subscriber order = module registration order; registration order documented in ModuleRegistry; harmony.md regenerated in Wave C from `Patches.Records` |
| Context size for agents | each brief lists exactly the inventories/sections to read; AGENTS.md history split in Wave C (earlier: briefs tell agents to read AGENTS.md sections 3, 5, 6, 7 only) |
