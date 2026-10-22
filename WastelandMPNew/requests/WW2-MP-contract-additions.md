# Proposed Core contracts for later MP integration

These are requests, not edits to frozen W0. All names below are proposals except existing contracts referenced explicitly. Coordinator resolves them between waves or during the later MP integration task.

| Need | Proposed Core capability | Reason |
|---|---|---|
| Session role/readiness/epoch/participants and per-player entities | `IMultiplayerSession` | Existing services must gate host/owner before local mutation; local Player alone cannot represent guests. |
| Owner identity + monotonic revision + handoff events | Extend existing `IVehicleOwnership` or add domain-neutral `IEntityAuthority` | Vehicle module ownership and network simulation ownership are different questions; items/collectors/tools also need authority. |
| Immutable snapshot providers, exact saved/runtime binding, apply proofs | `IWorldReplicationParticipant` + Core entity manifest methods | Event broadcast alone cannot reconstruct a late-joiner's persistent world. `Entities.Of` must not invent guest ids independently. |
| Targeted request/grant/commit with sender/target/epoch and operation id | `IAuthoritativeOperations` | Generic event broadcast cannot reserve shared ammo, food, money, tool edits, seats or fitting atomically. |
| Typed ready-owner save participants and first-failure channel | Extend existing `ISaves`/SaveBus | Actual reports before ack, one deadline and a sticky world failure must integrate into the real transaction. |
| Generation schema fingerprint, world overlays, host weather/clock, terrain revision | `IWorldGenerationCompatibility` | MP must never call Vistas directly; Compatibility binds signatures and exposes capability. |
| Current logical inventory slots and canonical item identity | `IPlayerInventoryState` | Apocapocket UI/held transforms do not describe full native inventory restore. |
| Complete snapshot/apply of optional tool edits and capture barrier | `IToolEditReplication` | Preserve real-save supplements without depending on another module's concrete class. |

Contract/event issues requiring explicit resolution:

- Planned `MoneyChanged [Host]` lists only old/new amounts. Shared quest rewards currently credit each connected player's separate wallet. Add player/wallet identity to an agreed event/operation rather than broadcasting a global balance to everyone.
- `Authority.Owner` is type-level; messages still need entity identity and ownership revision. Core must expose those so `Send<T>` can check the specific payload before sending.
- Planned `PlayerShout` has only world position. Use authenticated envelope speaker identity for voice choice; preserve explicit actor id if host can emit NPC/other-player shouts.
- Define saved ES3 ids versus runtime-assigned id namespaces. Do not assume that an arbitrary guest independently loading the same seed has identical runtime reference ids. Host manifest binding resolves parity.
- All-player perception, weather/terrain interest and cleanup protection need Core participant queries; host-local GameRefs/PlayerState cannot substitute.
- Do not weaken frozen signatures inside MP as a workaround. Record accepted contracts in the current WW2 request/report mechanism when integration begins.
