# Step 5 — part condition authority and ordered control messages

**Paused after this step.** Source compiled on Windows with zero warnings/errors. No deployment, game/server launch, commit or push. The full compatibility patch remains unfinished.

## Current base and QuestsCompat retirement

Denis reported that main retired QuestsCompat. Main is now `91bff52450e27a461fdbced48317910058ce5cf2` (RC33 plus the retirement). The isolated worktree carries that change, including its AGENTS/module/patch records, and its detached base now points at that commit. All earlier dirty/untracked MP source was preserved; main was untouched. Plugin registers native Quests only. Its optional Talk integration and party-quest adapter remain with Quests. Do not restore QuestsCompat or apply an older whole-file overlay to current main.

## Proven causes

1. WW HitWorld wrote condition directly on whichever peer simulated the projectile. Fitted-wheel melee sent local Bodypart Damage directly. MP ItemsTick reverts variables during QuietUntil or when that peer cannot send them, and host OnVars rejects changes made by someone other than the remote owner. Those writes could disappear or never reach the owner.
2. The supplied protocol-21 `UdpTransport.Send(peer, data, reliable)` ignores `reliable` and calls socket.Send. Native SendToAll's reliable flag alone therefore provides no retry, duplicate suppression or ordering. A lost part hit, owner result or state update could leave peers disagreeing.
3. Native Switch and Thrown are normally relayed by NetSession.OnDataReceived *after* Handle. An envelope that just calls Handle would lose guest-to-other-guest door/throw propagation unless it preserves that relay and sender.

Evidence is the existing external `inspection/ApocalypterMP.decompiled.cs`: ItemVars.For/Apply/Write/Revert, WorldObjects.ItemsTick/OnVars/FindAttachment/ApplyAttachment, NetSession send methods/OnDataReceived/RelayedTypes, UdpTransport.Send. No further decompilation or runtime simulation was used.

## Changes in source

- **Owner-routed condition changes:** new Core MultiplayerParts queues each part's hits in order. Host validates guest requests and derives WW condition loss/pop policy from host settings. The current owner applies the loss to its live condition (including current wear/repair state), acknowledges the result, and the host publishes that result. Operation receipts suppress duplicate retries. Ownership rejection reroutes with a new operation ID; disconnection waits for native ownership reconciliation. Work/receipts/deferred snapshots have cleanup paths and bounded queues.
- **Reconciliation:** MultiplayerItems adds missing root Condition/Repair condition fields after the original native variable list. Condition mutations also update their corresponding native baselines, so MP's quiet window cannot undo them. A host snapshot barrier filters stale Condition values in native OnVars until that peer has acknowledged the current snapshot, leaving unrelated native variables intact. Deferred snapshots and commands retry when peers/objects become ready.
- **Damage paths:** WW bullet/orb part damage and fitted-wheel melee now use the owner path. Shared ProcessEvent also routes negative native part Bodypart damage, including the path used when WW vehicle-damage conversion is off. Native mode changes the primary Condition without WW conversion/pop rules; offline behavior retains the original code. WW wheel pop-off runs through the existing Guns PopOff path on the applying owner only.
- **Ordered delivery:** new MultiplayerReliable uses type 249, magic/version, per-direction nonce/sequence, a 64-packet window, cumulative ACKs, retries and duplicate/out-of-order buffering. All reliable WW type-250 controls use it; unreliable bullet/pose visuals retain their existing delivery. Selected native item/ownership/car controls, damage, switches, throws, revival, sleep and player save/restore controls share that same ordered stream. The stream survives Joining -> Connected; disconnect/session changes clean it up. Native Switch/Thrown relay keeps the originating player and excludes the original sender.
- **No second door or attachment simulation:** native MP still owns switches and physical attachment replication. This step strengthens their control delivery instead of adding competing FSMs/pose writes.

About 580 added/changed source lines: new `Core/MultiplayerParts.cs`, new `Core/MultiplayerReliable.cs`, Multiplayer/MultiplayerItems/MultiplayerCombat, Guns NetworkShots/Tracers, plus the upstream Plugin retirement. Docs/AGENTS record the discoveries. No new config entries, saved components, prefab IDs or WW ES3 keys. MP's positional variable list is extended by appending; all peers need matching WW source/build. Native bootstrap messages and unlisted native packets retain MP's transport path.

## State audit preserved from native code

| State | Source finding | Status |
|---|---|---|
| Engine condition, oil and ratio | `Sources/fsm/prefabs/5.2L V8 230HP 340Nm Gasoline.md:607` load writes Condition, cap/LiquidAmount.Liquid, transmission/FinalDriveRatio.finalGearRatio and CheckTag.finalGearRatio. Native ItemVars.For already follows these load targets. | Native fields retained; WW condition mutation authority implemented in source. EngineSim and interaction/save races remain open. |
| Attached real blast lance | `blastlance_1.md` Attach parents to CarPart; saveItemVar/load sets CheckBool.Attached and ForBuyInt. Native ItemVars discovers these; MP attachment state carries parent ID/path and locked pose. | Existing native state path retained and its relevant control delivery ordered. Consumption/explosion and save/reconnect still need audit/testing. Rider visual props are not separate saved weapons. |
| Bolted metal plate | `metal_plate_1.md` Attach parents to CarPart and sets vehPart/layer 8; CheckTag derives loose/attached behavior. MP FindAttachment handles a non-hinge parent with a locked local pose. | Native identity/tag/pose ownership retained. Do not invent condition on plates without a native Condition field. Nested/detached persistence still needs audit/testing. |
| Doors, trunks, hoods | Native SwitchPairs include DoorOpen/DoorClose and Closed/Open. ApplySwitch checks current state before sending the native use event; OnSwitchRequest supplies late-join state. Junker DoorOpen rotates its parent hinge. | Native logic retained; guest relay and ordered delivery included. Late join, save/load and simultaneous interaction remain unverified. |

## Patch inventory added/extended in this step

| Target | Owner / kind | Gate and purpose |
|---|---|---|
| MP NetSession.Handle | MultiplayerBridge existing prefix | Unwrap ordered packets or dispatch existing WW/native packets; preserves actual sender. |
| MP NetSession.SendToAll / SendTo / SendToAllExcept | MultiplayerBridge prefixes | Active MP and selected native message IDs only; enqueue on ordered stream. Envelope/unlisted packets go through original. |
| MP ItemVars.For | MultiplayerBridge existing postfix | Append missing part Condition/Repair fields; preserve existing provider extension/order. |
| MP WorldObjects.OnVars | MultiplayerBridge prefix/finalizer | Cache and restore nested sender/object context. |
| MP ItemVars.Apply | MultiplayerBridge prefix | Only the target part with an outstanding peer snapshot barrier; replace stale incoming Condition with current host values. |
| Shared Fsm.ProcessEvent | Existing Core dispatcher subscriber | Active MP, Damage, Bodypart/Health; route part damage and retain client-creature suppression. |

Jobs use Core's scheduler: Part damage authority at 0.1 s; reliable messages at 0.05 s. Native movement/physics packets remain under MP. Private API access uses cached generated delegates, not reflection in packet/frame paths.

## Validation and artifacts

PowerShell: `dotnet build WildWasteland.csproj -c Release -p:Deploy=false -v:minimal` — **0 warnings / 0 errors**, 2026-10-18. Compilation only; network delivery, native Harmony bindings and gameplay have not been exercised.

- DLL MD5: `C688C223E6F853AD903E2D01B018ECD4`.
- SHA256: `08198AD0BFB17D4323C69C789BF06DE85ECAA587DB174ED0ACF01627CB9EFAAD`.
- Non-live DLL: external `WildWasteland_0.1.36_mp_step5_C688C223.dll`.
- Full source checkpoint: external `rc33-step5/` with source-overlay, `RC33-step5-source-overlay.zip`, FILES.txt and tracked.patch. Overlay includes untracked files; tracked.patch does not. Apply the overlay to clean base `91bff52450e27a461fdbced48317910058ce5cf2`; do not apply both patch and overlay.
- Earlier checkpoints target the old base and are historical. Do not use them as the current main overlay.

## Later manual scenarios

1. Host and guest each shoot an engine/wheel on host-owned and guest-owned cars from beyond 3 m. All peers should converge on one configured condition loss per hit. Repeat within the three-second native quiet window, with shotgun penetration, and with WW vehicle-damage conversion off.
2. Slash a fitted wheel; at zero it should detach once on its owner and appear loose everywhere. Transfer the driver's seat while hits are pending, then disconnect/reconnect. No repeat hit, condition reset or duplicate wheel should appear.
3. Run an engine and repair/refill while damaging parts. Confirm native wear/repair/oil/fuel behavior as well as WW damage; note that interaction-authority races are still an open source task below.
4. Attach/remove real blast lances and metal plates. Open/close doors, trunk and hood from each guest. Another guest and late joiner should see the same attachment and switch state. Repeat native throws, revive and sleep voting.
5. Later UDP and Steam sessions must use this exact WW build on all peers. Rejoin and perform the above actions; enable host VerboseLog for `[Multiplayer] Part damage settled: <part>, operation <n>, owner <peer>`. Queue-full/reliable-message failures must be investigated rather than treated as success. No network fault injection/harness was run here.
6. Finally save/quit/load on host, reconnect and load offline. These persistence scenarios are required acceptance gates, not completed verification.

## Outstanding work / next important step

- **Save/load durability:** pending part operations are runtime state. They are not yet drained/fenced with the native host save transaction. A save before the owner result arrives can save older state. Implement transaction/lifecycle coordination before claiming full persistence; no new save identity/key is needed merely for a queued hit.
- **Interaction authority:** native nearby-client repair/refill edits can race an authoritative snapshot. Audit repair/tool costs, fuel/oil/coolant, native equipment/consumption and EngineSim ownership rather than assuming condition damage alone completes them. Mode-2 guest range currently falls back to 4 m when Services.Guns is absent; derive native ranged reach for the Guns-disabled case. Client module-master policy must keep world-side owner handlers consistent; wheel pop callback depends on Guns running there.
- **Handshake/fail-closed behavior:** the broader bridge still falls back to offline on binding failure. Require compatible WW capabilities/role/readiness so no guest simulates a world after a failed bridge. Native Hello/Welcome/WorldInfo bootstrap delivery still uses the supplied MP path and needs a UDP lifecycle audit; the new channel covers established control streams.
- **All part persistence:** finish source review of attach/detach/consume/explode and save-time client ownership, including native offline reload, before marking PART-STATE-CHECKLIST complete. Keep native field/ID registration and retired QuestsCompat intact.
- Custom monster orb/rider explosion presentation/damage, bolt attachments, per-gun/player survival persistence, host weapon/trajectory validation, broader settings/EngineSim/weather lifecycle items remain in RESUME.md.

Resume only when Denis asks. Complete one important step, build Deploy=false, document and pause. Do not deploy or launch the planned headless test yet.
