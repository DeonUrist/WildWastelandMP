# Independent protocol and runtime contract

This is **WW2 schema 1**, with independent magic, metadata and message registration. It is not native protocol21, WWMP handshake27/28, outer type250 or the WWMP extension-kind registry. Never reuse those numbers to imply interoperability.

## Envelope

All integers are unsigned little-endian. Sender identity comes from the provisioned link or SDK connection, never a field a guest can impersonate.

| Offset | Width | Field |
|---|---|---|
| 0 | 4 | Magic `0x324E5757` |
| 4 | 2 | Envelope schema `1` |
| 6 | 2 | Explicit registered message kind; 0 reserved for receipts |
| 8 | 8 | Fresh negotiated nonzero connection/world epoch |
| 16 | 8 | Per-direction/per-stream nonzero sequence |
| 24 | 1 | Control=0, World=1, Combat=2, Presentation=3 |
| 25 | 1 | 0=data, 1=receipt; no unknown flags |
| 26 | 2 | Exact payload length |
| 28 | <=1172 | Domain payload, with explicit codec/version and bounded fields |

Datagram cap1200 bytes. A UDP MAC adds32 bytes, yielding1232 bytes before UDP/IP headers. UDP uses a unique random32-byte key for **each peer pair and session**, derived separately for host-to-guest and guest-to-host MACs. Endpoint provisioning/key exchange is external and required. MAC authenticates bytes; it provides no confidentiality or persisted player-account ownership. Discard old keys on teardown. The caller owns and clears its original key buffer. Do not put credentials in save files, host settings, command lines or logs.

Steam uses the game-provided Steamworks.NET runtime, own metadata keys `ww2mp.schema`, `ww2mp.host`, P2P virtual port27 and the SDK-authenticated Steam user id. The shell checks Steam readiness and owns the process-global callback pump. This adapter never initializes or shuts down Steam. Lobby metadata advertises only coarse schema; full game/mod/settings admission still occurs on the established connection. A lobby owner change ends the game connection instead of silently promoting another host.

## Channel behavior

Control/world/combat each have32 retained outgoing and32 reorder slots by default. No per-packet streams, reflection or collection construction. Selective receipts acknowledge retained packets; the cumulative contiguous send floor gates producer capacity. Out-of-window input is rejected before acknowledgement. Old duplicates are acknowledged again without application. Packet send failure retains the slot and retries; producer queue failure is backpressure and must never be interpreted as success.

`OrderedLane.Drain` performs ordered application. A decoder/application exception poisons that lane, and `PeerChannel` closes the link. The shell must revoke admission and decide whether authoritative state needs a reload fence. Do not catch an exception and continue applying a half-mutated operation. Receipt proves bytes were retained, **not successful application or disk durability**.

Combat carries committed hurt/revive/restore transitions in their required order. Death/inventory/snapshot cross-stream dependencies require explicit operation revisions/fences; do not rely on ordering between lanes. A large join must not sit ahead of combat in one FIFO.

Presentation is a **complete superseding state bundle for that peer**, such as one avatar's motion/equipment visual state. `LatestLane` discards older sequences globally for that stream. Do not send different entities, unrelated poses, one-shot impacts or audio events as independent messages on that one latest-only stream: a newer message would incorrectly supersede a different older one. Add an explicit domain/entity stream key and replay window before expanding that use. One-shot disposable effects need their own deduplication/expiry codec.

All methods run on the Core main-thread scheduler. Buffers passed to decoders are borrowed for the callback only. No callback may retain them, publish them asynchronously or mutate the channel. Copy only when transferring ownership to a bounded job. No background Unity calls.

## Admission and launch

The shell implements a separate bounded bootstrap exchange before `PeerChannel` construction; no initial epoch0 frame is accepted. Steam already authenticates its connection; UDP already has trusted per-peer key provisioning. Bootstrap checks nonce/challenge, game build, required content hashes, codec schema and generation settings, negotiates a fresh epoch, then constructs channel state. There is no implemented bootstrap wire codec in this foundation; do not improvise it by accepting arbitrary data as a Welcome.

```
selected save pinned -> launch host world -> actual world ready -> listener/lobby
authenticated link -> compatible content -> host settings installed before guest launch
 -> complete snapshot applied -> safe placement -> host accepts final placement -> Ready
```

Each attempt has its own token. Expired/disconnected attempts cannot update the next attempt. Milestone methods are called after actual provider work and verification, never because a guest sent a boolean. Host receives position consistent with granted safe placement before declaring Ready. Require the final ordered snapshot receipt plus a separate digest/apply proof. Allocate no new ready-player avatar from stale delayed packets.

`CompatibilityStamp` expects canonical32-byte SHA256 digests. Sort ids/keys ordinally and encode counts, lengths and values unambiguously with UTF8 before hashing. Include actual required mod content, schema ids and world generation settings. Do not hash local UI/audio preferences or credentials. Exact hash equality detects mismatch; it does not attest that an untrusted client executes honest code.

## Domain validation

Only explicitly registered codecs cross the wire. A `[Broadcast]` annotation is eligibility, not authorization. The Core transport adapter caches codecs at registration and handles `Authority.Local` as never sent, Host as host-only and Owner as current host-assigned entity owner. Remote events call `ModEvents.ApplyRemote`, never `Publish`, to avoid echo. Request/intents and committed state/result messages are distinct kinds.

Decoders validate exact length, bounded string/count/decompression sizes, finite values, entity existence, world generation, current ownership revision, operation id, direction, request rate and geometric plausibility. `MessageRouter` supplies an admission/host/owner gate, not these game-specific checks. The entity/revision passed to it must be decoded from the **same validated payload** that the callback applies. Never authorize entityA and then read entityB inside the callback.

Use one `OperationJournal` per authenticated participant per epoch. Bind each reserved id to immutable target/request data in the domain coordinator and reject retries that reuse the id with different data. Journal capacity exhaustion stops new grants; completed ids must not be evicted while a retry could still be valid. Persist committed world effects through Saves; an in-memory request journal does not provide exactly-once execution across crashes/reloads.

## Budgets and failure policy

- PeerChannel defaults: at most16 received datagrams per pump and4 packets per lane phase; cooperative0.5ms budget between phases. SDK polling separately returns at most64 messages and fixed32 packets per Steam peer ring. A full/malformed receive ring marks the Steam session failed; the shell must close it.
- Channel buffers cost roughly228KiB per peer before object overhead; a Steam ring adds about38KiB. Do not accept an unbounded peer count. Admission defaults16; Steam capacity includes host.
- Snapshot assembler defaults4MiB **per transfer**. Coordinator must also enforce a global cap (proposed8MiB, two active assemblies), transfer expiry, provider identity and a fair send schedule.
- `PeerChannel` checks time between phases; one codec/callback/SDK call cannot be preempted. Global host peer fairness, control/combat reservation, bandwidth congestion/rate limiting, RTT-based retry and disconnect/heartbeat policy remain shell work. The current fixed250ms retransmit is a foundation, not an internet congestion controller.
- UDP MAC hashing allocates a small digest per call on this runtime. Snapshot hashing allocates a digest and has an indivisible final pass. Do not claim allocation-free networking or guaranteed frame-time limits from the fixed arrays alone.
- Save synchronization shares one30s deadline across owner and optional work. Timeout permits only confirmed host state and emits phase/elapsed/missing-peer diagnostics. It cannot clear `SaveBarrier.Failure`, bypass missing terrain, hide an actual commit/write failure or claim pending work was saved.
- Steam callbacks have a45s lobby request bound; actual connection/admission/heartbeat timeouts belong to the coordinator. No automatic migration or reconnect claiming by display name.
