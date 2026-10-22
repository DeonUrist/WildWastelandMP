# Implementation boundary

## Compiled source supplied

| Source | Concrete behavior |
|---|---|
| `FrameCodec` | Fixed 28-byte little-endian header; schema, exact length, lane and epoch validation; 1200-byte cap |
| `OrderedLane` | Fixed send/reorder windows, selective receipts, retry, in-order delivery, producer backpressure, poisoned state on failed application |
| `LatestLane` | Disposable complete superseding presentation samples; stale sequence rejection |
| `PeerChannel` | Three reliable streams plus presentation, receive count/work budgets, cached callbacks, idempotent link teardown |
| `AuthenticatedUdpLink` | Nonblocking provisioned endpoint, HMAC-SHA256, distinct directional derived keys, eight-datagram receive bound; no encryption |
| `SteamSession`, `SteamPeerLink` | Friends-only lobby create/join; owner/protocol/member checks; handle-scoped connections; fixed receive rings; SDK resource release and teardown; request timeout |
| `AdmissionBook` | Token-bound milestone progression, cap, expiry and departure revocation |
| `CompatibilityStamp` | Exact canonical game/mod/codec/generation digest comparison |
| `OwnershipBook` | Host-assigned monotonic ownership revisions and entity cap |
| `MessageRouter` | Explicit schema registration, admitted host/owner validation, bounded payload dispatch |
| `SnapshotAssembler` | Chunk shape/token/digest/deadline validation; conflicting duplicate rejection; one-time ownership transfer of bytes |
| `SessionSettings` | Explicit host-key allowlist and complete atomic overlay, preserving local preferences |
| `OperationJournal` | Per-participant/epoch bounded request id deduplication, terminal decision retention |
| `SaveBarrier` | Fixed ready-owner set, actual-report acknowledgements, one total 30s deadline, sticky first integrity failure |
| `HostLaunchGate` | Exact selected-save/world-ready gate with cancellation tokens |
| `VistasPolicy`, `WorldPoint` | Pure ownership/heat/randomness rules and finite world-space coordinates |

## Integration work deliberately left to WW2

This package is not the full game multiplayer implementation. The future integration must supply:

1. BepInEx module entry, lifetime/session coordinator, Core scheduler and clock adapter, UI, heartbeat/disconnect/reconnect policy, global fair per-peer budgets and metrics.
2. A bounded bootstrap codec/handshake that exchanges authenticated connection tokens, negotiates the world epoch, installs host settings, validates canonical manifests and launches the guest world. The library primitives do not invent this exchange.
3. Core `IEventTransport` implementation and explicit encode/decode table for the frozen broadcast structs. Do not compile a second copy of Core contracts into this library.
4. Complete world/object/player snapshot producers, snapshot apply participants, durable saved identities and host-only runtime entity allocation; late-join interleaving and deletion tombstones.
5. Native game integration for input, player records, animation, collisions, ownership handoff, inventory, ammo/food/liquid transactions, vehicle parts/fitting/doors/trailers/seats, sleepers, respawn and quests.
6. Saves participant integration and transactional persistence of world + all-player state + optional tool state. This library neither writes ES3 nor replaces existing file publication/recovery logic.
7. Compatibility bridges for Vistas/EngineSim/Ultimate Tools and other optional mods, using Core queries/events only. `VistasPolicy` does not itself hook those mods.
8. Steam invite overlay and invite-accepted/command-line queue, game-owned callback-pump readiness, connection timeouts, shell error UI, and actual two-account Steam verification. `SteamSession.Failure` must trigger teardown; it is not a log-only diagnostic.
9. Usable UDP discovery/listening plus a trusted key-exchange/provisioning flow if public IP hosting is desired. The current UDP link is one fixed, already-provisioned endpoint pair, with unique session key per guest. It is not an anonymous public server, a NAT puncher, an encrypted tunnel, or a persisted account identity system.

Do not mark any of these as complete because the kernel compiles. Do not ship a menu HOST/JOIN button until its corresponding coordinator and world providers are implemented and tested.
