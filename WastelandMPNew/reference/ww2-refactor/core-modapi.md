# Core kernel and ModAPI - design sketches (C# 7.3, for Wave 0-A)

These sketches fix names, signatures and semantics. W0-A implements them; Wave B agents code against them. Contracts are FROZEN after W0 merges; changes go through `docs/refactor/requests/<package>.md` and are applied by the coordinator between waves.

## 1. ModApi registry

```csharp
namespace WildWasteland.Core.ModApi
{
    /// <summary>Typed service registry: the only way modules reach each other. Providers register in Init;
    /// entries owned by a module are removed automatically when that module shuts down (ModuleHost.Cleanup).</summary>
    public static class ModApi
    {
        /// <summary>Registers <paramref name="impl"/> as the provider of <typeparamref name="T"/> for <paramref name="owner"/>.
        /// A second provider replaces the first and logs a level-1 line (Compatibility may wrap a module provider).</summary>
        public static void Provide<T>(IModule owner, T impl) where T : class { Slot<T>.Set(owner, impl); }

        /// <summary>Current provider or null when the providing module is off. Field read, safe on hot paths.</summary>
        public static T Get<T>() where T : class { return Slot<T>.Value; }

        /// <summary>Convenience form of <see cref="Get{T}"/> for if-patterns.</summary>
        public static bool TryGet<T>(out T api) where T : class { api = Slot<T>.Value; return api != null; }

        /// <summary>Raised (level-1 log too) when a provider appears or disappears, so consumers can drop caches.</summary>
        public static event Action<Type> Changed;

        /// <summary>Removes every provider owned by <paramref name="owner"/>; called by ModuleHost.Cleanup.</summary>
        internal static void RemoveAll(IModule owner) { /* iterate registered slots */ }

        static class Slot<T> where T : class { internal static T Value; internal static IModule Owner; internal static void Set(IModule o, T v) { /* register for RemoveAll, raise Changed */ } }
    }
}
```
Rules: consumers never cache a provider across frames unless they subscribe to `Changed`; every call site handles `null` (provider module off). `Services.*` fields become `[Obsolete]` properties forwarding to `ModApi.Get<T>()` in W0 and are deleted in Wave C.

## 2. Event bus (multiplayer-ready)

```csharp
namespace WildWasteland.Core.ModApi
{
    /// <summary>Marker for event payloads. Payloads are structs with ids, enums, numbers, strings and world-space vectors only -
    /// never GameObject/Component references (use EntityId) and never scene positions (use Origin.World).</summary>
    public interface IModEvent { }

    /// <summary>Handler signature; payload passed by ref so large structs are not copied per subscriber.</summary>
    public delegate void ModEventHandler<T>(ref T evt) where T : struct, IModEvent;

    /// <summary>Who may originate the event in multiplayer: Host (world simulation), Owner (the controlling peer), Local (never sent).</summary>
    public enum Authority { Local, Host, Owner }

    /// <summary>Marks an event type as broadcastable. Events without it are local-only.</summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class BroadcastAttribute : Attribute { public readonly Authority Authority; public BroadcastAttribute(Authority a) { Authority = a; } }

    /// <summary>Optional network transport installed by a future multiplayer mod through ModApi.Provide&lt;IEventTransport&gt;.</summary>
    public interface IEventTransport { bool ShouldSend(Type eventType, Authority authority); void Send<T>(ref T evt) where T : struct, IModEvent; }

    public static class ModEvents
    {
        /// <summary>Adds a handler owned by <paramref name="owner"/>; removed automatically on that module's shutdown.</summary>
        public static void Subscribe<T>(IModule owner, ModEventHandler<T> handler) where T : struct, IModEvent { Bus<T>.Add(owner, handler); }

        /// <summary>Removes one handler (normally not needed: RemoveAll(owner) runs on shutdown).</summary>
        public static void Unsubscribe<T>(IModule owner, ModEventHandler<T> handler) where T : struct, IModEvent { Bus<T>.Remove(owner, handler); }

        /// <summary>Delivers <paramref name="evt"/> to local subscribers in registration order, each in try/catch counted against its
        /// module (ModuleHost error rule), timed when Debug tracing is on; then hands it to the transport if the type is [Broadcast].
        /// Allocation-free: handler arrays are copied on subscribe, not on publish.</summary>
        public static void Publish<T>(ref T evt) where T : struct, IModEvent { Bus<T>.Raise(ref evt); }

        /// <summary>Applies a remote event (called by a transport): runs local subscribers only, never re-sends.</summary>
        public static void ApplyRemote<T>(ref T evt) where T : struct, IModEvent { Bus<T>.RaiseLocal(ref evt); }

        /// <summary>True when anyone listens - lets publishers skip building payloads.</summary>
        public static bool HasListeners<T>() where T : struct, IModEvent { return Bus<T>.Count > 0 || Bus<T>.Broadcast; }

        internal static void RemoveAll(IModule owner) { /* all buses */ }
    }
}
```

## 3. Entity ids

```csharp
/// <summary>Stable id for anything an event refers to. Saved objects: their ES3 reference id (same on every peer that loaded the save).
/// Runtime-only objects: WW-assigned ids from a per-session counter seeded by the host. 0 = none.</summary>
public struct EntityId : IEquatable<EntityId> { public readonly long Value; /* ctor, Equals, GetHashCode, ToString */ }

public static class Entities
{
    /// <summary>Id of <paramref name="go"/>, assigning one on first use (dictionary by instance id; swept on scene load).</summary>
    public static EntityId Of(GameObject go);
    /// <summary>Live object for an id or null.</summary>
    public static GameObject Find(EntityId id);
    /// <summary>Registers a known id (load/replication).</summary>
    public static void Bind(GameObject go, EntityId id);
    /// <summary>The player entity (constant id per peer).</summary>
    public static EntityId Player { get; }
}
```

## 4. Log and Trace (Core facade; Debug module owns the policy and UI)

```csharp
public static class Log
{
    /// <summary>0 = off, 1 = main actions, 2 = deep. Set by the Debug module from [Debug] VerboseLevel; 0 when Debug is off.</summary>
    public static int Level;
    public static bool L1 { get { return Level >= 1; } }
    public static bool L2 { get { return Level >= 2; } }
    /// <summary>Level-1 line, prefixed "[ModuleId] ". Callers on hot paths check L1 first or use the generic overloads.</summary>
    public static void Main(IModule owner, string msg);
    public static void Main<A>(IModule owner, string format, A a);          // formats only when L1
    public static void Main<A, B>(IModule owner, string format, A a, B b);
    /// <summary>Level-2 line (traces).</summary>
    public static void Deep(IModule owner, string msg);
    public static void Deep<A>(IModule owner, string format, A a);
    public static void Deep<A, B, C>(IModule owner, string format, A a, B b, C c);
    /// <summary>Always written, rate-limited 5 per message (reset on scene load).</summary>
    public static void Warn(IModule owner, string msg);
    public static void Error(IModule owner, string msg, Exception e = null);
    /// <summary>Startup summary and migrations: always written.</summary>
    public static void Always(IModule owner, string msg);
}

/// <summary>Cheap named timing scopes for code that is not a job/hook/event (e.g. a bake step). Ids are registered once.</summary>
public static class Trace
{
    public static bool On;                                   // set by Debug
    public static int Register(IModule owner, string name);  // at Init
    public static long Begin(int id);                        // returns 0 when !On
    public static void End(int id, long started);
}
```
`Log.Error` no longer calls `SaveSystem.Fail`; Saves uses `SaveBus.ReportFailure(owner, msg)` explicitly.

## 5. Module contract additions

**D12 (fresh start): ignore `StorageId`, `LegacySections`, `AbsorbedModules`, `PlayerAt/ToolAt`, `MigrateMissing`, `MoveEntry` and `SaveTypes.Moved/MovedNamespace` below - not implemented. Every module binds its own `[<Id>]` sections and uses its own folders.**

```csharp
public interface IModule
{
    // existing members (AGENTS 5.3) ...
    /// <summary>Folder/section name used on disk (P1). Defaults to Id. e.g. Presentation -> "PlayerBody".</summary>
    string StorageId { get; }
    /// <summary>Old config section prefixes this module still binds verbatim (e.g. {"PlayerBody","NPC / Women of Wasteland"}).</summary>
    string[] LegacySections { get; }
    /// <summary>Old module Enabled switches folded into this module, with the feature entry they migrate to.</summary>
    IEnumerable<EnabledMigration> AbsorbedModules();
    /// <summary>Registers ModAPI providers; called after Init succeeds.</summary>
    void ProvideApis();
    /// <summary>Adds rows to shared F3 pages (PLAYER/WORLD/HUD/SYSTEM). Core lays them out; values stay the module's ConfigEntries.</summary>
    void Settings(SettingsShell shell);
}
```
`ModuleConfig` additions: `ConfigEntry<T> PlayerAt<T>(string legacySection, string key, ...)`, `ToolAt<T>(...)` (bind in an exact legacy section), `MigrateMissing<T>(entry, cfgFile, section, key, Func<string,T> convert = null)`, `MoveEntry<T>(entry, oldFile, oldSection, oldKey)` (WW->WW, runs until the new entry exists).

`ModuleRegistry` (Core/Boot): the only file that names module types; order = init order = Harmony subscriber order: Debug, Compatibility(early part: detection only), Saves, Presentation, NPCAI, Gunplay, HUD, Vehicles, Patrols, POI, Survival, Loot, Quests, Tools, Developer. (Compatibility bridges that need other providers resolve in `OnWorldReady` or on `ModApi.Changed`.)

## 6. Persistence helpers

```csharp
public static class SaveBus   // existing watcher kept
{
    // events (typed, ordered): LoadBegin(slot) -> WorldReady -> SaveBegin(slot) -> BeforeSerialize -> AfterSerialize -> SaveEnd(slot)
    public static SaveKey Key(string es3Key);                   // Write(string)/Read()/Delete(); cache + StoreCachedFile + verify, after native flush
    public static void ReportFailure(IModule owner, string msg); // explicit save failure channel (replaces Log.Error side effect)
}
public static class Sidecars
{
    /// <summary>Declares a per-slot companion file pattern so Saves includes it in transactions/backups/copies.
    /// pattern is relative to config/WildWasteland, e.g. "CleanHUD/Saves/{slot}.fog.bin".</summary>
    public static void Register(IModule owner, string pattern, bool legacyReadOnly = false);
    public static string PathFor(IModule owner, string pattern, string slot);
}
public static class SaveTypes  // existing, extended
{
    public static void Alias(string oldAssembly, string oldNamespace, string newNamespace); // existing
    /// <summary>Same-assembly rename for types moved inside WildWasteland.dll (saves written by WW 1.x).</summary>
    public static void Moved(string oldFullName, string newFullName);
    public static void MovedNamespace(string oldNamespacePrefix, string newNamespacePrefix);
}
```

## 7. Settings shell (replaces module references in CorePages)

```csharp
public sealed class SettingsShell
{
    public enum Page { Player, World, Hud, System, Debug, Developer }
    /// <summary>Adds a bool/choice/slider row bound to a module ConfigEntry, in a named group, with sort order.</summary>
    public void Row(IModule owner, Page page, string group, ConfigEntryBase entry, string label, int order = 0);
    /// <summary>Adds a custom-drawn row (previews like crosshair/body selection).</summary>
    public void Custom(IModule owner, Page page, string group, Action<ToolLayout> draw, int order = 0);
    /// <summary>Entries RESET on that page restores (owner modules decide which of their entries reset).</summary>
    public void Resettable(IModule owner, Page page, ConfigEntryBase entry);
}
```

## 8. Contract catalog (all in `Core/ModApi/Contracts/<Domain>.cs`)
Existing interfaces are kept and moved (names unchanged unless noted) so Wave A compiles; W0-A adds the new ones as stubs.

| domain file | contracts | provider | main consumers |
|---|---|---|---|
| Presentation.cs | `ICharacterPresentation` (attach/detach character by EntityId, inputs struct, action, weapon changed, `TryGetMuzzle`, `TryGetHeldProp`), `ICharacterAppearance` (ex `INpcAppearance`+`ICharacterAssets`: Dress(id, identity, randomize), TryPaths, variant roll), `ITraversal` (start/stop lease, ladder occupancy, player ladder state), `ISeatPoses` (seat/release/animate by seat kind), `IWeaponPose` (lower weight, ADS weight), `IVehicleSkins` (paint by faction/loot key), `ICameraRig` (view mode, PlayerView query, aim ray), `IPoseAuthoring` (Developer editors) | Presentation | NPCAI, Patrols, Quests, Gunplay, HUD, Developer, Compatibility |
| Npc.cs | `INpcApi` (existing + `SetSeated`, `SetFollowerDriven`, `Frighten`, `CanPerceivePlayer(from, fwd, profile)`, `Remember`, `Travel`), `INavApi` (Structures, overlays, waypoint providers, `ForgetStructure`, `BakeIsolated`, template access, `WorldGenerationSuspended`), `IFollowers` (Follow/Dismiss/Mount), `ISeatedCombat`, `IPopulation` (register interior groups, catalogs) | NPCAI | Patrols, POI, Quests, HUD, Tools, Compatibility |
| Weapons.cs | `IWeaponCatalog` (kind, caliber, base stats by weapon key), `IWeaponStats` (`float Get(int weaponKey, WeaponStat stat)`; `RegisterModifier(IWeaponStatModifier, order)` for attachments), `IWeaponState` (cocking, ammo in gun, ADS), `IItemSpawner` lives in Game.cs | Gunplay | HUD, NPCAI, Presentation, Compatibility, Quests |
| Hud.cs | `IMapMarkers` (existing + label/colour/size), `ICompassMarkers`, `ILocationMarkers` (exclude root, icon), `INotifications` (Push), `IQuestPresentation` (existing), `IUiTheme` (in Core/UI, provider Core) | HUD | Patrols, POI, Quests, Saves, Compatibility, Gunplay |
| Vehicles.cs | `IVehicleFactory` (register kind, build from TemplateFile, cache), `IVehicleIgnition` (existing), `IVehicleControls` (cockpit state), `IVehicleOwnership` (is module/third-party driven), `IDrivingTelemetry` (Compat ChaseCamera provides) | Vehicles (+Compatibility for ignition/telemetry) | Patrols, POI, HUD, Core Optimization, Tools |
| Patrols.cs | `IPatrols` (IsCrew, IsSeated, VehicleCombatActive, JourneyOf, PatrolCount), `IVehicleRouting`, `IRouteHintProvider` (registered by POI), `ICampStatus` (existing; moves to POI `IWorldLocations` in Wave B), `ICullingProtection`, `IFollowerVehicles` (existing + Release) | Patrols | Core Optimization, Quests, HUD, POI, Saves |
| World.cs | `IPoiApi` (existing), `ITownsApi` (outline, gates, street graph, parking slots, roles/conditions, templates), `IWorldLocations` (camps/POIs registry + state) | POI | Patrols, NPCAI, Quests, HUD, Tools, Loot |
| Survival.cs | `IWeatherRegions` (existing + `StormExposure(pos)`, `IsStormRoot`), `IShelterRegistry`, `IDayLight`, `IWeatherVisualPolicy` (provider Core Optimization) | Survival | NPCAI, Vehicles, HUD, Compatibility |
| Loot.cs | `ILootTables` (tables, assignment by POI type/town lot), `ILootSpawning` (spawner budget/progress, substitution rules) | Loot | POI, Patrols, Survival, Quests, Tools |
| Quests.cs | `IQuestApi`, `IQuestConversation` (existing, from QuestServices.cs) | Quests | HUD, Compatibility |
| Saves.cs | `ISaves` (Busy, AutosaveDueIn, ActiveFile, presentation state), events below | Saves | HUD, Patrols, POI, Compatibility |
| Game.cs (Core-provided, static) | `GameRefs`, `PlayerState`, `WorldIndex`, `WorldTiles`, `FsmIndex`, `FsmPause`, `PrefabCatalog`, `ItemSpawner`, `Factions`, `Clock`, `Names`, `Layers`, `CameraArbiter`, `InventorySupplies` queries | Core | all |

## 9. Event catalog (all in `Core/ModApi/Events/<Domain>Events.cs`)
[B] = `[Broadcast]` (authority in brackets), L = local only. Payload fields listed are the minimum.

| domain | event | payload | publisher -> consumers |
|---|---|---|---|
| Core | `ModuleStateChanged` L | moduleId, old/new state, error | ModuleHost -> Debug, HUD |
| Core | `OriginShifted` L | delta, totalOffset | Origin -> all |
| Core | `TileApplied` / `TileFinalized` / `TileMoved` / `TerrainEdited` L | tile x,z, world rect, draft/main | WorldTiles (+Compat Vistas) -> POI, Patrols, HUD, Quests, Optimization |
| Core | `HotkeyPressed` L | key name, owner id | Hotkeys -> (MP input replay later) |
| Core | `ItemCulled` [Host] | item id, world pos, reason | Optimization Culler, Saves cleanup -> Loot |
| Core | `VehicleSimulationFrozen/Unfrozen` [Host] | vehicle id, world pos | Optimization CarLod -> Vehicles, Patrols |
| Core | `SupplyDropped` [Owner] | kind, quantity, world pos, new item ids | Fixes InventorySupplies -> HUD |
| Core | `MoneyChanged` [Host] | old, new | GameRefs -> HUD, Quests |
| Saves | `SaveBegin`, `BeforeSerialize`, `AfterSerialize`, `SaveEnd`, `LoadBegin`, `WorldReady` L | slot, generation, kind (manual/auto/exit) | SaveBus/Saves -> all |
| Saves | `SavePresentation` L | phase (begin/progress/end), fraction, text | Saves -> HUD, Compatibility Apocasaver |
| Saves | `CombatActivity` L | actor id, kind, world pos | Gunplay/NPCAI/Patrols -> Saves |
| Presentation | `CharacterAttached/Detached` [Host] | entity, kind (player/human/female/animal), model id | Presentation -> NPCAI, Patrols, Quests |
| Presentation | `CharacterAction` [Owner] | entity, action (punch, throw, reload, climb...), weapon key | NPCAI/Patrols/player input -> Presentation |
| Presentation | `CharacterModelChanged` [Owner] | entity, model id, variant | Presentation -> HUD, Quests |
| Presentation | `TraversalStarted/Ended` [Owner] | entity, kind (ladder/climb/drop/crawl), start/end world pos | Presentation -> NPCAI, HUD |
| Presentation | `SeatPoseChanged` [Owner] | entity, vehicle id, seat | Presentation -> Vehicles, Patrols |
| Presentation | `CameraModeChanged` L | mode (FP/TP/map/binocular/scope) | Presentation camera -> Gunplay, HUD, Quests |
| Presentation | `CharacterVoice` [Owner] | entity, clip id | Presentation -> audio/MP |
| NPCAI | `NpcSpawned/NpcDespawned` [Host] | entity, prefab, faction, world pos, reason | NPCAI -> POI, Quests, Presentation, Survival |
| NPCAI | `NpcStateChanged` [Host] | entity, mode (idle/search/combat/cover/flee/follow...), target entity | NPCAI -> HUD, Saves, Debug |
| NPCAI | `NpcKilled` [Host] | entity, killer, corpse name, world pos, tag | NPCAI -> Quests, Loot, HUD |
| NPCAI | `NoiseEmitted` [Host] | source entity, kind, world pos, radius | NPCAI (+Gunplay) -> NPCAI |
| NPCAI | `NpcFrightened` [Host] | entity, threat entity, seconds, cause | Compatibility WeaponsPack / NPCAI -> NPCAI |
| NPCAI | `NestBuilt/NestFreed/NestAlarm/WebTouched/BurrowChanged/PounceLeap/DogBite` [Host] | entity, world pos, state | NPCAI -> Presentation, HUD |
| NPCAI | `PlayerShout` [Owner] | world pos | Core input -> NPCAI |
| NPCAI | `FollowerHired/Dismissed/Died/Lost/Mounted/Dismounted` [Host] | follower, leader, vehicle | NPCAI/Quests -> Quests, HUD |
| Gunplay | `WeaponFired` [Owner] | shooter, weapon key, origin/dir world, pellets | Gunplay -> NPCAI (hearing), HUD, Saves, Presentation |
| Gunplay | `ProjectileSpawned/Ended` [Owner] | projectile id, kind, origin, velocity | Gunplay -> MP |
| Gunplay | `ProjectileHit` [Host] | projectile id, target entity, part, world point, normal | Gunplay -> HUD, NPCAI, Patrols |
| Gunplay | `Hurt` [Host] | victim, attacker, amount, kind (projectile/melee/explosion/vanilla), bodypart, world point | Gunplay -> HUD, NPCAI, Quests |
| Gunplay | `VehiclePartHit` [Host] (existing, now id-based) | vehicle, part path, attacker, melee/projectile | Gunplay/Patrols -> Patrols, HUD |
| Gunplay | `WheelPoppedOff`, `BoltLanded` [Host] | vehicle/wheel ids; bolt pose | Gunplay -> Vehicles, MP |
| Gunplay | `AmmoChanged/MagazineChanged`, `AdsChanged`, `WeaponSwitchPhase` [Owner] | weapon key, counts / state / phase | Gunplay -> HUD, Presentation |
| Gunplay | `UnloadStarted/Finished`, `AmmoDropped`, `ArmorPlateDropped` [Owner] | weapon key, quantity, item ids | Gunplay -> HUD, Loot |
| Vehicles | `VehicleSpawned/Despawned` [Host] | vehicle, kind, template, world pos/rot, trailer, cargo key, paint key, journey | Vehicles/Patrols -> HUD, Presentation, POI, MP |
| Vehicles | `VehicleExploded` [Host] (existing, id-based) | vehicle, world pos, wreck kind | Patrols/Vehicles -> NPCAI, HUD, Quests |
| Vehicles | `VehicleIgnition` [Owner] | vehicle, on/off, source (player/AI/EngineSim) | Vehicles/Compatibility -> HUD, Patrols |
| Vehicles | `VehicleControlsState` [Owner] | vehicle, cassette, volume, handbrake, headlights | Vehicles -> HUD |
| Vehicles | `TrailerAttached/Detached` [Host] | truck, trailer | Vehicles -> Patrols |
| Patrols | `PatrolGroupRolled`, `JourneyCreated/Departed/Materialized/Virtualized/Ended` [Host] | journey id, source/dest ids, faction, kind flags, templates, vehicle ids, reason | Patrols -> HUD, Quests, POI |
| Patrols | `VehicleDrivingState` [Host] (5-10 Hz) | vehicle, throttle, steer, brake, gear, handbrake, engine | Patrols -> MP |
| Patrols | `CrewSeated/CrewBailedOut/CrewDismounted/CrewReboarded/DriverPromoted` [Host] | vehicle, npc, seat, reason, world pos, velocity | Patrols -> NPCAI, Quests, Presentation |
| Patrols | `RamHit`, `LanceThrown/RiderLeap/RiderDetonated` [Host] | ids, damage, vectors | Patrols -> HUD, Gunplay |
| Patrols | `MerchantVisit/MerchantStockChanged`, `FactionBattleStarted/Ended`, `ReinforcementsCalled`, `TownVisit` [Host] | ids, positions, results | Patrols -> HUD, Quests, POI |
| POI | `CampStateChanged/CampCleared`, `CampOwnershipChanged` [Host] | camp id, state, faction, world pos | POI -> Quests, Patrols, HUD |
| POI | `TownPlanned/TownBuilt/TownStreamedOut/TownDiscovered/TownConditionChanged/TownLotRoleChanged` [Host] | town id, template id, faction, outline/gates (on Planned) | POI -> NPCAI, Patrols, HUD, Quests, Tools |
| POI | `PoiSiteGenerated/PoiEncounterSpawned/GasStationPlaced/PoiDressingReady` [Host] | site id, type, world pos | POI -> Patrols, HUD, NPCAI |
| Loot | `LootSpawned` [Host], `SpawnerProgressChanged` [Host], `NaturalLootSubstituted` L | spawner id, table id, item ids | Loot -> POI, Saves, Debug |
| Survival | `StormSpawned/Removed/PhaseChanged` [Host], `StormHazardHit` [Host], `LightningStrike` [Host] | storm id, region, phase, target, world pos | Survival -> HUD, NPCAI, Patrols |
| Survival | `StewPotSpawned/ContentChanged/Drink/Pour` [Host] | pot id, contents, actor | Survival -> HUD, Loot |
| HUD | `LocationDiscovered` [Owner], `FogExplored` [Owner] (chunk deltas), `MapOpened/Closed` L | location key, chunk ids | HUD -> Quests, POI, Saves |
| Quests | `QuestOffered/Accepted/Declined/Progress/Completed/Failed/Cancelled/Abandoned` [Host], `ReputationChanged` [Host], `PlayerHostilityChanged` [Host], `ConversationOpened/ChoiceSelected/Closed` [Owner] | quest id, giver entity, faction, values | Quests -> HUD, NPCAI, POI |

Notes:
- Events replacing today's `Services` events: `CrewBailedOut(GO car, GO pax, GO npc)`, `NpcHurt(GO victim, GO attacker)`, `VehiclePartHit(GO part, GO attacker)`, `VehicleExploded(GO)` keep forwarding during Wave B (old delegates raised from the new publisher) and are removed in Wave C.
- `ProcessEvent` subscribers must keep in mind that Optimization PM5/PM6 skip unhandled TRIGGER/COLLISION/BECAME VISIBLE events (documented in the Hooks summary).
- Simulation-changing Optimization features (Culler, CarLod, CCD, time/sleep, animator culling) are host-only in multiplayer.
