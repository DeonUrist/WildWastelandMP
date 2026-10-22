# Default saving without ApocalypterMP — Testing

Denis explicitly requested the new save system as the default even without ApocalypterMP. Source proved Multiplayer.Init returned before save-engine installation; WorldStore/GameLauncher/player getters also required the missing assembly. Apocasaver was suppressed before the replacement initialized.

Plugin now installs the common engine after optional bridge discovery. Network chat/host/WorldSaver/ownership delegates bind only with a ready bridge; common save hooks/jobs use Core ownership. Installed and Ready separate hook installation from recovery success. Recovery blocks world writes until resolved and owns idempotent Apocasaver suppression, including later successful Retry. It preserves the managed plugin reference after Unity scene destruction.

OfflineSaveRuntime reuses standard WW's native loader/menu/scene/health/vehicle adapter. The existing ES3 directory supplies the catalog, named loads and Continue. Existing MP_sp-live/save/auto identities, backup roots, metadata/sidecars remain. Working copies are excluded; MP snapshots/MP selection are unavailable without the bridge. The optional launcher applies its root only when present, before recovery.

Native Broadcast/SendEvent saves enter the same offline transaction once. Shared Core MouseLook.DoMouseLook dispatch freezes direct axis reads during serialization; Quests migrated to that shared target without changing its dialogue guard. Network save interception remains when the bridge is present. Save/Load uses the installed library even when recovery needs Retry. The existing engine provides journaled publication, full companions, backup/restore, autosave countdown/combat safety, optional cleanup, world history, hinge/pose compatibility and vehicle-load placement/camera reset. No inventory-drop expansion, packet/handshake/config-key/native-format/version changes; standard WW untouched.

Final Windows Release/Deploy=false build (`bin/Release/offline-saves-final_20261019`) passed zero warnings/errors. Exact stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_offline-saves-final_20261019a.dll`,79,973,376 bytes; built/staged/installed MD5 **A04CBCBB0215A358CBDD4BBCB4989774**. Game closed, disabled WW/WWMP manifests absent, complete existing MP assets present. Only DLL deployed; unrelated pending Apocasetter removals of disabled ChaseCamera/Language/EngineSim preserved. Initial741792AB4850A637E520AC91EC7C4152 stage is superseded.

Version remains0.1.41 as a local test build; no commit/push/release/Index update, game launch/harness or live-config/save edits. Public RC5 does not contain this change. Runtime/save-integrity/failure-recovery/performance remain Testing.

Denis checks:

1. Without ApocalypterMP, restart: expect `[System] Saves ready: native offline and optional host saves, separate manual/autosaves, complete backups and interrupted-write recovery`, with no missing-type/startup errors. Required-dependency notice still concerns host/join.
2. Open styled/native Save/Load (also Clean menus off): expect the library and SYSTEM settings. New world -> named snapshot -> second world -> Continue/named load must retain distinct worlds; legacy native slots remain readable.
3. Check auto/manual backups, overwrite/delete/restore, separate autosaves/countdown/combat deferral and input/camera freeze. Use disposable copies for interrupted writes/recovery Retry; no duplicate Apocasaver takeover.
4. Save seated, reload, expect parked vehicle/safe standing player/stable camera with mouse initially still; check save-and-quit and cargo/quests/NPC/patrol/fog companions.
5. Reinstall Multiplayer and check offline plus host/guest saving/requests/quit/load and three-player ownership. One shared save engine, same handshake24/host authority; preserve distinct roots without importing saves.

Publication follow-up: Denis subsequently authorized push/release as RC6 (0.1.42). See release-0.1.42-RC6.md for final package/build hashes; the local-only status above describes the earlier test build.
