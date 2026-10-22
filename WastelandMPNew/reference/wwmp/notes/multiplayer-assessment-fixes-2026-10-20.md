# Multiplayer assessment fixes (2026-10-20, Testing)

Release checkpoint: the integration and both performance follow-ups below are included in [RC14 / 0.1.50](release-0.1.50-RC14.md), handshake27. The final sections supersede the initial resource-failure policy and open-item list; original build/commit/deployment statements below describe their respective historical checkpoints. Runtime acceptance remains Testing.

Denis requested fixes from Claude's supplied source assessment, explicitly using multiple agents and checking that their changes work together. The starting commit is `c3cc44e` (the previously deployed melee-b changes). Changes are limited to WWMP; native ApocalypterMP source/DLLs, the single-player repository, live configuration and saves remain untouched.

Three agents own networking, saves/lifecycle, and objects/gameplay. The coordinating agent owns player/avatar/input fixes and the combined integration review. Findings are checked against the installed native baseline's decompile at `V:/Repos/Apocalypter/_staging/apocapatrol-multiplayer-audit-20261011/mp-source`. Compilation is not runtime acceptance.

## Integration contracts

- The protocol revision advances from 26 to 27; every peer needs this exact build. Reliable world traffic remains lane 0. Creature/player damage, melee requests/results, Revive and PlayerRestore share lane 1. PlayerDied and PlayerSave remain ordered together on lane 0 so a death clears stale recorded inventory before the next report replaces it.
- Joining covers admission, incremental object transfer through its final acknowledged spawn, and the existing sleeping/new-player placement handoff. Autosaves wait for all these stages. A manual save excludes loading peers from its owner-freeze participants. A loading guest ignores freeze/flush requests.
- An owner-save/checkpoint acknowledgment follows an actual eligible native player report. Placement no longer hides inventory reports; incomplete sleeper restoration still does. A valid guest death immediately empties its old inventory record until a fresh report arrives, preserving dropped world objects.
- Departed identities cannot recreate native avatars or repopulate WWMP motion/equipment/choice caches through delayed packets. Native RemovePlayer clears per-peer reliable/admission/transfer state through the same departure event.
- Bulk join/snapshot producers check reliable queue capacity before sending. Exhaustion fails the affected connection explicitly instead of silently continuing with missing state. UDP socket failures preserve retry jobs. Welcome/settings payloads are fragmented; successful Hello admission compares a compact exact-build hash, requesting the detailed manifest only on mismatch.
- Shared-resource use must be authorized before native code credits the player. Rejecting a negative delta after local consumption alone cannot repair duplicated ammo, money or health.

## Compatibility and limits

One new WWMP setting, `[Multiplayer] Install identity` in the top-level plugin configuration, stores a generated per-install GUID for native non-Steam player record keys. It is local and is never included in host settings synchronization; display names also stay local. Steam identity is unchanged. Existing username-keyed UDP records are not automatically claimed: the old key cannot safely distinguish two installations with the same Windows username. Existing records remain in the save. The GUID prevents accidental collisions; it is not cryptographic peer authentication. Do not copy this identity between installations.

The assessment's broader profiling list (full native object update scans, appearance/effect/party repetition, save hashing/ES3 work, passenger seat arbitration, and remaining native cache cleanup) needs separate measured changes. This pass must not be described as eliminating every performance concern or accepting runtime gameplay.

## Human verification

1. Host plus two guests, all on the same build: join a populated world, including a sleeping guest. Let autosave become due during loading. Existing players must remain responsive; autosave resumes only after placement and object transfer complete. Exercise manual save during a join and confirm a loading guest is not frozen.
2. Die on a guest, then save/leave during respawn. Rejoin and inspect inventory and dropped world objects. Down the host during an autosave countdown; saving must defer. A downed guest leaving must not produce a healthy sleeper at 1 HP.
3. Two guests consume the same resource almost simultaneously; verify the shared amount and both inventories together. Include ammo, bolts, liquids, and host-versus-guest interaction. Repeat around save, disconnect and ownership transfer.
4. Rejoin while carrying a native `MP_` replica. Verify adoption, no duplicate item in hand, and a completed sleeper wake/checkpoint. Disconnect a guest, wait, and check that delayed avatar packets do not restore its marker/body.
5. Verify hostile patrol contact with an on-foot guest, damage to vehicle parts, scaled boss health, guest cave darkness/noise, and downed stationary/crawling bodies from both observers. Existing vehicle occupant protection must still apply.
6. Type camera and ignition bindings in chat and editor text fields. They must not actuate controls; opening chat must not steal editor input. Verify normal controls resume after closing.
7. With a naturally lossy/interrupted UDP connection, check that other peers continue receiving combat/world updates. Check reliable chat, death/profile/trade state, revive ordering and named mod-mismatch rejection. No automated game/harness run is authorized.

## Assessment coverage

All implemented changes remain Testing. Numbers below match Claude's assessment.

| Findings | Implemented behavior / remaining limit |
|---|---|
| 1 | Loading/placing peers stay out of owner-freeze barriers; autosaves wait for admission, object acknowledgment, snapshot and placement readiness. |
| 2 | WWMP and native world-ready callers use the existing cached SaveLoadGame FSM. |
| 3 | One paced catalogue per readiness token, capacity-aware sends, final spawn acknowledgment, and explicit connection failure on queue exhaustion. Native partial inventory restoration waits for all IDs, with a bounded failure. |
| 4 | Whole-state SHA256 change detection, targeted demand transfers, capacity-aware chunks, retryable failed apply. |
| 5 | SocketException containment on native UDP SendTo and RawTo preserves retry jobs. |
| 6–7 | Actual report acknowledgment, death clears stale inventory, optional existing stats entry `ww.respawning` invalidates temporary positions, real downed/respawn save and sleeper gates. |
| 8 | Placement exception cleanup, 90-second placement/handoff bound, native respawn iterator finalizer. |
| 9 | Negative resulting deltas are rejected. WW177–179 authorize native Ammo/UseItem, Batteries/UseItem, Nuts/UseItem, Food/Eat and Drink/DrinkON before player credit. Drink retains the reservation until off. Other resource FSM/event names, pouring/refilling and arbitrary third-party operations are **not covered** by these leases; the broad generic-resource finding is therefore only partially addressed. |
| 10 | Already fixed in starting commit c3cc44e; retained host-only forwarded PlayerDamage on guests. |
| 11 | Host ObjectGone requires actual owner, or unowned ordinary item within interaction reach of a valid guest; rejected deletion re-sends spawn. This retains the native near-item interaction policy, rather than claiming authenticated authority over all client actions. |
| 12 | Normalize MP_ prefab metadata and remove untracked old loose hand copies before restoration, excluding pending adoption. |
| 13 | Pending creates have a 10-second failure path; incomplete adoption aborts the guest handoff with its saved state retained, never pretending the queue drained. This bounds failure, rather than repairing every native creation rejection. |
| 14 | Part hit interception falls back to native damage when no valid queued operation exists. |
| 15 | Existing Ram tick checks overlap/swept geometry against on-foot guest capsules. Existing collision ignores and occupant policy remain; large origin/placement jumps do not sweep impacts. |
| 16 | Boss cap observes the actual native spawn scaling factor; elevated restored health is retained when the original factor is unavailable. |
| 17 | Cave noise classification uses source position; guest target darkness uses a per-target ambient cache. |
| 18 | Avatar state/events/profiles require an admitted identity; stale roots hide after 5 seconds and recover on fresh snapshots. Per-peer caches clear on departure. |
| 19 | A downed remote without a usable crawl frame gets a paused humanoid crawl rest graph, disposed on normal/motion playback or destruction. |
| 20 | Camera/cockpit polls respect chat/editor input; native Chat.Tick closes/stands down while editor input is owned. |
| 21 | Chat/notice/death/profile/trade/creature effects now enter reliable wrapping, with native relay membership and chat throttling retained. |
| 22 | Compact exact-manifest hash, bounded full-manifest bootstrap on mismatch, reliable 900-byte payload fragmentation for settings and other oversized wrapped packets. |
| 23 | Persistent local GUID avoids username collisions, with Steam unchanged and no ambiguous legacy-record auto-claim; this is not client authentication. |
| 24 | Patrols registers AfterSave offline too, and uses existing AtomicFile publication. Recovery checkpoint publication uses it as well. |
| 25 | Revive and PlayerRestore join damage on the combat lane. |

The secondary performance list is not a completed optimization project. The cache/filter/atomic-publication/departure corrections above are included; native OnClientHit/ClearTheWay and broad object scans, repeated effect/appearance/party traffic, save hashing/ES3 duplication, seat arbitration and the other listed native cache/security concerns remain open.

## Resource failure recovery

A granted operation is not expired and re-granted after a lost result. If its participant disconnects or the result cannot be committed, saving and checkpoints are withheld, the last files remain intact, and the host must return to the title without saving and reload. The save UI exposes that route and the failure reason. A persistent ReloadRequired fence survives stopping/rehosting the same live world; ending just the session cannot resolve an uncertain credit. No new resource save key, sidecar or saved component is introduced.

Two cross-reviews caught integration failures before staging: a resource request arriving after freeze could stall the owner flush (now rejected before queuing), and connection overflow cleanup could mutate reliable collections mid-traversal (now deferred). Other corrections preserve complete restoration during a slow join, retry snapshots after an apply failure, keep install identity out of host settings, and explicitly publish consumed-item removal before the next grant.

## Build and staging

- Final Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=false`: zero warnings, zero errors. `git diff --check` passed.
- Exact stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_assessment_20261020a.dll`, 80,036,864 bytes.
- Built/staged MD5: `999FA08542E9213E8F75C67A440B9376`.
- Numeric version remains 0.1.49; handshake is 27. Every peer must use the exact new build.
- Not deployed: the current standard-WW installation has no WildWastelandMP plugin folder/native multiplayer dependency. No incomplete DLL-only installation was created. Game processes were not launched or controlled; live configs and saves were not edited.
- Changes remain uncommitted; no push, tag or release. Runtime, save integrity, appearance and performance remain Testing.

## Integration pass (Claude, same day, after the three-agent build)

Cross-check of the combined tree for contradictory decisions and new failure modes, then the two perf items the first pass left open. Changes on top of the assessment tree:

- **Resource leases are no longer session-fatal.** `sanity_pills` has a root-level `Food/Eat` that decrements Quantity instead of `DestroySelf`; one pill with quantity > 1 tripped "invalid result" → host `ReloadRequired`, saving blocked, every ammo/food/drink/nuts use blocked for all players. A guest disconnecting mid-drink or an authority epoch change did the same. Now: invalid/late/orphaned results, a departed peer, an epoch change and session end *release* the lease (host item value kept, warning logged); Food/Nuts whose root survives are a normal completion; `Continuous && consumed` is treated as consumed. Only a failed host commit (`Edit` false) remains fatal. One-shot leases time out after 120 s, continuous after 300 s; client requests the host never answered drop after 10 s, and a grant arriving after that drop is handed straight back (179 with NaN). `BeforeSendVars` no longer answers false for leased ids: that made native `ItemsTick` **revert** the item's variables every 0.5 s during the lease (infinite water on guest drinks, intermittent free magazines). The ProcessEvent prefix is gated by a session flag so it costs nothing offline.
- **Save barrier bound:** a participant that cannot answer a phase (restore still waiting for objects) is dropped from `Waiting` after 30 s with a warning; its last report is used. Previously everyone stayed frozen until the guest's 90 s restore `_end`.
- **Joining grace:** a peer stuck before its first avatar state stops blocking autosaves after 180 s (tracked from the handshake tick, warned once).
- **Downed health policy unified:** a downed guest's record gets revive health (native `ReviveHealth`, default 30) both when it leaves and when the host saves while it is downed; before, the save path kept health 1 and the player came back beside the host at 1 HP.
- **Respawn follows the live anchor object:** the host's 3 s anchor packet already carried the object's current position; the guest now applies it to its saved point (`_setBed`) and respawns there (within 10 s of a host update) even when its own copy of the object is inactive or not spawned, instead of waiting 30 s and falling back to camp/host.
- **`CachedWorldReady`** falls through to native while the SaveLoadGame FSM is not cached yet (first second after a scene load).
- **Perf:** guest `OnClientHit` is replaced by a prefix using `TrackedAncestor` (native did `FirstOrDefault(IsChildOf)` over every tracked object on every Damage send with a GameObject target); `ClearTheWay`'s `PassageTarget` result is cached per tracked object for 1 s. `MultiplayerActors` manifest replies check `CanQueue` before bursting a rig (queue exhaustion now ends the connection).
- **Melee packet kinds renumbered 160/161 → 180/181.** 160/161 are the sleeper Pose/Remove kinds and 173–176 Ultimate Tools; `Multiplayer.Message` hands one shared reader to every subscriber, so `GuestMelee` consumed 16 bytes of every host Pose packet (pose updates silently dropped on guests). 180–248 were free.
- Fragments are also cleared in the periodic In-sweep.

Still open from the assessment: native `ClientUpdate`/`HasChanged` scans, effect/appearance/party repetition, save hashing/ES3 double write, passenger seat arbitration, native cache leaks. The resource leases cover only Ammo/Batteries/Nuts `UseItem`, Food `Eat` and Drink `DrinkON`.

## Performance follow-up (same day): open items 1–4

- **Per-shot visual packets off the reliable lane.** Impact visuals 129 (host → all) and 130 (guest shot end) are unreliable now; their receive side already dedupes through the bolt windows. Crossbow bolts keep 129 reliable because their launch (51) and attachment (126) are reliable and an impact overtaking the launch would tombstone the bolt on that guest.
- **Guest `ClientUpdate` cost** (`src/Core/MultiplayerPerf.cs`): `FollowTarget` skips the transform write once the 0.1 s glide has settled and the copy already sits on its target (test matches native exactly; origin shifts and new samples fall back to native); `KeepKinematic` remembers bodiless objects for 30 s instead of `GetComponent<Rigidbody>` per frame; `PassengerSideLookedAt` (GameObject.Find + every tracked car, per frame on foot) answers from a 0.1 s cache, rescanning if the cached car despawned.
- **Host `HasChanged` allocations:** the one `GameObject.tag` read is redirected (transpiler) to a `CompareTag` cache, so the 10 Hz scan no longer allocates a string per object; `IsVisible`'s renderer lookup is cached per object on the host (5 s recheck for objects without a renderer; guests keep the native lookup so a swapped renderer is seen immediately).
- **Effect traffic:** the full descriptor (114, prefab name) repeats once a second instead of 10 Hz; moving non-inert effects send a compact pose (kind 182, no prefab string) at 10 Hz and only while the pose changes. The guest's 3 s keepalive is refreshed by both.
- Kind registry after this round: 180/181 melee, 182 effect pose; 183–248 free. Check every `Send(` / `kind ==` literal before adding one: `Multiplayer.Message` shares a single reader across subscribers.
- Build: cloud mcs zero errors; stage `_staging\WildWastelandMP_assessment_20261020c.dll`, md5 `9b2712710b4ffb5c9849ee28aecb6a40`.

## Remaining review items (same day, build d): 5–10

- **Traffic repetition.** `Multiplayer.Send` gained `except` (native `SendToAllExcept`) so host motion relays no longer echo to their origin. Actor appearance packets: the 3 s repeat is unreliable; a peer gets the full set reliably once when it becomes ready (`ClientReady`); guests apply appearances when something changed (or once a second) instead of every frame. Party (kind 3) and transaction (kind 4) packets go out on change or every 5 s, per peer, and immediately for a peer that just became ready.
- **Save path.** `CopyVerified` hashes the source while copying and verifies the target against that hash (two reads per participant instead of five; the backup/publish guarantees are unchanged, only the second independent re-read of the source during Prepare is gone, which inside the save barrier only an external writer could have exercised). The native `WorldSaver.Save` world flush is skipped under a WWMP transaction when the file already exists (transpiler on its iterator: `ES3.StoreCachedFile` → `NativeWorldWrite`); `Complete()` flushes once after the modules' AfterSave writers.
- **Passenger seat arbitration** (`src/Core/MultiplayerSeats.cs`, kinds 183/184): a guest asks the host before sitting; the host grants when no fresh player state says someone sits there, it is not that seat itself, and no grant for that car is younger than 2 s; the guest re-checks on-foot/reach on the grant. The host's own seating checks the same states.
- **Native cache pruning** (`MultiplayerPerf`): every 30 s, id-keyed caches (`creatureLooks`, `passiveCars`, `carMotions`, `carCopyStates`, `carLastSent`) drop ids no longer tracked and Unity-keyed ones (`carParts`, `hornCache`, `keyboardAsShipped`, `hingeCache`, `driveZones`, `GroupScaling.sizes`, `fsmCache`) drop destroyed keys. `PlayerPresence` re-ignores nearby rigidbody colliders every 15 s instead of 3 s (the forget must stay: Unity drops an ignore pair when a collider is toggled). `LocalActions.Tick` is skipped while no Player exists (two `GameObject.Find` per frame in the menus).
- **Trust.** Guest shot impacts (128) must start within 75 m of the shooter's host-side position and end within the reported distance + 15 m of the creature (margins sized for the 0.25–0.7 s presence lag at vehicle speeds); legacy melee replays are capped at 60 m instead of 1000; malformed-packet logs are rate-limited to one line per peer per 5 s (`Multiplayer.Complain`).
- Not done from the minor list: `PauseControl.OnInput` reflection while downed (rare state), native `KeepHandsFree` running in single player (a deliberate native behaviour, not ours to change), per-type caching in `PauseControl`.
- Kind registry: 180/181 melee, 182 effect pose, 183/184 passenger seat; 185–248 free.
- Build d: cloud mcs zero errors; stage `_staging\WildWastelandMP_assessment_20261020d.dll`, md5 `6b75d058f6876a5cb64202c2d6443cef`. Handshake stays 27 (all peers already need the assessment build).
