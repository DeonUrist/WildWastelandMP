# Player recovery checkpoints — Testing (2026-10-18)

Denis requested lightweight five-minute inventory/health/armor protection for host and guests, plus player snapshots that follow a world rollback, including players who have already left. His explicit clarification is: **every world reload restores its matching player snapshot**. Newer player checkpoints must never be applied over a loaded world snapshot, even after a host crash.

## Behavior

2026-10-19 follow-up: sleeping bodies, separate preserved explicit respawn anchors, Secondary Use selection, indefinite revive window and cleanup protection are implemented in [SLEEPERS-RESPAWN.md](SLEEPERS-RESPAWN.md). Moved sleeper locations update PlayerRecord.Position without overwriting the selected b.* respawn anchor; rejoin restores both roles independently. The world-snapshot rollback policy below remains in force. Latest handshake is 16; old revision/build evidence later in this document is historical.

- Every 300 seconds of real time while hosting a ready world, request current guest reports through the existing ordered transport. Flush only carried-item variables, not the world or all tracked objects. Collect for up to 15 seconds without freezing gameplay; retain the last validated record for an owner who does not answer. Host state is captured locally. Existing native five-second player reports continue to keep live records newer than the disk interval.
- Atomically replace `<world>.player-checkpoint.txt` beside the world save on a worker thread, retaining `.bak`. The file contains the most recent player records, item variables, timestamp, world seed/snapshot identity and dirty-exit markers. Only immutable strings reach the worker; all Unity reads stay on the game thread. No world-save event, loading screen or pause is started by a player checkpoint. Capture/serialization still has a small main-thread cost; performance has not been measured.
- Clean menu DISCONNECT, client quit-to-menu and normal application quit send a final inventory/vitals report and wait for the host acknowledgment. A timeout leaves recovery available and logs a warning. Forced termination or connection loss uses the last server-validated state, potentially newer than the five-minute checkpoint. Native `OnPeerLeft` captures/detaches those items and the new bridge checkpoints the resulting offline record. Native inventory recreation remains the single restore path.
- Rejoining after an unclean disconnect shows a top-left CleanHUD notice after native restoration and loading complete. Missing inventory objects at the native restore timeout produce an incomplete-recovery warning instead of claiming full recovery. Notices use the existing native Chat.Notice → MultiplayerNotifications → CleanHUD MessageHook route and retain native fallback with CleanHUD disabled.
- Every full host save embeds **all known player records**, including offline players and a separately captured host record, under ES3 key `WildWastelandMP.PlayerSnapshot`. The existing frozen owner-flush barrier supplies the final connected states. Embed the snapshot in the same ES3 cache **before** allowing the native SaveGame broadcast, so the native world's first disk flush already contains its matching player snapshot. An aborted/dead-host save that never broadcasts SaveGame does not advance the snapshot.
- Hosting a reloaded world replaces the native registry from the embedded snapshot, ignoring newer registry/checkpoint contents for player state. Players who joined only after that snapshot are new to that restored world. Host gear and vitals/armor are restored by the native world loader; never recreate gear or reapply an old host record when hosting starts in an already-running world. Guests use normal inventory recreation when they join. New-seed reuse of a slot cannot import that slot's previous embedded player records. Stopping/rehosting the same loaded world without reloading retains its current records.
- A host crash followed by a reload rolls **everyone** back to the full world save, per Denis's clarification, and displays a corresponding host notice. Five-minute post-save progress does not survive that rollback. Normal host closure retains final player records and a normal application quit allows up to three seconds for the checkpoint writer to finish.

Inventory coverage follows the existing ApocalypterMP native loadout: three carried weapon slots and native quick-item/equipment slots, plus their saved item variables. Ammo pools, health, needs, money/quest receipts and bed data retain the existing native/WW report path. This change adds armor points and armor/backpack capacities, which native PlayerVitals omitted. Extra inventories supplied by other standalone mods are not added to the native loadout by this change.

Existing `.players.txt` files and native save keys/formats remain untouched. **Legacy world saves have no historical embedded snapshot**: retain their existing registry on first use and log that a full world save is required to establish rollback protection. There is no way to reconstruct an old matching registry from a world file that never stored one. Snapshot read/format failures cancel hosting rather than starting with mixed player generations.

Disconnect follow-up (2026-10-18): native `NetSession.OnPeerDisconnected` called `WorldSaver.Request()` immediately after `WorldObjects.OnPeerLeft`, causing a real full-world save and its shared saving screen. Denis requested only a connection/disconnection notice. MultiplayerSaving now removes that one parameterless request call from the disconnect handler, retaining native item capture/detach, the recovery checkpoint, avatar removal and peer broadcast. Notices read `X connected` / `X disconnected` through the existing notification route. Scheduled autosaves, manual saves and host save-and-quit retain their full save presentation. No config/protocol/save-format changes.

Manual check: with host and two guests, connect/cleanly disconnect one guest, then repeat with connection loss. Host and remaining guest should receive one named notice without a disconnect-triggered save or pause; the departing player's checkpoint should still update and rejoining should restore its validated state. An independently due autosave may still save normally. Check manual save and host save-and-quit separately, plus native notice fallback with CleanHUD disabled. Runtime verification belongs to Denis.

## Files and protocol

Six source files, roughly 490 added/changed lines:

- `src/Core/MultiplayerRecovery.cs` (225 new lines): lifecycle, armor reports, owner checkpoint requests, clean exit acknowledgments and recovery notices.
- `src/Core/MultiplayerRecoveryStorage.cs` (228 new lines): native-compatible record serialization, embedded world snapshots, reload selection and atomic background writes.
- `src/Core/MultiplayerItems.cs`: carried-inventory-only variable flush, including pending native item adoption.
- `src/Core/MultiplayerSaving.cs`: prepare snapshot before native world serialization and observe that a world save actually ran.
- `src/Core/Multiplayer.cs`: install the recovery bridge and defer menu disconnect until final player reporting.
- `src/Core/MultiplayerHandshake.cs`: revision **7**, requiring matching peers. New WW packet kinds 143–145 use the existing reliable ordered stream. No new configuration entries or base-repository changes.

## Build and deployment

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true` succeeded with **0 warnings / 0 errors**. Both game copies were closed. Disabled WW manifests were absent from the game/BepInEx cache locations checked before deployment.

Built/installed/unique stage MD5: `94E8A85C9DD8527DFA327A3AED91052B`.

Stage: `bin/Release/WildWastelandMP_player-recovery_20261018_183223.dll`.

No game launch, test harness, runtime automation, commit, push or base deployment. Runtime correctness, visual notices, packet-loss/timeout handling, extra-mod loadouts and performance remain unverified; leave Testing.

## Denis's manual checks

1. Use matching revision-7 peers. Host a test world and make one full world save to establish its embedded snapshot. Equip distinct weapons/gear on each player; record ammo counts, loaded rounds, health and partially depleted armor. Expect startup `Player recovery ready (5-minute checkpoints; world reload restores the saved snapshot)`.
2. Play for over five minutes, including driving and opening local menus. Expect no new pause/loading screen from the checkpoint. Expect `Player checkpoint written (player data only; no world save or pause)` and the checkpoint file timestamp to advance. Existing full autosaves are independent.
3. Force-close only the client manually; leave the host world running. Rejoin with the same player identity. Expect its last validated gear/ammo/health/armor, without duplicated weapons, and `Unclean disconnect: your latest server player checkpoint was restored.` in the top left. Repeat with a normal DISCONNECT/quit-to-menu; expect the final state and no unclean-disconnect warning. Changing display NAME must not change save identity.
4. Include a player who disconnects before the host saves. Make a full save and back up that `.es3` as A. Rejoin/change both players' inventory/vitals, make a newer save B and progress further. Close the sessions, restore A's `.es3` to its original slot, then reload/host it. Keep the newer `.players.txt`/checkpoint beside it. Expect host **and** the returning/offline guest to match A, not B or the latest checkpoint. Expect `Player recovery: world snapshot restored for ... known players (including offline players)` and each joining player's snapshot notice.
5. After another full save, change both players again and wait for a player checkpoint. Force-close the host manually, then reload that world. Expect everyone to return to the full save's states, discarding post-save progress as requested, with the host's unclean-exit/snapshot notice. Check that the host's gear was loaded once and guest gear recreated once.
6. Stop/rehost without reloading: current player states should remain. Start hosting after playing solo in the loaded world: current host gear/vitals must remain. Try a new seed using a previously saved native slot: old embedded player records must not be imported. Regression-check ordinary full saves, save-and-quit, loading screens, client save requests, death/respawn, ammo/gear use, and CleanHUD-off fallback notices.

Launcher save folders: host `E:\SteamLibrary\steamapps\common\Apocalypter\MP\Saves\host`, client `MP\Saves\client`. The authoritative checkpoint lives in the host folder. Main evidence is `MP\Logs\host.log` / `client.log`, with BepInEx startup in `BepInEx\LogOutput.log`. Confirm installed MD5 before interpreting missing log lines.
