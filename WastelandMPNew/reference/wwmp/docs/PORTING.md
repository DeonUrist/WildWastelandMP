# Porting a source mod into Wild Wasteland

Read `AGENTS.md` first (target architecture). This file is the concrete recipe used for the first combined release (0.1.0). Core is in `src/Core` and is **read-only for module ports** (if Core lacks something, work around it inside the module and list the request in your port report).

## 0. Modules and folders

| Module id (folder) | Namespace | Class | Source | Replaces GUIDs (`ConflictsWith`) |
|---|---|---|---|---|
| `PlayerBody` | `WildWasteland.Modules.PlayerBody` | `PlayerBodyModule` | Apocaplayer 2.3.9 | `com.denis.apocalypter.apocaplayer`, `com.denis.apocalypter.apocaclimber` |
| `NPC` | `WildWasteland.Modules.NPC` | `NpcModule` | NPC Overhaul (npcai) 1.9.15 | `com.denis.apocalypter.npcai` |
| `Guns` | `WildWasteland.Modules.Guns` | `GunsModule` | Gunplay 1.3.0 | `com.denis.apocalypter.gunplay`, `com.denis.apocalypter.apocaunloader` |
| `HUD` | `WildWasteland.Modules.HUD` | `HudModule` | Gunplay HUD 1.2.2 | `com.denis.apocalypter.gunplayhud` |
| `Patrols` | `WildWasteland.Modules.Patrols` (+ `.Motorcycles`, `.WarRig`) | `PatrolsModule` | Apocapatrol 2.14.4 + War Rig 1.1.3 (+ debug MerchantSpotter 1.0.2) | `com.denis.apocalypter.apocapatrol`, `com.denis.apocalypter.warrig`, `com.denis.apocalypter.merchantspotter` |

- **One namespace per module** (sub-namespaces only where listed). `Core.SaveTypes` maps old saved component types (`Apocapatrol.X, Apocapatrol` -> `WildWasteland.Modules.Patrols.X`) by pure prefix rename, so **keep every class name** (and nested class names) of types that can end up on saved objects. Do not rename classes in general.
- Subfolders inside the module folder group the logical frameworks (e.g. `NPC/Detection`, `NPC/Navigation`, `NPC/Brain`); folders do not change the namespace.
- Embedded resources: put files under `src/Modules/<Id>/Embedded/...`; logical name = `<Id>.<path with / -> .>` (see build.sh). Update the string lookups.

## 1. Entry point

Replace the `BaseUnityPlugin` with `public sealed class <X>Module : ModuleBase`:
- `Id`, `DisplayName`, `ConflictsWith`, `DefaultEnabled` (true).
- `BindConfig(ModuleConfig cfg)`: bind ALL entries (see 3). Called even when the module is off/conflicting, before any Init.
- `Init()`: everything the old `Awake` did except config binding and the runner: install patches (2), register jobs (4), hotkeys (5), services (6), load assets (7). Must be **re-runnable after Shutdown** (runtime off -> on from F6): reset statics at the start of Init or in Shutdown.
- `Shutdown()`: undo game-state side effects (restore materials/renderers/camera, destroy per-object components the module added, stop coroutines, destroy created GameObjects/textures if they would leak or keep running), set the service field it provides back to null. Core removes patches, jobs, hooks and hotkeys after Shutdown returns.
- `OnSceneLoaded`, `OnWorldReady`, `OnSaveBegin/End`, `OnLoadBegin`, `OnOriginShift(delta)`: map the old handlers (old `SceneManager.sceneLoaded +=` lambdas become `OnSceneLoaded`).
- Keep a static `internal static <X>Module Instance` if the old code used `Plugin.X` statics; replace `Plugin.Log.LogInfo(...)` with `Core.Log.Info(...)` (gated by verbose), `LogWarning` -> `Core.Log.Warn`, `LogError` -> `Core.Log.Error`. Keep log messages but prefix them with `[<Id>] ` where cheap.
- `Info.Location` / `typeof(X).Assembly.Location` / `Assembly.GetExecutingAssembly().Location` as the data folder -> `Dir` (= `BepInEx\plugins\WildWasteland\<Id>\`). Theme for UI is shared in Core (`Core.UI.Skin.Dir`).
- Sidecar/data folders under `BepInEx\config\<OldName>\...` -> `Core.Assets.ModuleConfigDir(Id)`; **read the old location as fallback** (first run migration: if the new file is missing and the old one exists, copy it). User data files that lived next to the old DLL (e.g. `PatrolsAndLoot.json`, `MerchantLayouts.json`, `BusPassengerPose.json`, `PlayerTemplates/`) -> same rule with `Core.Assets.LegacyPluginDir("<OldFolder>")`.
- **Save data keys/formats stay verbatim** (ES3 key names, binary schemas) so existing saves load.

## 2. Harmony

- Never `new Harmony(...)`, never `PatchAll`. Use `Core.Patches.Patch(this, original, prefix: mi, postfix: mi, finalizer: mi)` or `Core.Patches.ApplyClass(this, typeof(SomePatchClass))` for `[HarmonyPatch]` classes.
- **Shared targets go through `Core.Hooks`** (subscribe in Init; Core patches lazily and removes your handlers on Shutdown):

| Target | Hook | Handler signature |
|---|---|---|
| `CreateObject.OnEnter` | `Hooks.CreateObjectEnter` | `Prefix(this, a => bool)` / `Postfix(this, a => {})` |
| `SendEvent.OnEnter` / `.OnUpdate` | `Hooks.SendEventEnter` / `SendEventUpdate` | prefix (return false = skip original; you must call `Finish()` yourself when the original would have) |
| `SetFsmFloat.OnEnter` / `.DoSetFsmFloat` | `Hooks.SetFsmFloatEnter` (prefix+postfix) / `SetFsmFloatDo` (prefix) | |
| `SetVelocity.DoSetVelocity` | `Hooks.SetVelocityDo` | prefix |
| `GetButtonDown.OnUpdate`, `GetButton.DoGetButton`, `GetButtonUp.OnUpdate`, `GetMouseButtonDown.OnUpdate` | `Hooks.GetButtonDownUpdate`, `GetButtonDo`, `GetButtonUpUpdate`, `GetMouseButtonDownUpdate` | prefix |
| `Raycast.OnEnter` / `.DoRaycast` | `Hooks.RaycastEnter` (prefix+postfix) / `RaycastDo` | |
| `SmoothLookAt.DoSmoothLookAt` | `Hooks.SmoothLookAtDo` | prefix |
| `ActivateGameObject.OnEnter` / `.DoActivateGameObject` | `Hooks.ActivateGameObjectEnter` (prefix+postfix) / `ActivateGameObjectDo` | |
| `SetPosition.DoSetPosition` | `Hooks.SetPositionDo` (prefix+postfix) | |
| `SensorGetDetectionRayHit.OnEnter` | `Hooks.SensorRayHitEnter` | prefix + `Finalizer(this, (a, ex) => ex)` |
| `SensorGetLineOfSightResult.OnEnter3D` / `OnUpdate3D` | `Hooks.LosEnter3D` / `LosUpdate3D` | prefix |
| `SensorGetDetections.DoAction` | `Hooks.SensorDetectionsDo` | prefix |
| `Fsm.ProcessEvent(FsmEvent, FsmEventData)` | `Hooks.ProcessEvent.Prefix(this, (fsm, ev, data) => bool)` | |
| `Fsm.EnterState(FsmState)` | `Hooks.EnterState.Prefix/Postfix(this, (fsm, state) => ...)` | |
| `ES3ReferenceMgrBase.GetPrefab(long,bool)` | `Hooks.GetPrefab.Resolver(this, id => ES3Prefab or null)` | |
| `NWH FloatingOrigin.Jump` | use `IModule.OnOriginShift(delta)`; raw access `Hooks.OriginJump` | |

  Handlers receive the action instance (`__instance`); read private fields with cached `AccessTools.FieldRefAccess` or `Traverse` set up once in Init, never per call by name. HarmonyX semantics are kept: all prefixes run, the original is skipped when any returned false.
- Handler first line = cheap filter (static bool, reference compare to a cached FSM/owner, dictionary lookup). No string building, no LINQ, no allocations in the filter path.
- Targets only your module uses: patch them as a module patch (above). They appear in the F7 "Harmony patches" page automatically.
- Patches against other absorbed mods (`Apocapatrol.Patrol.BailOut`, `Apocaunloader.Runner.Update`, `Apocapatrol.MeleeWheels.RaiderHandles`, `ConfigFile.Save` for settings layout...) are deleted and replaced by Core services/events (6) or dropped.

## 3. Config (F6 vs F7)

- `cfg.Enabled` (module master switch) is bound by Core as the first entry of section `<Id>`.
- `cfg.Player(sub, key, default, desc[, AcceptableValueRange/List])` -> main cfg (Apocasetter / **F6**), section `<Id>` or `<Id> / <sub>`. Only player-facing gameplay settings and **one on/off toggle per feature** (bind toggles first, in feature order). Target: <= ~25 F6 entries per module.
- `cfg.Tool(sub, key, default, desc[, range])` -> `BepInEx\config\WildWasteland\tools.cfg` (**F7**): tuning constants, offsets, poses, debug switches, editors' toggles, developer options, hidden settings of the old mods (they used never-saved files; now they are editable in F7).
- `cfg.Hotkey(key, Key.X, desc)` -> main cfg `[Keys]` (rebindable in F6). Prefer `Key.None` defaults for debug/editor keys (they are reachable from F7 buttons); keep old defaults for gameplay keys.
- Migration: for every entry that existed in the old cfg, call `cfg.Migrate(entry, "<old cfg file name>", "<old section>", "<old key>")` right after binding (it only applies on the first WW run and logs). The old cfg files are in `/home/claude/ww/game/BepInEx/config/` for reference (Denis's real values).
- Use `SettingChanged` for anything that must react live (the F6 and F7 UIs write entries live).
- Drop the old mods' own config re-ordering/purging hacks (`SettingsLayout`, `PurgeStaleEntries`, `OrphanedEntries` purges on the shared file) - they would damage other modules' entries. Never call `Config.Clear()` / remove orphans on the shared files.

## 4. Update loops -> scheduler

- No MonoBehaviour on the Runner with its own `Update/LateUpdate/FixedUpdate/OnGUI`. Two allowed patterns:
  1. Static/plain class: `Scheduler.EveryFrame(this, "Name", Tick, Phase.X)`, `Scheduler.Interval(this, "Name", 0.5f, Tick)`, `Scheduler.Sliced(this, "Name", 1000, Step)`, `Scheduler.Gui(this, "Name", Draw)`; coroutines via `Scheduler.StartCoroutine`.
  2. Old runner MonoBehaviour that needs `StartCoroutine`/`GetComponent<>()` sibling access: keep it as a component on a module-owned child GameObject (`new GameObject("WW.<Id>")` parented to `Core.Runner.Host`, destroyed in Shutdown), **rename** `Update`->`Tick`, `LateUpdate`->`LateTick`, `FixedUpdate`->`FixedTick`, `OnGUI`->`Gui` (non-Unity names!) and register them as jobs. Preserve the old relative order of the old runner components (register in that order).
- Per-frame work that was interval-gated inside Update (e.g. `if (Time.time < _next) return;`) should become `Interval` jobs when trivial; otherwise leave the gate.
- Phases: `EarlyUpdate` (= old `[DefaultExecutionOrder(-1000)]` runners), `Update`, `LateUpdate`, `LateUpdateLast` (after everything), `FixedUpdate`, `EarlyFixedUpdate`, `Gui`.
- Per-object components (per NPC/car/turret) may keep their Unity loops (AGENTS 5.2 exception); keep `[DefaultExecutionOrder]` and `[ES3NonSerializable]` attributes. They must stop/destroy themselves when the module shuts down (check a static `Active` flag or destroy them in Shutdown).
- Apply the cheap perf fixes listed in your notes file section 10 when they are local and safe (cache `Camera.main` via `Core.GameRefs.MainCamera`, replace per-frame `GameObject.Find` with `Core.GameRefs.Player` etc., remove per-frame LINQ/allocations). **Do not change behaviour** to gain performance; preserving features is paramount.

## 5. Hotkeys / input

- `Core.Hotkeys.Register(this, entry, action, when)` instead of polling `Keyboard.current[...]` in Update for top-level hotkeys. Editor-internal keys (numpad nudges while an editor is active) can keep direct polling inside the editor's tick.
- F7 opens the tool window; Core owns that key. Old debug toggle keys (e.g. Insert for the bus pose editor) become `Key.None` by default plus an F7 button/toggle.

## 6. Cross-module services (src/Core/Services.cs)

- Provider sets `Core.Services.<X> = new <Adapter>()` in Init and `= null` in Shutdown. Consumers check `Services.X != null` at every call site (or cache per tick) and fall back exactly like the old "provider mod not installed" path.
- PlayerBody provides `Services.Animation` (`IAnimationApi`/`IAnimatedCharacter` mirror `Apocaplayer.ModAPI`/`ModAPI.Character`). NPC and Patrols consume it instead of `Apocaplayer.ModAPI` + `Chainloader` GUID checks + `Rider.Clip` reflection (`GetClip`).
- NPC provides `Services.Npc` (`INpcApi` mirrors `NPCAI.Api` + `Structures(list)` replacing `NPCAI.Nav._structures` reflection). Guns and Patrols consume it.
- Guns provides `Services.Guns` (`IGunsApi` mirrors `Gunplay.Api`, plus `HandlesFittedWheelMelee`). NPC consumes it (WeaponRanges, VanillaEvents), Patrols consumes `HandlesFittedWheelMelee` (MeleeWheels stands down when true; keep the Apocaraider probe as well).
- HUD provides `Services.Hud` (`IHitFeedback`). Guns calls it (was `GunplayHUD.Api` by reflection) and sets `Services.Hud.ExternalReporter = ProjectilesEnabled` so the HUD's vanilla observer stays quiet while Guns reports hits.
- Patrols raises `Services.RaiseCrewBailedOut(car, pax, npc)` at the end of `Patrol.BailOut` (npc = its return value) and `Services.RaiseVehicleExploded(car)` in `Explode.Blast`; NPC subscribes (`Services.CrewBailedOut += ...` in Init, `-=` in Shutdown) instead of patching Apocapatrol.
- Third-party mods (Apocasetter, Apocasaver, Apocapocket, DogCompanion, ApocaDustStorm, MotorcycleMod, PartAdjustment/Ultimate Tools, Apocaspawner, Apocaraider) keep their reflection bridges, one file each, as before. Apocasetter: use `Core.ApocasetterBridge` (InputBlocker, GameFont) instead of own reflection.
- Modal editors use `Core.UI.EditorSession` (one owner for the whole mod, shared with the F7 window). IMGUI skin: `Core.UI.Skin` (= PatrolSkin) and `Core.UI.Ledger` / `Core.UI.LedgerDropdown` (= LedgerSkin/LedgerDropdown, `Ledger.Header(width, "TITLE")`).

## 7. F7 tool pages

Return pages from `ToolPages()` (called once when F7 first opens). Use `Core.UI.ConfigPage` for lists of tool settings (match sections by exact name or prefix `"Patrols / Pose*"`) and `Core.UI.ActionPage` / a `ToolPage` subclass for buttons and live tools (`ToolLayout`: `Heading, Text, Value, Button, Buttons, Toggle(entry), Slider, Entry(any ConfigEntryBase)`). Groups: `Core.UI.ToolGroups` (Player body & animation, NPC AI & navigation, Weapons & HUD, Patrols & vehicles, Seat & turret poses, Merchants, Debug & performance). A page that opens another modal editor calls `ToolWindow.CloseThen(() => editor.Open())`. Live numpad editors (turret spots, bus pose, merchant objects, weapon poses...) get a page with: on/off toggle, the key list, the current selection, and SAVE/RESET buttons where the old editor had keys for them; the editor itself keeps running in-world after F7 closes.

## 8. Port report and wiki

When done, write:
1. `/home/claude/ww/notes/port-<Id>.md`: source commit, file mapping (old file -> new folder), Harmony table rows (target | kind | hook or module patch | filter | frequency) for docs/harmony.md, F6 entries list, F7 pages list, migration list (cfg keys, data files, save keys), behaviour changes (should be none; list any), perf changes, known gaps/risks, Core requests.
2. Wiki pages in `/home/claude/ww/wiki/Modules/<Id>/` (format in `docs/WIKI-FORMAT.md`).

## 9. Build

(Claude cloud sessions; on Denis's PC use `dotnet build`, AGENTS.md 7.1.) `MANAGED=/home/claude/ww/game/Apocalypter_Data/Managed BEPCORE=/home/claude/ww/game/BepInEx/core sh tools/build-module.sh <Id>` compiles Core + your module (others stubbed). mono mcs 6.8, `-langversion:latest` (= C# 7.x): no `out var`/pattern variables inside iterator methods, no local functions, no locals shadowing a lambda parameter, no tuples/`default` literal/`in` params; `?.`, `=>`, `nameof`, string interpolation, `out var` outside iterators are fine. Must build with zero errors.
