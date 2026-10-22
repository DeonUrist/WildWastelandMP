# Dropped guns between guests

2026-10-19, **Testing**. Denis confirms guest-to-host and host-to-guest drops are visible; another guest cannot see a guest's dropped gun.

Windows Release/Deploy=false passed with zero warnings/errors. Built/staged MD5 `7A6AF06A20DEDFFFB0AB91B708808066`; unique stage `bin/Release/WildWastelandMP_guest-drops_20261019_032209.dll`. A game is running, so deployment is deferred; installed MD5 remains `80924349D16694490BAB11A47F63BCDD`. The build includes the existing concurrent host-save/crawl/merchant changes without changing them. No game launch, harness, commit, push or release.

The inspected installed ApocalypterMP DLL matches SHA256 `BF0528817B0575A00E7A03F9E520DDACCBEE0322328369CE97351F433633CE2E`. Native `OnClientStates` filters ownership and relays accepted states, but `NetSession.Relay` calls `Broadcast` directly, bypassing WW's SendTo/All/AllExcept reliability hooks. UDP ignores the native reliable flag. Also, `OnRelease` applies the final state and calls `GiveBackToHost`, which captures the new baseline without explicitly publishing it. A stationary item can therefore stop generating changes without the observing guest ever receiving its final visible state. These are source-proven delivery gaps; the available local logs do not contain a three-player drop trace, so runtime resolution remains unverified.

`MultiplayerReliable` now sends reliable, accepted ObjectStates relays through the existing ordered envelope, preserving the original guest's sequence identity and excluding that guest. Moving unreliable updates retain native routing. The receiver accepts this host-supplied relayed state without automatically forwarding raw guest snapshots: ownership filtering still belongs to native OnClientStates. `MultiplayerItems` observes a release only from the recorded owner and, after successful native return to host authority, publishes the captured final state reliably to every client. Host drops retain their existing path. Existing packet layouts, handshake revision 19, configuration, item identity, magazines and saves are unchanged; all players still require identical mod builds.

Native references: `_staging/ww-mp-20261018/inspection/ApocalypterMP.decompiled.cs`, NetSession.Relay/OnDataReceived and WorldObjects.OnClientStates/OnRelease/GiveBackToHost/CollectChange/UpdateOwnedByMe. Native gun drops restore Visibility/colliders, position at ItemDrop and unparent the original item: `Sources/systems/items_weapons.md` and `Sources/fsm/scene1_game/PlayerCameraHolder.md:2497`.

Manual verification requires **one host and two guests**, all restarted with the same DLL:

1. Guest A takes a loaded gun into a slot, draws/holsters it, then drops it. Host and guest B must see the same ground gun, including after it settles. Repeat A/B reversed and with each weapon slot, on level ground where the gun settles quickly.
2. Guest B picks up A's drop, checks loaded rounds, then drops it. All three must converge on one gun; the ground object must disappear when stored and reappear after dropping. Repeat pickup by the host and a host-origin drop to both guests.
3. Drop several guns, wait until settled, then reconnect guest B. Its snapshot must show the same remaining ground guns. Repeat over UDP and Steam.

With existing global VerboseLog enabled, a valid guest release adds `[Multiplayer] Released item state published to all clients: <item>`. This confirms host publication, not receipt/rendering on the other guest. Agents do not launch the game or run harnesses; Denis verifies gameplay and performance.
