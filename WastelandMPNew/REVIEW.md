# ApocalypterMP implementation review

Reviewed 2026-10-22. Evidence: offline protocol-21 source at `V:/Repos/Apocalypter/_staging/apocapatrol-multiplayer-audit-20261011/mp-source`, current WWMP checkout HEAD `6844d36` plus pre-existing uncommitted Vistas work. [Native inventory](NATIVE-REVIEW-INVENTORY.csv) records all inspected-baseline source hashes; [WWMP inventory](SOURCE-INVENTORY.csv) identifies the compatibility implementation. This is a source review, not evidence that a newer upstream release has the same behavior or that any potential abuse was reproduced.

## Architecture worth retaining as requirements

The native project separates a transport interface, session admission/relay, world-object replication, player state, world storage and UI. `NetSession` coordinates those as static state; `WorldObjects` combines many simulation and replication responsibilities. Steam lobbies locate the host; Steam Networking Sockets carry gameplay. UDP provides direct IP connectivity. Host authority over world spawn/AI is necessary for deterministic ownership even when individual cars/items use peer simulation.

The WW overlay has accumulated essential correctness knowledge that a replacement must preserve: strict mod/schema admission, settings before terrain launch, bounded snapshot assembly, acknowledged paced joins, per-entity authority, owner reports before saves, host-selected persistence identity, and staged sleeper restoration. These are behavioral requirements; copying the native class/packet structure is unnecessary.

## Native findings

Paths below are relative to the inspected external source root, never new code copied into this package.

| Finding | Source evidence | Consequence and replacement decision |
|---|---|---|
| UDP ignores its `reliable` argument | `ApocalypterMP.Net/UdpTransport.cs:99` forwards directly to `SendTo` | A reliable call alone does not provide delivery/order. Implement a bounded channel with explicit acknowledgement; do not infer durability from a send return. |
| Host creates a transport peer for an unknown endpoint on ordinary payload receipt | `UdpTransport.cs:169–206` | Application admission occurs later. Separate authenticated endpoint provisioning from world admission; cap both. New UDP deliberately has no anonymous internet listener or home-made public key exchange. |
| UDP receive/disconnect callbacks can close the socket during Update | `UdpTransport.cs:107–167`, `Dispose:243`; WW `src/Core/MultiplayerTransport.cs` | Native dispatch can reenter stop while the loop still has work. New links have idempotent close; packet reads and gameplay delivery are separate operations. |
| UDP record identity is supplied by the client | `NetSession.cs:789–834`, `LocalKey:1670` uses Steam or Windows username | Names and installation GUIDs avoid accidental collisions but do not authenticate a player. Steam identity must come from the connection. UDP needs explicit trusted per-peer key provisioning; never claim a legacy record by name. |
| Native Hello accepts protocol 21, not the WW feature/config schema | `NetSession.cs:789–834` | Native version alone is insufficient. WW currently adds a manifest/revision layer. New protocol negotiates game build, required content, codec schema, world generation fingerprint and a fresh connection/world epoch. |
| Hosting starts networking before its local world descriptor assignment | `NetSession.cs:431–454` | The WW wrapper supplies the loaded-world guard. Preserve that invariant explicitly in the replacement: select/pin save, launch, verify world-ready, then create listener/lobby. |
| Disconnect triggers a full native world-save request | `NetSession.cs:579–610` | WW deliberately suppresses that request, retains checkpoint/sleeper handling and leaves full saving to host policy. Preserve that separation. |
| Native join sends the whole object catalogue from Welcome | `NetSession.cs:Welcome`, `WorldObjects.SendAllTo` | World transfer needs capacity-aware pacing, immutable generation identity and final applied-state acknowledgement. A Welcome is not a complete join. |
| Steam polling limits a batch to 64, but loops until the poll group empties | `SteamSocketsTransport.cs:Update` | The total frame workload remains unbounded under load. New Steam polling reads one batch of at most 64, queues into fixed buffers and returns. Core supplies the global time budget. |
| Steam allocates an array per incoming native message before domain validation | `SteamSocketsTransport.cs:Update` | Size validation and fixed rings precede gameplay parsing in the new adapter. Always release every returned native message, including exception paths. |
| Native Steam Connected handling does not first require a known outgoing/accepted handle | `SteamSocketsTransport.cs:OnStatusChanged` | Global SDK callbacks must be scoped to this session's listener and expected connection handles. New adapter tracks those handles and rejects unexpected identity transitions. |
| Native lobby host metadata is accepted with owner fallback, without host migration protocol | `SteamLobby.cs:OnEntered` | Lobby ownership is discovery metadata, not transfer of authoritative world state. New adapter requires advertised host == current owner and ends on owner change. No automatic host migration. |
| Native Steam setup can initialize Steam and pump callbacks before SteamManager | `SteamSetup.cs:Ensure/Tick` | Sharing a process-global SDK has ownership constraints. New adapter requires Core to confirm game-owned Steam readiness and never calls SteamAPI.Init, RunCallbacks or Shutdown. |
| Native static session/global object state requires broad teardown | `NetSession.cs:Stop`, `WorldObjects.Reset` | New kernel scopes buffers/revisions to objects owned by one peer/epoch. Core must revoke admission before freeing its avatar caches and gameplay participants. |

## WW overlay: strengths and unfinished areas

The overlay already compensates for many native gaps. `MultiplayerReliable` adds two ordered lanes and fragmentation. `MultiplayerHandshake` adds exact mod-build admission and settings; current dirty source uses revision 28 for Vistas, while released RC18 retained revision 27. `MultiplayerJoin` checks queue space and final receipts. `MultiplayerSaving` applies a single 30-second owner/optional-work budget and retains confirmed host data on timeout. `HostSaveSystem`/save helpers coordinate publication and recovery. These should become normal services in WW2, removing reliance on private native fields and IL patch counts.

Do not carry over outdated early audit conclusions without their later corrections. The [assessment report](reference/wwmp/notes/multiplayer-assessment-fixes-2026-10-20.md) records subsequent improvements to resource expiry, save flushing, seats, caches and traffic. In particular, an abandoned resource lease is normally released while keeping host values; **only a failed authoritative commit remains a reload-required integrity failure** in that implementation.

Known limits remain: arbitrary resource FSMs/pouring/refilling, real Steam session behavior, owner conflict under actual lag, native activation stutter, and broad adversarial validation are not universally verified. The apparent full-health dog one-shot and backward downing placement remain unproven; the reach correction and vehicle melee policy do not establish their cause. Optimization and correctness must be assessed separately.

## Steam API facts checked against primary documentation

Lobbies provide discovery, membership and metadata; they are separate from the actual game connection. See [Steam matchmaking](https://partner.steamgames.com/doc/features/multiplayer/matchmaking). Socket messages have explicit reliability/order semantics and native message ownership/release obligations; see [ISteamNetworkingSockets](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets). Invites and `+connect_lobby` are supported discovery entry points; see [ISteamMatchmaking](https://partner.steamgames.com/doc/api/ISteamMatchmaking). The shell still needs invite/command-line UI integration. The Steam SDK's transport authentication does not authorize a received gameplay command.
