# Step 15 — review, repairs and local multiplayer results

2026-10-18. **Non-live experimental patch; leave in Testing.** This completes the requested review/repair and multiple local host/join test pass. It does not establish full gameplay acceptance of every WW feature. Earlier step13/14 build-only and pause statements are historical; the user explicitly authorized these isolated runtime tests.

## Patch identity and isolation

- Base/main: `91bff52450e27a461fdbced48317910058ce5cf2`, WW0.1.36, including retirement of QuestsCompat. Main remained clean throughout.
- Worktree: `C:\Users\chura\.codex\worktrees\ww-mp-rc33\WildWasteland`.
- Complete package: `V:\Repos\Apocalypter\_staging\ww-mp-20261018\rc33-step15`; cumulative patch includes new files, source-overlay, DLL, results and this handover.
- DLL MD5 **C1836DE43A35BD24FFD094B91F865CE3**, SHA256 **8031B465AEA50E73CE25987B7F453F4FF028C31314BD598E82085028E7160418**. Windows PowerShell Release, Deploy=false, zero warnings/errors.
- Inspected ApocalypterMP1.0/protocol21 SHA256 **BF0528817B0575A00E7A03F9E520DDACCBEE0322328369CE97351F433633CE2E**. No external DLL modifications or redistribution. All peers need the same WW overlay; handshake revision3.
- No live deployment, commit, push or live config edit. Hashes of all16 baselined top-level personal save/settings/log files and the installed WW DLL matched afterward. Nested historical Apocasaver backups were not hash-baselined. Owned test processes were stopped.

## Review findings repaired

1. **Items/parts:** native ItemVars reconciliation could undo writes made on the wrong owner. Float/int deltas and bool/string compare-and-set changes now prepare an atomic absolute owner command, serialize operations per item, retry idempotently across ownership changes, mirror authoritative values and wait for peer snapshot acknowledgments. Pending commands fence stale owner reports and participate in saving. A host consume notification precedes native despawn/held-item recovery.
2. **Survival pots:** shared contents/cooking/spoil/pour state now belongs to the host independently of physical ownership. Drinking uses expiring per-player intents, exclusive pot access and sequenced benefits; ingredient validation/consumption and two-pot pours commit on the host. Guests retain inert boiling/pour presentation. Null texture keys are skipped when registering ES3 material references; headless runtime had proved those invalid.
3. **Save lifecycle:** both peers freeze; final owner/item/player flushes and part/edit acknowledgments drain before the native host save. Phase retries, an explicit successfully-completed-flush flag, failure cleanup, excluded native inventory restoration, deferred WW save callbacks, ES3 cache storage and frozen remote poses preserve the barrier. Guest Save stays disabled/gray and CleanHUD's save/loading presentation follows it. The final review additionally caught a duplicate phase re-ACKing a failed flush; failed enumerators now restart on retry and only successful flush/report completion permits acknowledgment.
4. **World settings:** sending unchanged settings every headless frame filled the reliable queue. Handshake supplies the full snapshot; subsequent traffic sends changes only. CleanHUD's master switch is world-owned so a guest cannot bypass fog/marker policy by disabling the module. Guest new-world launch no longer auto-opens host world settings.
5. **Early join:** withholding Hello while the host generated terrain caused a real UDP timeout; that attempt was reverted. Welcome/heartbeats now proceed immediately, WorldInfo waits for host readiness, and a post-scene readiness request triggers a native object resnapshot. This repairs objects cleared during native scene loading.
6. **Damage/bolts:** exact client creature/body-part hits apply once on the host without a host-pose recast. Legacy native MP hits have no weapon/origin data and cannot use WW's weapon ranges; a conservative legacy bound retains identity/state checks. Sustained hits use bounded per-peer sequence replay windows rather than one shared expiring receipt limit. Host-maintained bolt anchors/corpse/ricochet/removal state and inert observer tracers remain.
7. **Car impacts:** collision traces showed a new kinematic corpse blocking a guest car after a kill. The native ClearTheWay temporary passage filter now includes remote uncarried corpse replicas, including `_Dead` animal remains that MP's Food-based Ragdoll flag misses. Native sweep, timeout and collider restoration remain. Actual native ram damage still belongs to WW's server path.
8. **Effects/headless/optional integration:** fast effects publish immediately and removals retain tombstones; guest thrown gameplay copies retire after native launch capture. True nographics MapMagic failed its RenderTexture height path, so headless sessions select native CPU SetHeights. Optional EngineSim passive feed/torque follows ownership while retaining its native worker/audio/post-physics behavior.

## Executed tests

These were actual separate game processes with MP and WW enabled, copied game resources, UDP127.0.0.1:27861, separate configs/plugins/TestSaves, and guest lag injection200ms (typically200–225ms RTT; some loading spikes higher). No installed-game launch. The external controller redirects save/settings paths and coordinates/observes real native prefabs, FSMs and protocol handlers. It is not part of production. Packet-level impact tests exercise real handler/transport/health behavior; they do not simulate physical gun input or validate rendered aiming.

| Run | Host / guest result | Coverage |
|---|---|---|
| WW integration1, attempt4 | 26/26 +31/31 checks passed | Exact moved-target hit, duplicate rejection, untouched neighbor, host/guest bolt anchor, shared quest/rep/full payout, pot sip, permissions, save freeze/resume, reconnect receipt, sleep voting, revival |
| Expanded WW, attempt7 | 37/37 +41/41 passed | All six weapon classes; loaded JSON PipeRat/crew/fittings; guest has no second pilot/crew; host-owned part repair70→72 and acknowledgments; two journey-owned foot walkers and guest detection suppression; previous scenarios repeated |
| WW/stress, attempt11, pre-save-retry DLL | **44/44 +48/48 passed, zero failures** | 8,300 unique impacts, old/recent replay rejection after receipt rollover, all six classes, bolt correction, JSON car/crew **19/19 fittings**, repair, foot walkers, quest/rep/full payout, pots, save, reconnect, sleep/revive and controlled native ram |
| Final WW/stress/fault injection, attempt12, final DLL | **45/45 +48/48 passed, zero failures** | Previous full matrix repeated, including8,300 impacts; three intentional guest flush exceptions held the host barrier until a successful retry; controlled ram repeated |
| Native full probe, attempt6 | Finished both save/reload/rejoin phases; one failed assertion per peer retained | 111 fresh /112 saved shared object variable lists match, all11 child saved fields present; live/load-time liquids, vehicle ownership/engine/key/gauges, riding/walking/platform/wall behavior, downed/bed respawn, legacy boss250→210 on both peers |

Final stress host log records **8,307 accepted impacts**: seven initial distinct test shots plus8,300 stress shots. The stress target lost exactly24,900 health (30,000→5,100); replaying the first and last stress IDs added no damage. The normal attempt11 logged no Error/Fatal. Final attempt12 logged exactly three intentional injected owner-flush errors on the guest and zero unexpected errors on either peer; no reliable queue saturation, rejected WW packets or failed reliable delivery. The host explicitly verified no serialization before successful recovery.

Controlled ram: **15.0m/s approaching,14.5m/s at the creature,16.5m beyond it in final attempt12 (16.4m in attempt11)**, with host-confirmed creature death. This used a test-only flat collider on both peers and released the parking brake through the real native Handbrake FSM. The original native coast test forces velocity into a randomly equipped starting car, leaves its parking brake engaged and traverses generated terrain; those original failed assertions remain in the evidence. Before the corpse fix, traces also proved solid corpse contacts; after it, those contacts disappeared. Do not rewrite the original suite as fully passed.

The native starting-bandage assertion also remains failed: it expects a global count of2, while the world contained distinct original17, new-player MP31 and another world87 objects. The new-player starter kit was added once; this is not evidence of a duplicated kit. Earlier attempts/failures are retained under runtime-step15, including the broken headless height path, settings flood, legacy damage and delayed-Hello timeout that led to fixes.

## Retained behavior and limits

Host WW NPC AI/navigation/events/Patrols own world simulation. Shared quests and reputation, full reward to each separate wallet, host world/fog/marker policy, local HUD and map/compass switches, host-only debug, locked-off MP Performance, disabled MP compass/F9–F12, native model selection/offline identities and native save formats remain as agreed. MP alphas, revival, companionship, co-op vehicles, persistence, sleep votes and Steam code paths remain; MP lures/horn attraction/reinforcement waves are suppressed. QuestsCompat remains unregistered.

**Do not claim every feature runtime-verified.** Remaining acceptance matrix:

- Steam transport was unavailable in these local processes; UDP was exercised. GPU rendering, UI appearance, actual aiming/animations/audio and a human joining the intended server still need interactive verification.
- Source covers independent NPC perception/closest acquired visible player on foot/in cars, but the full occlusion/memory/multiple-player combat matrix was not automated here.
- EngineSim was not installed in these test instances. Its optional torque/audio/tuning lifecycle needs a real integration run.
- Live blast-lance/armor attach/detach/consumption, every door/trunk/hood, all templates/modded prefabs, every weapon/reload mode, offline load after MP save, weather/explosion combinations and long sessions still need the PART-STATE-CHECKLIST matrix. Successful repair/fitting registration is not proof of that whole matrix.
- Generic item edits preserve the native caller's tool/resource transaction; there is no universal refund/rollback system for every native resource cost after an exceptional rejected/conflicting edit. Weapon/ammo validation is bounded, not a complete host anti-cheat reconstruction. A connected peer that never acknowledges can keep a save paused; no partial-save timeout is used.

## Resume / next interactive test

Use rc33-step15 only; older packages are historical. Apply the complete patch in a separate clean checkout of the base after git apply --check, or explicitly review/rebase onto a later main. Never use tracked.patch alone or blindly overlay later source. `FILES.txt`, `CHANGED-FILES.tsv`, `APPLICABILITY.txt` and SHA256SUMS document the package.

On identical copied-world host and human guest: confirm the matching-patch/bridge-ready lines; operate the gray guest Save button; host-save and observe SAVING/pause/resume on both; change host world/fog policy and guest display switches; run the remaining fitting/AI/weapon/offline matrix above. Useful confirmations: `Client impact: ... exact client target (no host recast)`, `Item edit committed`, `Server save: gameplay paused`, `all connected owners flushed; writing host world`, `gameplay resumed`.

The separate runtime-step15 folder contains isolated test copies, TestSaves, helper source and all chronological logs. They are not the planned live headless server. No test process is intentionally left running. Keep the source draft and reports for continuation; no commit/push/deployment was performed.
