# Harmony patch inventory (0.1.0)

Sleepers/explicit respawn (2026-10-19, Testing): MultiplayerBridge adds PlayerVitals.Capture postfixes (sleeper model + explicit anchor/camp metadata), NetSession.Stop prefix/postfix (sleeper close guard/cleanup), Survival.EnterDowned postfix (unlimited deadline), CanBeDowned prefix (native enable retained, no teammate-radius exclusion), OnBedConfirmed prefix/postfix (preserve explicit anchor around ordinary sleep), PlayerVitals.ApplyBed postfix (restore explicit selection), Survival.OnGUI transpiler (remove countdown format), generated DieAndRespawn.MoveNext transpiler (replace exactly one SafeTeleport.To call + final native notices), and NetSession.TryRestorePlayer transpiler (replace exactly one placement call). Existing Recovery PlayerRegistry.GetOrCreate/WorldObjects.OnPeerLeft hooks perform wake/sleep lifecycle; CaptureLines updates moved body positions before snapshot. Core GetDistance.DoGetDistance prefix guards native distance reset/deletion/deactivation for selected item/parent vehicle; DestroyObject.OnEnter prefix guards only DeleteFarAwayItems/DeleteFallingItems. Existing shared GetButtonDownUpdate dispatcher consumes native Use_2 on selected bed/seat press. New ISleepingBodies config-lifetime provider; existing Camera.onPreRender billboarding includes sleepers. Host/epoch/peer-validated packets 159–164, handshake 16; physics and display use Core scheduler, no new OnGUI component. Compile passed, runtime installation/gameplay pending; see compat/ApocalypterMP/SLEEPERS-RESPAWN.md.

Disconnect checkpoint-only follow-up (2026-10-18, Testing): MultiplayerSaving adds a MultiplayerBridge-owned transpiler on native `NetSession.OnPeerDisconnected(ulong)`, replacing exactly one parameterless `WorldSaver.Request()` call with NOP while preserving labels and native OnPeerLeft/recovery checkpoint/cleanup/broadcast. Signature/call-count mismatch fails installation. Existing Chat.Notice prefix normalizes native join/leave suffixes to connected/disconnected. No per-frame work or external DLL changes. Windows compilation passed; runtime patch installation remains Denis's verification. See compat/ApocalypterMP/PLAYER-RECOVERY.md.

MP player HUD (2026-10-18, Testing/source only): Core MultiplayerNames adds `ApocalypterMP.Game.RemotePlayer.Update()` postfix (cache native TextMesh/peer; live FloatingNames visibility) and `RemotePlayer.Destroy()` prefix (remove cached entry). No scene scans; camera alignment uses onPreRender after PreCull overrides. This is distinct from PlayerBody's native `Game.Avatar.PlayerBody.Destroy` postfix. Existing MultiplayerHandshake OnHello/transport Send hooks now exchange strict loaded-plugin GUID/version/MVID manifests, handshake 15; no additional handshake target. All hooks retain MultiplayerBridge ownership. No build/deploy under standing deferral; runtime/compilation pending. See compat/ApocalypterMP/PLAYER-HUD-AND-MOD-CHECK.md.

Guest passenger exit (2026-10-18, Testing/source only): MultiplayerBridge adds a prefix to native `ApocalypterMP.Net.WorldObjects.PassengerGetOut()` through MultiplayerVehicles.BeforeExit. Early-out: guest loaded world/epoch readiness, actual passenger-seat parenting and alive/non-downed local player. Native candidate/mask use cached delegates; support/free-capsule and finished MapMagic main terrain gate an ordinary guest exit. A pending-only 0.25 s scheduler retry retains native placement/OnFoot actions once safe. No terrain reset, diagnostic logging or external DLL change. Compilation/runtime unverified under explicit build deferral; see compat/ApocalypterMP/PASSENGER-GROUND.md.

MP logging (2026-10-18, Testing): Core MultiplayerLogging prefixes `BepInEx.Logging.ManualLogSource.Log(LogLevel, object)` under the Core owner, like the existing Vistas source filter. Only the exact native ApocalypterMP Plugin.Logger instance is affected: routine Info/Debug/Message follows WW VerboseLog; warning/error/fatal survives with English text. The two Core prefixes have disjoint source guards; no module-owned duplicate sink prefix is added. Translation uses anchored templates on log text only, preserving captured values. Native pre-bridge startup lines remain explicitly accepted. No gameplay or external DLL change. See compat/ApocalypterMP/LOGGING-MENU.md.

Player recovery (WildWastelandMP, 2026-10-18, Testing): MultiplayerBridge adds `PlayerRegistry.Open` postfix / `Close` prefix / `GetOrCreate` postfix; `PlayerVitals.Capture` and `Apply` postfixes (alongside existing Party receipts); `WorldObjects.OnPeerLeft` postfix; `NetSession.TryRestorePlayer` prefix/postfix (alongside Party) and `AnnounceClosing` prefix; `Worlds.QuitPatches.Intercept` prefix for client final reporting. Cold lifecycle targets or five-second player reports; active-role guards apply. `Application.wantsToQuit` is a lifecycle event, not a Harmony target. Existing WorldSaver.Save/SaveGame gates now prepare the embedded player snapshot before native disk serialization; SaveBus observes actual serialization. One 0.25-second scheduler job gates five-minute player-only checkpoints and deferred notices; worker threads receive only immutable strings. No new scene scan, module dependency or native full-save invocation from checkpointing. See compat/ApocalypterMP/PLAYER-RECOVERY.md; revision 7.

Multiplayer Name field (2026-10-18, Testing): MultiplayerBridge adds a postfix on native `NetSession.LocalPlayerName()` to use settings.cfg `[Multiplayer] Name` when nonempty. Empty retains native Steam/UDP naming or MultiplayerLaunch's existing Host/Client prefix result. Connection handshakes, chat and save-request names keep their native consumers; LocalKey/save identity is unchanged. No external DLL edit or protocol change.

WildWastelandMP quit packet flood (Testing): `MultiplayerTransport.Packet` prefixes native `Net.UdpTransport.HandlePacket` under the existing MP patch owner. It rejects disposed transports and missing endpoints/null or empty payloads before native peer lookup; complete packets retain the native path. This covers both direct and delayed dispatch without catching unrelated exceptions. The existing Dispose/Update/SendTo lifecycle hooks remain in place.

Issue #3 (WildWastelandMP, Testing): MultiplayerBridge's existing `Diagnostics.DebugKeys.Update` prefix now suppresses all MP F5–F12 actions. A new `NetSession.get_Status` prefix replaces only the offline F5/F6/F7 hint with the main/ESC menu entry point; active-session status remains native. CleanHUD HOST/JOIN/DISCONNECT calls reuse native HostWorld/StartUdpJoin/End/AnnounceClosing through cached delegates; no native input, transport or save target is added.

The non-live ApocalypterMP source overlay adds a separate [compatibility patch inventory and checkpoint](../compat/ApocalypterMP/STEP-13-ITERATION-HANDOVER.md). Its optional MP hooks are not deployed or gameplay-verified. QuestsCompat remains unregistered; native Quests owns the optional Talk hooks below.

Every patched method appears here exactly once. Rules (AGENTS.md 5.6): a target shared by two modules is patched **once** by a Core dispatcher in `src/Core/Hooks.cs` (Harmony id = the plugin GUID) and the modules subscribe handlers; a target only one module uses is a **module patch** applied through `Core.Patches` with the module's own Harmony id `com.denis.apocalypter.wildwasteland.<Id>` (unpatched when the module is switched off). `Core.Patches` logs `CONFLICT` when two modules patch the same target+kind directly. The live inventory is on the F7 page **Debug & performance -> Harmony patches** and in the startup summary (`core hooks: <target>[prefixes/postfixes] ...`; `(NOT PATCHED)` marks a dispatcher that failed to apply).

Dispatcher semantics = HarmonyX with separate plugins: every prefix handler runs (in module init order PlayerBody, NPC, Guns, HUD, Patrols), the original is skipped when any returned false, postfix and finalizer handlers always run. A dispatcher is only patched when at least one running module subscribed (`Hooks.ApplyUsed`), so a disabled feature costs nothing. All 50 string-named targets were checked against the game's IL (review 2026-10-13).

"Hot?" = how often the target runs; the early-out filter is what each handler checks **first** (static bool / reference compare / length gate) before any string work.

## 1. Core dispatchers (Hooks)

| Target (Type.Method) | Kind | Subscribers in order (module: handler) | Hot? | Early-out filter per handler | Known other patchers |
|---|---|---|---|---|---|
| `HutongGames.PlayMaker.Actions.CreateObject.OnEnter` | prefix | Core: `MultiplayerHosting.BeforeStarterCar` (chosen native starter prefab and matching parts index); Guns: `Tracers.BeforeMeleeEffect` (metal melee sparks; skips + `Finish()`); Survival: `NaturalLoot.BeforeCreate` (25% natural stove rolls become empty pots; original creation continues) | per spawn | Core: pending new host world, explicit vehicle, Starter_Area/CarSpawn_StarterBase ItemSpawner/spawn only; Guns: `TracersEnabled && MetalSparks`, FSM "HitEffect", player melee; Survival: FSM "ItemSpawner", owner `ItemSpawn*`, shared Item input/output, exact stove prefab name/id | ApocalypterAI, Apocaspawner |
| same | postfix | PlayerBody: `Projectiles.AfterCreateObject`; NPC: `Senses.AfterCreateObject`, `Idle.AfterCreateObject`, `Pounce.AfterCreateObject`, `Gungirl.AfterCreateObject` (WoW); Guns: `GunsModule.AfterCreateObject` (-> `Bosses.Spawned`, `Tracers.OrbSpawned`, `Tracers.BoltCorpseSpawned`); Patrols: `TrafficCamps.SpawnHook.Postfix` | per spawn | PlayerBody: `LanceIgnoresOwnCar`, `Game.InCar`, then `Fsm.Name == "Attack"`; NPC: `!Running` (Pounce: `Airborne == 0`); WoW: `!WomenFeature.Active`, cached prefab name, Flexa/Sprokka or their corpses only; Guns: null result, `BossHealth` / `TracersEnabled && OrbsHitWalls`, boss name cached per prefab id; bolts: landed list nonempty, Health FSM, owner has attached bolt, created `_Dead` object; Patrols: `storeObject == null`, owner `EnemySpawn_` | |
| `SendEvent.OnEnter` | prefix | NPC: `Brain.BeforeSendEvent` (skips + Finish), `Aim.BeforeSendEvent`; Patrols: `NativeStormProtection.DamageEventEnter.Prefix` (skip + Finish), `Ram.BeforeSendEvent` (skip + Finish for own crew) | every SendEvent in the game | Brain: `_npcs.Count == 0 \|\| !On`; Aim: `!Running \|\| !AimEnabled` then event name "Activate" / FSM "RangedAttackWait"; storm: `WarRigUnit.All.Count == 0`; Ram: `!Running`, FSM "CarAttack", target shares car root | ApocalypterAI, standalone NPCAI |
| `SendEvent.OnUpdate` | prefix | Patrols: `NativeStormProtection.DamageEventUpdate.Prefix` | every everyFrame SendEvent | `WarRigUnit.All.Count == 0` | |
| `SetFsmFloat.OnEnter` | prefix (observe only, returns true) | HUD: `VanillaHits.BeforeSetFsmFloat` | every SetFsmFloat (many per frame) | static `VanillaHits.Observing` and `External && Services.Guns != null` -> return; then `Bodypart` / `Damage` / `Attack` / owner has `Reload` FSM | ApocalypterAI, standalone Gunplay / NPCAI |
| same | postfix | NPC: `VanillaEvents.AfterDamage`, `DogBite.AfterSetFsmFloat`; Guns: `Tracers.AfterSetFsmFloat` (loose-wheel melee); Patrols: `Ram.AfterSetFsmFloat` (any car's damage multiplier against creatures other than the player) | same | NPC: `!Senses.On` / `_list.Count == 0 \|\| !Running`; Guns: `MeleeOnWheels`, owner name length 6 + `"Attack"`, `IsPlayerMelee` cached per weapon id; Patrols: `!Running`, FSM name length 9 + `"CarAttack"`, `Bodypart.Damage`, excludes player and its children | |
| `SetFsmFloat.DoSetFsmFloat` | prefix (skip) | Patrols: `NativeStormProtection.DamageValue.Prefix`, `Ram.BeforeSetFsmFloat` | every SetFsmFloat | storm: `WarRigUnit.All.Count == 0` then `TornadoDamage` / `Damage`; Ram: `!Running`, FSM "CarAttack", target shares car root | |
| `SetVelocity.DoSetVelocity` | prefix (skip) | NPC: `Brain.BeforeSetVelocity` (registers NPCs via `Brain.Of`), `DogAnimations.BeforeSetVelocity` (only with the dog bundle) | per frame per ground NPC | `!On` (static) then FSM name "Movement" (needed for registration); dogs: `Dogs.Count == 0 \|\| !Running` | ApocalypterAI |
| `GetButtonDown.OnUpdate` | prefix (skip) | Core: `TakeItemUsePatch.Prefix` (HealthStacks top-up); PlayerBody: `ThirdPerson.BeforeGetButtonDown`; NPC: `Senses.BeforeGetButtonDown`; Guns: `GetButtonDown_Patch.Prefix` (Reload suppressed while unloading); Survival: `Runtime.DrinkInput` (held pot owns Use) | every GetButtonDown action every frame | Core: FSM name length 8, TakeItem/over/Use, Bandage_Item or FirstAid_Item, input unblocked/unpaused/focused; transfer only on Use press. PlayerBody: per-FSM classification cache (reference-keyed dictionary, no string compares after the first); NPC: `!Running`, modifier None, modifier held (one keyboard read per frame); Guns: `AmmoSettings.Enabled`, `buttonName != "Reload"`; Survival: cached UseDrink FSM reference, held registered pot | Apocasetter InputBlocker (skips the original while its window is open), PartAdjustment / Ultimate Tools, ApocalypterAI, HealthStacks (built-in handler not registered when standalone loaded) |
| `GetButton.DoGetButton` | prefix (skip, storeResult assigned) | NPC: `Senses.BeforeGetButton`; Guns: `Ironsights.BeforeGetButton` | every GetButton per frame | NPC: as above; Guns: cached player aiming FSM reference, ToggleIronsights, input unblocked and in play | Apocasetter InputBlocker |
| `GetButtonUp.OnUpdate` | prefix (skip) | NPC: `Senses.BeforeGetButtonUp` | every GetButtonUp per frame | as above | Apocasetter InputBlocker |
| `GetMouseButtonDown.OnUpdate` | prefix | (dispatcher defined, **no subscriber in 0.1.0 - never patched**) | - | - | Apocasetter InputBlocker |
| `Raycast.OnEnter` | prefix | NPC: `VanillaEvents.BeforePlayerShot` (observes, returns true); Guns: `Tracers.BeforeRaycast` (returns false + `Finish()` when it fires; visible PlayerBody muzzle in third person, native muzzle otherwise, converging on the eye-ray target) | every Raycast.OnEnter (everyFrame ones too) | NPC: `!Senses.On \|\| Guns projectiles on`, FSM "Attack"; Guns: `TracersEnabled && PlayerGuns`, FSM "Attack", `repeatInterval != 0`, `_notGun`/`_guns` dictionary by Fsm reference | ApocalypterAI, standalone Gunplay / NPCAI |
| `Raycast.DoRaycast` | prefix (skip) | NPC: `Brain.BeforeRaycast` | per frame per attacking NPC | `_npcs.Count == 0 \|\| !On`, FSM "Attack" | ApocalypterAI |
| `SmoothLookAt.DoSmoothLookAt` | prefix (skip) | PlayerBody: `FirstPersonSway.BeforeSmoothLookAt` (`[Sway]` under the PlayerCamera); NPC: `Brain.BeforeSmoothLookAt` (melee chasers) | every SmoothLookAt every frame | PlayerBody: per-action classification cache (sentinel for other FSMs), then `SmoothWeaponSway`; NPC: `_npcs.Count == 0 \|\| !On` | ApocalypterAI |
| `ActivateGameObject.OnEnter` | prefix (never skips), postfix | Guns: `MagazineDrawPatch.Prefix` -> `PerGunAmmo.Draw`; `WeaponTransition.AfterActivate` starts the raise pose | every ActivateGameObject.OnEnter | magazine: `Fsm.Name.Length == 12` + `"WeaponInHand"`, `activate.Value`, `PerGunMagazines`; transition: cached WeaponInHand FSM reference, SmoothWeaponSwitch, activation only | |
| same | postfix | NPC: `Brain.AfterActivate` | same | `_npcs.Count == 0 \|\| !Running`, dictionary lookup | |
| `ActivateGameObject.DoActivateGameObject` | prefix (skip) | Core: `ColorGrading.BeforeActivate`; PlayerBody: `ThirdPerson.BeforeActivateGameObject` | every ActivateGameObject | Core: length/name + native color_grading owner and PP color grading target, force that volume/option/toggle off; PlayerBody: static `ThirdPerson.AimZoom` | |
| `SetPosition.DoSetPosition` | postfix | Patrols: `Suspension.AfterSetPosition` | every SetPosition | static int `SuspensionTune.Live <= 0`, then FSM "Suspension", owner `hinge_wheel_` | |
| `Micosmo.SensorToolkit.PlayMaker.SensorGetDetectionRayHit.OnEnter` | prefix | NPC: `VanillaEvents.BeforeNpcShot` (observes); Guns: `Tracers.BeforeRayHit` (returns false + sends `isNotIntersectedEvent` when it fires; NPC crossbow origin is the cached native arrow tip, other guns retain fire_effect) | per NPC burst ray | NPC: `!Running`, FSM "Damage Ranged"; Guns: `TracersEnabled`, FSM "Damage Ranged", target null, `ShooterInfo.IsGun` cached per owner id | ApocalypterAI, standalone Gunplay / NPCAI |
| same | finalizer | NPC: `VanillaEvents.AfterNpcShot` (restores the lengthened RaySensor; module-side stack keyed by the action instance, runs even when Guns skipped the original) | same | `!Running` | |
| `SensorGetLineOfSightResult.OnEnter3D` | prefix (skip) | NPC: `Senses.BeforeLosResult`; Patrols: `SeatedLosGuard.Before` (zero outputs + `Finish()` when not everyFrame) | per burst check | NPC: `!On`, FSM "Attack"; Patrols: `targetObject.Value != null` -> vanilla | |
| `SensorGetLineOfSightResult.OnUpdate3D` | prefix (skip) | NPC: `Senses.BeforeLosResult`; Patrols: `SeatedLosGuard.Before` | every frame for everyFrame LOS actions | same | |
| `SensorGetDetections.DoAction` | prefix (NPC skips; Guns observes) | NPC: `Senses.BeforeGetDetections` (registers agents via `Senses.Of`); Guns: `Bosses.BeforeGetDetections` (registers human bosses once per owner) | per frame per NPC | NPC: `!On` (static) then FSM "Detection"; Guns: `!BossHealth`, FSM "Detection", owner instance-id cache | ApocalypterAI |
| `HutongGames.PlayMaker.Fsm.ProcessEvent(FsmEvent, FsmEventData)` | prefix | NPC: `Brain.BeforeDrivingHide` (filters driving retreat), `DogAnimations.BeforeDamageEvent` (only with the dog bundle; skips); Guns: `Fsm_Event_Patch.Prefix` -> `Unloader.OnFsmEvent` (never skips), `WeaponTransition.BeforeEvent` (delays player weapon switches while lowering) | EVERY FSM event | NPC Brain: `_npcs.Count == 0 \|\| !On`, event "hide", Attack/trigger; dogs: `Dogs.Count == 0 \|\| !Running`; Guns: `Unloader.Busy` field compare; transition: cached WeaponInHand FSM reference, SmoothWeaponSwitch, replay guard | Apocasaver `Fsm_ProcessEvent_Patch` (separate Harmony owner; coexists) |
| `Fsm.EnterState(FsmState)` | prefix + postfix (never skip) | Guns: `MagazineStatePatch` -> `PerGunAmmo.BeforeState` / `AfterState`; CleanHUD: `PauseCursorFix.AfterEnterState`, `LoadingScreen.AfterEnterState`; Performance: `PollSleep.AfterEnterState`, `CarLod.AfterEnterState` | every state entry of every FSM (x2) | Guns: name length 12; CleanHUD: running, Menu/play on __GameManager__ or Continue/clicked on native savegame / Yes_Save; Performance: tracked FSM / active feature | Apocasaver `AutosaveStatePatch` (postfix) |
| `ES3Internal.ES3ReferenceMgrBase.GetPrefab(long,bool)` | prefix (first resolver returning a prefab wins) | Patrols: `MotorcycleIntegration.ResolvePrefab` (id 735710202610050001), `PrefabLookup.Resolve` (War Rig, id 735710202610090001); Survival: `SurvivalModule.ResolvePrefab` (Stew Pot, id 7392481500621300201) | per prefab lookup on load | `id != PrefabId` | |
| `PlayMakerFSM.OnEnable` | postfix (observe) | Patrols: `NativeStormProtection.TrackDust.Postfix`; Performance: `FsmRegistry.OnEnablePost` (PM1 index), `GuiLoopFeature.OnFsmEnabled` (PM5) | per FSM enable | Patrols: `FsmName != "SandPlayer"`; Performance: registry users > 0 / static `On` | |
| `Apocaspawner.Catalog.Entries` (optional, reflected) | postfix | Survival: `SpawnerBridge.CatalogBuilt`; Patrols: `SpawnerIntegration.CatalogPostfix` | catalog access | Survival: cached returned list/entry reference; Patrols: spawn enabled, entry key | Adds Stew Pot and War Rig through one Core dispatcher; no patch without subscribers |
| `NWH.Common.FloatingOrigin.FloatingOrigin.Jump` | prefix + postfix (**always patched**) | Core: `Origin.BeforeJump` / `AfterJump` -> `IModule.OnOriginShift(delta)` for every running module (PlayerBody: active climb path; NPC: ghosts, nav boxes, brain/idle/nests/webs positions; Guns: bullets in flight; HUD: floating numbers; Patrols: `TrafficSpace.OriginShift`). No module subscribes to the raw `Hooks.OriginJump`. | per 1000 m jump | delta == zero -> nothing | standalone Apocapatrol (`TrafficSpace.OriginJump`, removed) |

## 2. Core-only patches (`Core.Patches.Patch(null, ...)`, Harmony id = GUID)

| Target | Kind | Handler | Purpose | Hot? | Filter |
|---|---|---|---|---|---|
| `ES3Internal.ES3Reflection.GetType(string)` | prefix | `Core.SaveTypes.Prefix` | Maps saved component type strings `Apocapatrol.X, Apocapatrol`, `WarRig.X, WarRig` (and `Apocaplayer`, `NPCAI`, `Gunplay`, `GunplayHUD`, unused today) to `WildWasteland.Modules.<Id>.X` by prefix rename; leaves the lookup to ES3 when the old assembly is still loaded | per saved type string on load (cached per string) | `typeString` has no comma -> vanilla; dictionary cache |
| `iTween.TweenUpdate` | prefix | `Core.ITweenGuard.Prefix` (iTween Guard 1.0.0, always on) | A physics tween whose Rigidbody was removed (part bolted on, item picked up) is switched to non-physics (`physics`/`kinematic` = false via Harmony `___field` injection) instead of throwing a NullReferenceException every physics tick | per running tween per Update/FixedUpdate | `!___physics` -> return (bool field, no call); then one `GetComponent<Rigidbody>` for physics tweens only. Not patched when the standalone iTweenGuard is loaded |
| `iTween.Stop(GameObject,string)` | transpiler | `Core.ITweenGuard.StopTranspiler` / `StopPrefix` (always on) | Guard the prefix Substring against shorter tween names during PlayMaker action exit, including traffic vehicle destruction; continue the original stop loop and dispose matching tweens normally | per tween on the target during typed Stop | Short names remain nonmatches; valid Substring and native lowercase comparison/disposal are unchanged. Independent of the standalone Rigidbody guard |
| `HutongGames.PlayMaker.ActionHelpers.GetGameObjectFsm(GameObject,string)` | prefix (skip) | `Core.QuietFsm.Prefix` (moved from Patrols 2026-10-14; works with Patrols off) | Same lookup as vanilla without the "Could not find FSM" warning | every FSM lookup | static bool `QuietFsm.On` (= tools.cfg `Patrols / Debug` "Hide missing-FSM warnings", default true) |
| `PlayMakerPrefs.get_LogPerformanceWarnings()` | postfix | `Core.QuietFsm.PerformanceWarnings` | Native handler-creation performance diagnostics pass only with WW global VerboseLog on; trigger handlers still initialize normally | FSM preprocessing/handler setup | Original preference AND Log.Verbose; independent of QuietFsm.On; no Unity log filter |

`Apocasetter.SettingsUI.Toggle()` (optional, Core-owned prefix `ApocasetterBridge.BeforeMenuToggle`) blocks opening the F6/MODS window while LoadingProgress.ScreenVisible or SaveBus.Busy, while allowing an already-open window to close. Resolve once through the existing bridge and validate Toggle()/_open signatures; no Apocasetter DLL/config change. F3's shared ToolWindow.Open path uses the same gate, and Tick closes a pre-existing WW window and defers its new-game auto-open until loading ends.

Not Harmony but worth knowing: `Core.SaveBus` polls `SaveLoadGame.ActiveStateName` once per frame (no patch on the SaveLoadGame FSM); Apocasetter's `InputBlocker` prefixes every PlayMaker input action and WW's input handlers still run under it (HarmonyX runs all prefixes) - `Core.Hotkeys` early-outs on `ApocasetterBridge.InputBlocked`.

## 3. Module patches (targets only that module uses; Harmony id `<GUID>.<Id>`)

### Survival (#134, Testing)

| Target | Kind | Handler | Filter (first line) | Hot? |
|---|---|---|---|---|
| `ES3Internal.ES3Prefab.Awake` | postfix | `SurvivalModule.PrefabAwake` | prefab ID equals 7392481500621300201 | native prefab creation/load; registers live pots, no custom saved components |

Shared GetButtonDown/GetPrefab/Apocaspawner catalog subscriptions are listed in section 1. Runtime work uses `Survival.StewPot` (LateUpdateLast), `Survival.References` (1 s), and `Survival.Existing pots` (500 us scene discovery on enable/world-ready).

### PlayerBody

| Target | Kind | Handler | Filter (first line) | Hot? |
|---|---|---|---|---|
| `ApocaChaseCamera.ApocaplayerBridge.FindType` (optional, 0.2.5) | prefix | `ChaseCameraCompat.BeforeFindType` | PlayerBody running; maps only the five former Apocaplayer types to this module and its config facade | Source bridge discovery only; ChaseCamera owns its original view/aim/origin hooks |
| `HutongGames.PlayMaker.Actions.MouseLook.OnUpdate` | prefix (skip) | `ThirdPerson.BeforeMouseLook` | `return !Orbiting` (static bool) | every MouseLook, every frame |
| `HutongGames.PlayMaker.ActionHelpers.DoMousePick(float,int)` | postfix | `PickAssist.AfterDoMousePick` | `Running` + static `ThirdPerson.On / CarShift` | once per frame something picks |
| `HutongGames.PlayMaker.Actions.iTweenMoveTo.OnEnter` | prefix | `AimTransition.BeforeMoveTo` | `Fsm.Name == "AimDownSights"` | per ADS press / release |
| `HutongGames.PlayMaker.Actions.PlayRandomSound.OnEnter` | prefix + finalizer | `FemalePain.Before` / `Finally` | `FemalePainSounds` + Female (static), FSM name, owner == Player | per damage sound |
| `HutongGames.PlayMaker.Actions.SetAudioClip.OnEnter` | prefix + finalizer | `FemalePain.BeforeDeath` / `FinallyDeath` | `FemalePainSounds` + Female (static), Health/playerDeath, owner == Player | per death sound |
| `HutongGames.PlayMaker.Actions.MousePickEvent.DoMousePickEvent` | prefix (skip during ignition hotkey only) | `Car.BeforeMousePick` | `!_ignitionPress`, then exact ignition FSM reference | every panel pick; a key press follows the configured hover/check/click events without the physical mouse check cancelling hover on state entry |
| `NWH.VehiclePhysics2.Steering.CalculateSteerAngles` | prefix (observe original) | `WheelArms.BeforeSteer` | exact local driver's cached Steering reference, removable steering-wheel mount has no attached vehPart | every vehicle physics tick; clears only local missing-wheel steering input before native smoothing/centering |

Dropped from Apocaplayer: `BepInEx.Configuration.ConfigFile.Save` postfix (SettingsLayout). Non-Harmony hook: `Camera.onPreCull += ThirdPerson.PreCull` (only while third person is on).

### NPC

| Target | Kind | Handler | Filter (first line) | Hot? |
|---|---|---|---|---|
| `Rotate.DoRotate` | prefix (skip) | `Brain.BeforeRotate` | `_npcs.Count == 0 \|\| !On`, FSM name | per frame per rotating NPC |
| `LookAt.DoLookAt` | prefix (skip) | `Brain.BeforeLookAt` | same | per burst state |
| `AddForce.DoAddForce` | prefix (skip) | `Brain.BeforeAddForce` | same | per Unstuck hop |
| `AudioPlay.OnEnter` | prefix (skip + finishedEvent + Finish) | `Senses.BeforeAudioPlay` | `!On`, FSM name | every AudioPlay |
| `RandomWait.OnEnter` | prefix (swaps min/max, runs original) | `Aim.BeforeRandomWait` | `!Running`, FSM "RangedAttackWait" | per burst cycle |
| `ActivateGameObjects.OnEnter` (optional, `TypeByName`) | postfix | `Brain.AfterActivate` | as ActivateGameObject | rare |
| `Wait.OnEnter` (only with the dog bundle) | prefix (modifies time) | `DogAnimations.BeforeDamageWait` | `Dogs.Count == 0 \|\| !Running` | every Wait |
| `DogCompanion.Dog.Update` (prefix + postfix), `.ChaseTick`, `.LateUpdate`, `.OnDisable`, `.Steer`, `.RideTick`, `.Step`, `DogCompanion.Distraction.Nearest` (third party, only when Dog Companion is loaded) | as NPCAI | `DogCompanionCompat.*` | `On` = bridge resolved && Running && `[NPC] DogCompanionCompat` | per dog tick |

Dropped from NPCAI: postfixes on `Apocapatrol.Patrol.BailOut` / `Apocapatrol.Explode.Blast` (now `Services.CrewBailedOut` / `VehicleExploded`), the 7 Harmony instances. Known other patchers of Rotate/AddForce: ApocalypterAI.

### Guns

| Target | Kind | Handler | Filter (first line) | Hot? |
|---|---|---|---|---|
| `Apocaspawner.Parameters.WeaponDamage` (optional) | postfix | `ApocaspawnerDamage.AfterDamage` | player projectiles enabled, non-null result, crossbow owner | per spawner parameter read; applies Guns' projectile multiplier to the native base stat |
| `UiTextSetText.OnEnter` | prefix + finalizer (temporary literal text) | `WeaponDamageText` | player projectiles enabled, crossbow ItemName/Buyable, native damage literal | per UI text state entry; CleanHUD also observes in postfix |
| `GetLayer.OnEnter` | postfix | `Tracers.AfterGetLayer` | `TracersEnabled`, FSM "Attack", state `getLayer`, result 10 | rare |
| `SetAudioClip.OnEnter` | postfix | `Tracers.AfterSetAudioClip` | `TracersEnabled`, FSM "Attack", state `actorHitSound`, player melee blade | per blade hit on living actors or corpses |
| `PlaySound.DoPlaySound` | prefix (camera position for player empty clicks and reload sounds; OnEnter keeps Finish) | `EmptyGunSound.Before` | FSM Attack/empty (empty_fire), AnimSound or Reload; weapon under PlayerRig.Parent, first person | per PlaySound |
| `TranslatePosition.OnFixedUpdate` | prefix (false for registered orbs, `Finish()` when not everyFrame) | `Tracers.BeforeTranslatePosition` | `_orbs.Count == 0` | every TranslatePosition per FixedUpdate |
| `IntClamp.DoClamp` | prefix (false only while an overflow drop is pending) | `AmmoCapacity_Patch` -> `BackpackOverflow.BeforeClamp` | `Fsm.Name.Length == 4` + `"Ammo"`, per-action cached classification, `Idle()` fast path | every IntClamp, everyFrame x calibers |
| `FloatClamp.DoClamp` | prefix (false only while a plate drop is pending) | `ArmorCapacityPatch` -> `ArmorOverflow.BeforeClamp` | `Fsm.Name.Length == 13` + `"ArmorBackpack"`, per-action cache, `Idle()` | every FloatClamp, everyFrame |
| `HutongGames.PlayMaker.FsmState.OnEnter` | postfix | `BackpackSlot_Patch` -> `BackpackOverflow.OnSlotEnter` | `Fsm.Name.Length != 13`, `DropBackpackOverflow`, `"SlotEmptyFull"`, owner `Backpack_Item` | every state entry of every FSM |
| `ES3PlayMaker.SaveAll.Enter` | postfix | `MagazineSavePatch` -> `PerGunAmmo.SaveGlobal` | FSM "SaveLoadGame", key `global` | per save |
| `ES3PlayMaker.LoadAll.Enter` | prefix | `MagazineLoadPatch` -> `PerGunAmmo.LoadGlobal` | same | per load |
| `ES3.StoreCachedFile(ES3Settings)` | prefix | `MagazineStorePatch` -> `PerGunAmmo.BeforeStore` | `settings.path == _saveFile` | per cache flush |

Known other patchers of SaveAll / LoadAll: Apocasaver `SaveHooks` (prefix/postfix/finalizer, separate owner; WW's are observe-only + one key write - coexist). Dropped from Gunplay: `Apocaunloader.Runner.Update` prefix + `UnpatchSelf` on Apocaunloader's id (now ConflictsWith), `Apocapatrol.MeleeWheels.RaiderHandles` prefix (now `Services.Guns.HandlesFittedWheelMelee`), `PatchAll`.

### HUD

No module-only patches (its single target is the shared `SetFsmFloat.OnEnter` prefix above).

### CleanHUD

| Target | Kind | Handler | Filter (first line) | Hot? |
|---|---|---|---|---|
| `ApocaChaseCamera.DrivingReadings.Capture` (optional, 0.2.5) | transpiler (observation) | `ChaseCameraHud.CaptureReadings` / `Record` | Successful source snapshot; bridge ready + CleanHUD running; original return value retained | Per source driving HUD tick and CleanHUD refresh in either driving view; typed snapshot fields, no boxing or replacement telemetry |
| `UiTextSetText.OnEnter` | postfix | `MessageHook.Postfix` | `!Active`, null Text, everyFrame; HUD canvas only, timed-state classification cached by FsmState | per UI text state entry |
| `Apocasaver.AutosavePresentation.Begin` / `.End` (optional, reflected third-party UI methods) | postfix | `ApocasaverLoading.AfterBegin` / `.AfterEnd` | Cached loading screen provider null; display only, exceptions contained | per autosave presentation begin/end |
| `Apocasaver.SaveNaming.OnGUI` (optional, reflected third-party UI method) | prefix | `ApocasaverLoading.BeforeNameGui` | Running + Clean menus enabled + naming popup open; replaces drawing only, original Confirm/Cancel delegates retain save/input/cursor lifecycle; original popup when off or drawing fails | per source OnGUI callback; no other known patchers |
| `Apocapocket.ExtraSlots.BuildUi` (optional, reflected third-party UI method) | postfix | `ApocapocketInventory.AfterBuildUi` | Bridge absent / first cloned icon absent; increments a revision for one-time alpha capture and exclusion of CleanHUD-owned transforms from native card shifting | per native pocket UI creation |
| `a.u.A(): void` (optional World Amp 1.0.7 engine HUD) | prefix | `VistasEngineHud.Draw` | CleanHUD running + HUD enabled + bridge ready; suppresses numeric overlay and sends overheating/seized transitions to MessageHook; original when off | per source gameplay Repaint; no other known patchers |

Shared `GetButtonDown.OnUpdate` prefix: `CancelHook.Prefix`, registered after HUD and before Patrols. Early-out on module/menu off or expired `BlockCancelFrame` with no loading screen; skips only Cancel while a CleanHUD sub-screen, key capture, dialog or loading/saving screen is open. Apocasetter remains the input blocker. No separate Harmony instance or runner. `Canvas.willRenderCanvases` keeps replaced native graphics hidden; removed on disposal.

### Patrols

| Target | Kind | Handler | Filter (first line) | Hot? |
|---|---|---|---|---|
| `ES3ReferenceMgrBase.AddPrefab(ES3Prefab)` | postfix (observe only) | `Prefabs.AfterAddPrefab` | Patrols running and original returned true; invalidate lookup generation, no scan inside the patch | per new ES3 prefab registration |
| `ES3ReferenceMgrBase.Awake` | postfix (observe only) | `Prefabs.AfterManagerAwake` | Patrols running; invalidate lookup generation, no scene API inside the patch | per ES3 manager initialization |
| `Wait.OnUpdate` | prefix (hold final loading transition) | `PatrolLoadPreparation.BeforeWait` | Loading flag, Running, exact SaveLoadGame FSM reference, state `Wait for Terrain Gen`; original resumes when cache and saved patrol preparation finish | every Wait update; unrelated actions return immediately; NPC's dog patch is on the separate OnEnter method |
| `GetDistance.OnEnter` | prefix (skip + Finish) | `PoiEncounter.BeforeCleanupDistance` | Cached actions of __GameManager__/DeleteFarAwayItems only; protected tankers/parts and undriven POI trucks take the native FINISHED path instead of distance deletion | native 10 s cleanup sweep |
| `Explosion.ShouldApplyForce(GameObject)` | postfix | `NativeStormProtection.RadialForce.Postfix` (host + guest presentation) | `__result && rigs loaded`; host resolves remote player shelter too | per explosion force query |
| `Tornado.OnTriggerStay(Collider)` | prefix (skip) | `NativeStormProtection.Suction.Prefix` (host + guest presentation) | rigs loaded; existing tanker bounds | per tornado contact per physics step |
| `MotorcycleMod.Factory.Template()` (third party, only when loaded) | postfix | `MotorcycleIntegration.PrepareRearAccess` | - | rare |
| `Apocaspawner.Spawner.Spawn(entry,int)` (third party, only when loaded) | prefix | `SpawnerIntegration.SpawnPrefix` | entry key | on spawn; catalog subscription is listed in section 1 |
| `ApocaDustStorm.StormShelter.ContainsActor` / `ApocaDustStorm.PlayerStormHazards.Tick` (third party, only when loaded) | prefix | `DustStormBridge.Shelter` / `RefreshOnCrossing` | `broken` flag (one failure stands both down) | per DustStorms tick |

Dropped from Apocapatrol / War Rig: the 6 + 1 Harmony ids, `TrafficSpace.OriginJump` (Core.Origin), WarRig `PatchAll(assembly)`, `Apocasetter.Catalog.OptedIn` postfix (SettingsIntegration), WarRig's separate `CreateObject` / `GetPrefab` patches (now dispatcher subscribers). Non-Harmony hooks: `TerrainTile.OnBeforeTilePrepare / OnTileFinalized`, `Application.quitting += PatrolPersistence.FlushPending`, `SaveBus.StateChanged += PatrolPersistence.OnStateChanged` (subscribed in Init, removed in Shutdown).

### Performance (#133, first pass, Testing)

Every patch is installed once when its feature first starts and stays for the module lifetime; bodies test the feature's static flag first (Off = one bool test). Hook subscriptions: `Fsm.ProcessEvent` prefix (FS1 wake, CT8 Coyote filter; both return true unless CT8 drops a no-op repeat), `Fsm.EnterState` postfix (PollSleep wake, CT2), `CreateObject.OnEnter` postfix (render scan of spawned objects), `PlayMakerFSM.OnEnable` postfix (above).

FS4 is an exception to the Off fast path: once installed, its prefix always runs the ActionDataPort copy; Off selects reflection inside that copy. No ActionData method is transpiled.

| Feature | Target | Kind |
|---|---|---|
| PM1/PM3 registry | `Fsm.OnEnable`; `PlayMakerFSM.OnDisable`, `PlayMakerFSM.OnDestroy` | enable prefix observes after native list append and before callbacks; disable copy removes the index at the native removal point while tracking, using vanilla List.Remove when PM3 is off; destroy copy when PM3 is on |
| PM2 | `Fsm.SendEventToFsmOnGameObject(GameObject,string,FsmEvent)`, `Fsm.BroadcastEventToGameObject(GameObject,FsmEvent,FsmEventData,bool,bool)` | prefix (replaces) |
| PM4 | `Fsm.BroadcastEvent(FsmEvent,bool)`, `PlayMakerFSM.BroadcastEvent(FsmEvent)` | prefix (replaces) |
| PM5 | `PlayMakerGUI.OnGUI` (replaces), `PlayMakerFSM.OnDisable/OnDestroy` (observe), `Fsm.CreateSubFsm` (postfix), `PlayMakerOnGUI.Start` (postfix), `PlayMakerFSM.OnBecameVisible/OnBecameInvisible` (prefix, skips proven no-op events) | mixed |
| PM6 | `Fsm.OnTriggerEnter/Stay/Exit`, `Fsm.OnCollisionEnter/Stay/Exit` | prefix (exact copies; unhandled system event skipped) |
| PM7 | `ActionHelpers.DoMousePick` | prefix (shared per-frame cast); PlayerBody `PickAssist` postfix still runs after it; Shadow compares collider/triangle exactly and point/distance within 0.1 mm to exclude PhysX float noise |
| PM10 | `ConvertFloatToString` / `ConvertIntToString` private Do methods | prefix |
| FS1 | `Fsm.Update` (postfix: sleep decision), `Fsm.Event(FsmEventTarget,FsmEvent)`, `SwitchState`, `Start`, `OnEnable`, `Stop`, `Init`, `Reinitialize`, `Clear`, `OnDestroy`, `DelayedEvent` x2, every physics/animator/GUI callback of `Fsm`, `FsmStateAction.Finish`, `PlayMakerFSM.OnBeforeSerialize`, `PlayMakerFSM.Awake` (postfix) | prefix = wake |
| FS2 | `FsmProperty.GetValue`, `FsmProperty.SetValue` | prefix (replaces, falls back to vanilla) |
| FS3 | `CallMethod.DoMethodCall` | transpiler (MethodBase.Invoke -> compiled invoker) |
| FS4 | `ActionData.LoadActionField` | prefix (always replaces with ActionDataPort; generated setters/constructors when ready and On, reflection otherwise) |
| SigmaMod compatibility | `PlayMakerFSM.get_FsmStates` | postfix; while SigmaMod's GUID is loaded, calls native `InitStates` for uninitialized FSMs before callers read actions; installed at module Init independently of FS4, no SigmaMod version/private API dependency |
| Content (FsmEdits) | `PlayMakerFSM.Awake` | postfix (applies verified FSM edits; clears copied ManualUpdate) |
| CT2 | `SetIsKinematic.OnEnter` | prefix |
| CT9 | `AddComponent.OnEnter` | prefix (duplicate Rigidbody) |
| RC5 | `ThreadManager.TaskThreadAction` (MapMagic) | prefix |
| EN7 | `AzureTimeController.FixedUpdate`, `AzureWeatherController.FixedUpdate` | prefix |
| EN2 | `PostProcessEffectSettings.IsEnabledAndSupported` (postfix), `SharpenRenderer.Render` (prefix) | resolved by reflection (WW does not reference Unity.Postprocessing.Runtime) |
| EN3/RC2/RC8 | `GraphicSettingHelperMethods.ChangeQualitySettings` | postfix (re-apply caps after the graphics menu) |
| RC6 | `FastIKFabric.LateUpdate` | prefix (skip invisible/far rigs; never the player) |

## 4. Known shared targets without a WW subscriber

Other mods patch these too; if a future module needs them, add a dispatcher: `Rotate.DoRotate`, `AddForce.DoAddForce` (NPC-only module patches today; ApocalypterAI patches them), `Wait.OnEnter`, `RandomWait.OnEnter`, `AudioPlay.OnEnter`, `LookAt.DoLookAt`, `FsmState.OnEnter`, `IntClamp.DoClamp`, `FloatClamp.DoClamp`, `ES3PlayMaker.Save/SaveAll/Load/LoadAll.Enter` (Apocasaver), `ES3Settings.get_FullPath`, `ES3IO.CommitBackup` (Apocasaver only - never patch these for save purposes, see notes/HostMods.md 2.6).

### POI (#81)

No Harmony patches and no scheduler jobs. Subscribes to native TerrainTile.OnBeforeTilePrepare, OnTileFinalized, OnTileApplied and OnTileMoved; removes subscriptions and restores graph links/nodes on shutdown. Site components only handle trigger/enable events. Patrols supplies vehicle assembly through Services.Poi; it retains the existing temporary build coroutine and native vehicle/NPC behavior.

### Optional Engine Sim compatibility (0.1.13 RC10, #127, Testing)

Only Engine Sim 1.7.0 is adapted. Hook/delegate setup is transactional; an unsupported interface restores standalone behavior. No source DLL/native asset is modified or redistributed.

| Owner | Target | Kind / handler | Frequency / filter |
|---|---|---|---|
| EngineSimCompat | EngineSim.EngineManager.Update / FixedUpdate | ScheduledOnly prefix; original callbacks run from named Update / FixedUpdate jobs | Manager identity and dispatch flag; exactly one execution per corresponding scheduler invocation |
| EngineSimCompat | EngineSim.EngineManager.Scan | CachedScan transpiler substitutes only the RealisticEngineSound discovery array | Original 0.5 s scan, candidate filtering, running preference, distance sorting, MaxEngines and session reconciliation retained |
| EngineSimCompat | EngineSim.EngineManager.FindListener; EngineSim.Listener.get_Current | FindListener prefix supplies cached active listener | Bridge ready; cached listener snapshot includes scene and persistent roots |
| EngineSimCompat | EngineSim.IgnitionSwitch.Update | SwitchInput transpiler, BeforeSwitch / AfterSwitch prefix/postfix | Existing source press/hold/release logic; keyboard input scoped to the player's current switch/frame, input/focus gates; bounded AI starter request |
| EngineSimCompat | EngineSim.IgnitionSwitch.Pointed | CachedCamera transpiler / KeyboardPointed postfix | Core cached camera; matching player keyboard hold counts as pointing |
| EngineSimCompat | EngineSim.IgnitionSwitch.SetText | SetText prefix writes native Text through cached delegates | Bridge ready; original text retained, no per-update property reflection |
| EngineSimCompat | EngineSim.DriveCoupling.Release | Released postfix removes pending starter/player ownership | Bridge ready; source releases engine and FSM itself |
| EngineSimCompat | NWH VehicleController.OnEnable; RealisticEngineSound.OnEnable | VehicleEnabled / SoundEnabled postfixes queue a sliced subtree / register the sound | Bridge ready; scene-boundary seeds include inactive sounds, no recurring whole-scene walk |

The manager jobs fail open to original callbacks/discovery on an uncaught exception. Native simulation threads, DSP audio, WheelCoupling's WaitForFixedUpdate coroutine, Windage/RimFriction physics and Belt.LateUpdate remain owned by Engine Sim. Disabling the module restores original callbacks without stopping source sessions. Engine Sim's own verbose FSM dump is unchanged and is not sliced.

### Native Quests and optional Apocatalker (2026-10-18, Testing)

Quests runs directly as a WW module; all former Quests/Hire/Takeover/Game private Harmony targets have been retired. Named jobs own frame/pose/progress/camp/giver/map/reputation/interaction and 500 us world discovery. CleanHUD reads typed Services.Quests/QuestUi rather than patching quest drawing.

| Owner | Target | Kind / handler | Frequency / filter |
|---|---|---|---|
| Quests | Shared Hooks.CreateObjectEnter | Death postfix; source kill/giver-death notifications | Health FSM spawn state, ready world |
| Quests | Shared Hooks.GetButtonDownUpdate / GetButtonDo / GetButtonUpUpdate / GetMouseButtonDownUpdate | Input prefixes | Original native quest conversation only; scoped input blocking |
| Quests | HutongGames.PlayMaker.Actions.MouseLook.DoMouseLook | BlockLook prefix | Native conversation flag first, then player owner; skips mouse reads/rotation while open, independently of PlayerBody and CleanHUD |
| Quests | Optional Talk.Plugin.Open / Close | Opened / Closed postfixes | Attach native quest choices/flags/voice lifecycle to original Talk content |
| CleanHUD | Optional Talk.DialogueUI.ShowDialogue / AppendChatLine / SetChoices | Changed postfix | Invalidate mirrored dialogue; source content retained |

Optional Talk fields/calls use Cecil-generated delegates through Core.OptionalAccess, avoiding stripped DynamicMethod and interpreted Expression.Compile in hot paths. Source ES3 reflection remains a save-boundary transport, not an external Quests dependency.

### Weather native fog scope — 2026-10-16 (Testing)

| Owner | Target | Kind / handler | Frequency / filter |
|---|---|---|---|
| Weather | UnityEngine.AzureSky.AzureFogScattering.OnRenderImage | WeatherSky.NativeFogBefore prefix + NativeFogAfter finalizer; temporary palette/pale-fog density overrides, restored even on exception; original rendering always runs | Native camera image effect; only selected gameplay camera during active WW storms, no scene search or extra blit |
| Weather | HutongGames.PlayMaker.Actions.Explosion.DoExplosion | WeatherNative.ExplosionBefore prefix suppresses the original centre-sphere force for native SandStorm actions only; WW nonalloc annular queries use the native parameters/pulse state and patched ShouldApplyForce | Native Tornado OnEnter/FixedUpdate; Fsm name then cached prefab-identity classification; other explosions unchanged |
| Weather | HutongGames.PlayMaker.Actions.TriggerEventByLayer.DoTriggerEnter | WeatherNative.TriggerBefore prefix suppresses original native centre-trigger damage; annular entry forwards next/next2 to the original TornadoDamage FSM | Trigger contact; TornadoDamage name then cached native SandStorm identity; other triggers unchanged |
| Weather | Shared Hooks.FsmEnable | WeatherNative.Enabled postfix tracks SandPlayer roots; disables native DisableSandstorm deletion FSM while replacement owns encounters, restores its original enabled state on shutdown | Enable plus 500 us sliced existing-FSM discovery; name filter before native identity lookup |
| Weather | Shared Hooks.ProcessEvent | WeatherNative.EventBefore observes SaveGame and restores borrowed native component state before native serialization; event always continues; OnSaveEnd suppresses again | Event name filter; no ES3 target patched, no new save key/component |
| Weather | Shared Hooks.SetVelocityDo | WeatherNative.VelocityBefore prevents native motion for owned manual previews and stops natural anchors during diagnostic freeze/non-gameplay; original natural movement otherwise runs | Move name then cached root ID; unrelated SetVelocity calls unchanged |

## VistasWorldGen 1.0.7 (Core-owned optional private targets, 2026-10-18, Testing)

CleanHUD also owns the optional `a.u.A(): void` engine HUD prefix through `VistasEngineHud`. While Clean HUD is enabled it replaces the source numeric overlay with once-per-condition overheating/seizure notifications; disabling Clean HUD restores source drawing. Active/Temperature/Stalled use cached Core.OptionalAccess delegates. Source simulation and config are unchanged.

`VistasWorldGen.QuietLog` prefixes `BepInEx.Logging.ManualLogSource.Log(LogLevel, object)` only when Vistas is installed. It suppresses Info/Debug for the exact Vistas logger instance while WW global VerboseLog is off. Warnings/errors/fatal/message levels and all other logger instances remain unchanged; enabling VerboseLog restores source diagnostics immediately. No console/disk config changes or log-string inspection.

Before installing the Vistas hooks, `VistasWorldGen.ResolveField` prefixes MonoMod.Utils.ReflectionHelper's private `_ResolveReflection(MemberReference, Module[])`. It handles only FieldReference operands whose scope identifies the loaded Vistas assembly; candidate fields must match their declaring type, name and resolved field type (including constructed generics, with modifier wrappers removed). Other references keep MonoMod's original resolver. This corrects the installed MonoMod version's name-only field matching for Vistas' overloaded obfuscated names, which otherwise rewrites valid field accesses with incorrect types and prevents patch activation. The patch is Core-owned and is absent without Vistas; it performs no scene scans or gameplay callbacks.

All obfuscated members resolve by full signature, including return/field types; no game action dispatcher is duplicated. `A.E.A(A.c,float)` observes place entry; `A.E.A(A.y,bool)` suppresses far discoveries and observes successful nearby discoveries; `A.E.d()` suppresses only source banner rendering while CleanHUD owns it. `a.t.A(float)` suppresses summit scouting. `a.y.e()` and `a.y.B(a.f)` veto dust/haboob eligibility while WW Weather is present; `a.y.A(a.f,float)` converts explicit dust/haboob transitions to windy. `A.q.a(double[],double[],int,bool,string)` snapshots real road centreline segments; `A.S.A(A.q,MapMagicObject,TerrainTile,Terrain,TerrainData,A.S+A,int,float)` observes completed main-terrain grading; `WorldAmpDriver.ClearWorld()` clears road snapshots. Harmony patches are installed through Core.Patches with null/Core ownership. Non-Vistas games install none of these hooks. Version mismatches warn and continue installation, including newer releases; only actual missing APIs or hook failures prevent a ready bridge.


## Non-live MP iteration, steps 6–13 (Testing)

MP notification presentation (2026-10-18, Testing): Core MultiplayerNotifications prefixes `ApocalypterMP.ModRunner.OnGUI`, `UI.Chat.Notice(string)` and `UI.Hud.Show(string,float)`, owned by MultiplayerBridge. OnGUI forwards the same seven cached native UI calls outside load/save guards, preserving existing nested hooks while removing the direct debug status label; no native MP UI draws during loading. Notice/Show route live messages through the optional CleanHUD provider, suppress loading/pre-ready system history and keep native fallback where appropriate. `Hud.Show` empty clears still execute. No per-frame reflection/scene scans, no added OnGUI component and no external DLL edit. See compat/ApocalypterMP/NOTIFICATIONS.md.

Hosting/settings follow-up (2026-10-18, Testing): the existing MultiplayerSaving prefix on `UnityEngine.UI.Button.Press` now also intercepts the cached native client Save button to send a host notification; other buttons retain the Frozen save guard. Native `WorldStore.Load/Create/PathOf`, `MpWorld.Name/SaveFile`, `GameLauncher.Launch`, `NetSession.NameOf(ulong)` and `UI.Chat.Notice(string)` are accessed through cached optional delegates, without new Harmony targets. Launcher path/identity hooks remain; automatic hosting/joining is removed. Handshake revision 6 adds save-request packet 142 and supported native host slot handoff. See compat/ApocalypterMP/HOSTING-SETTINGS.md.

MP UI follow-up (2026-10-18, Testing): Core ApocasetterBridge adds optional postfixes on `Apocasetter.InputBlocker.Set(bool)` (UI open/close) and `Apocasetter.SettingsUI.LateUpdate()` (each frame while UI exists). Preserve source input blocking/cursor behavior; after a UI time write, loaded multiplayer worlds use timeScale 1, while MultiplayerSaving.Frozen retains 0. Offline source behavior is unchanged. The `_open` field is validated as bool before installing the LateUpdate postfix. No external DLL changes; runtime installation remains pending human verification.

See compat/ApocalypterMP/STEP-13-ITERATION-HANDOVER.md for the active checkpoint and limitations. New shared `Core.Hooks.ExplosionDo` owns `Explosion.DoExplosion`: MultiplayerBridge routes remote forces for non-Tornado actions, Weather retains native Tornado interception. Both subscribe through this dispatcher; no duplicate module prefix on that target. Shared CreateObject observes bounded transient presentation, SendEvent filters non-host saves, existing ProcessEvent owns damage guards. Other new bridge targets: MP WorldSaver.Save (enumerator wrapper)/Tick, PauseControl.Tick, PlayMakerFSM.BroadcastEvent(FsmEvent), Button.Press; MP ItemVars.For/Apply, WorldObjects.OnVars/ShouldSendVars; MP OnHello and UDP/Steam Send for handshake/bootstrap; WorldObjects.OnThrown/OnLocalThrow; FsmFloatAdd.OnEnter for remote SanityAdd. Optional EngineSim DriveCoupling.Update and WheelCoupling.Step gate owner simulation without moving post-physics timing. Existing MP native SendTo/All/AllExcept hooks provide ordered controls. Guns extends existing item fields; player/body and native Quests targets remain as recorded by their compatibility adapters. All targets use cached delegates and optional bridge/module owners; gameplay/runtime patch installation is not verified.

## Non-live MP step 14 — shooter impacts (Testing)

Guns uses its existing tracer interception/shared CreateObject observer for reliable client creature-hit reports and host bolt attachment/corpse/ricochet/removal state. No new Harmony target. Guest replay suppresses the reported hit's Damage, DamageFlammable and CheckFriendly gameplay events while retaining local effects. Host dispatches the exact native Bodypart damage once and keeps native health replication. Visual shot packets include world epoch; handshake revision 2 rejects mismatched overlays. See compat/ApocalypterMP/STEP-14-CLIENT-IMPACTS.md for bounds, remaining validation gaps and manual tests. Build-only checkpoint, Deploy=false.
## Non-live MP step 15 — runtime review (Testing)

Current authority: `compat/ApocalypterMP/STEP-15-REVIEW-AND-TESTS.md`; earlier step13/14 statements are historical. The user explicitly authorized isolated local host/join tests. No live deployment.

Core additionally patches native WorldObjects.ClearTheWay's single creature-kind filter: remote uncarried/unowned corpse replicas use the original temporary LetCarThrough/RefreshColliders mechanism, retaining its sweep, timeout and collider restoration. Native MP ApplySpawn makes corpse roots kinematic and positions them from host snapshots; real collision traces showed these replacing run-over creatures with car-blocking obstacles. The filter match is required exactly once or bridge installation fails. Existing OnClientHit/SendCarHit authority hooks remain. WorldInfo waits for host readiness while Welcome/heartbeat is immediate; native SendAllTo is repeated after the guest's post-scene epoch request, not a second scene launch.

Headless-only MapMagicObject.OnEnable selects its native CPU SetHeights application for both terrain resolutions. Normal graphics devices keep their original path. EngineSim FixedUpdate torque calls yield to physical ownership; passive feed follows native replicated RPM/throttle/running. Save hooks also cover ExcludeFromSave cleanup and frozen FollowTarget/FollowCar poses. Existing ItemVars/OnVars/ShouldSendVars hooks now support typed atomic edits, absolute owner commands and final snapshot acknowledgments; Survival pot state stays server-owned. No external MP/EngineSim DLL is modified or redistributed. World settings send deltas after the handshake snapshot. Sustained impact deduplication uses a bounded per-peer sequence window, avoiding a shared saturation cutoff and time-based replay expiry.

Step15 final save retry review: a completed-flush flag distinguishes a successful owner flush from a disposed failed enumerator. Duplicate save phases only re-ACK successful work; failures restart flushing on retry and never release the host's serialization barrier. The external fault-injection run tests this path.
