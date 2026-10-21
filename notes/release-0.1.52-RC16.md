# Wild Wasteland MP RC16 (0.1.52) — Save synchronization timeout

Fixes a host save remaining paused indefinitely while owner or mod synchronization is pending. Both owner phases now share a 30-second real-time budget. On timeout, saving continues with the host's last confirmed state; pending queues are retained for later processing rather than discarded or falsely acknowledged.

After a successful save, a wrapping yellow top-left warning remains visible for 15 seconds. It names the elapsed wait, phase and pending work: player replies, part queues/incoming updates/receipts, item edits, and Ultimate Tools initialization/discovery/unapplied changes. The warning is also written to the log, with missing player IDs separately recorded. Guests receive the warning; a text-only top-left fallback supports CleanHUD off. Successful Save and Exit still exits normally.

**Unconfirmed changes may be absent from the timeout-assisted snapshot.** The limit covers synchronization, not total disk-save duration. Terrain preparation, resource-integrity gates, actual write errors and rollback safeguards remain in force. The triggering log identifies the save barrier, but does not prove which queue blocked that particular attempt.

Version **0.1.52**, tag **v0.1.52**, handshake **27** retained. Update every peer to the same release for full warning diagnostics. No new config, packet kind or save key. Existing host-notice strings allow save warnings up to 1024 characters; ordinary notices retain their previous limit. RC15's single-player save-selection fix remains included.

Install **BepInEx 5 x64** and the separately distributed [Apocalypter Multiplayer mod 0.4.1](https://www.nexusmods.com/apocalypter/mods/10), then extract the entire release ZIP into `BepInEx/plugins`. Keep standard Wild Wasteland disabled.

Manual verification (Testing):

1. Save normally as host; expect normal completion without a timeout warning.
2. Reproduce the pending-operation save. After about 30 seconds of synchronization, expect world writing, completion and resumed gameplay. The yellow warning must identify elapsed time and pending queues/counts, matching `Save warning: waited ...` in the log.
3. Check the warning/log on guests and with CleanHUD disabled. Let pending work settle, save again and reload to verify the confirmed state.
4. Test Save and Exit, ordinary autosaves and a normal SP save/load. Actual failed writes must retain their existing failure handling.

No game/harness launch, live save/config/base edits, or changes to other repositories. Gameplay and save integrity remain **Testing**. Source diagnosis and implementation details: [save-sync-timeout-2026-10-21.md](save-sync-timeout-2026-10-21.md).

Windows Release/Deploy=false passed with zero warnings/errors. Exact stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.52-RC16a.dll`; built/staged MD5 **C69B34F776F60AE4635E86F947CEBB82**. Runtime-only ZIP: **287 files**, **236,693,207 bytes**; SHA256 **94f94d12798876592d6f57db21a15d74343e4a102d2ad5a464b0ec28defaa0e1**. CRC/exact bytes/safe unique paths/single DLL/18 loading screens/156 retained baseline assets checked. Standard-WW install lacks active MP folder/dependency, so staged only; no incomplete deployment. Official non-prerelease release candidate for latest discovery.
