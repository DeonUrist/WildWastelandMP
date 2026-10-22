# Step16 — active MP fork and interactive presentation fixes

2026-10-18. User explicitly switched development/deployment to `V:\Repos\Apocalypter\WildWastelandMP` and the installed game's plugins, naming the mod WildWastelandMP. This supersedes the previous non-live restriction for this fork. Do not resume new edits in the old isolated worktree. Main WildWasteland remains unchanged.

## Current build

- Release Windows build/deploy: zero warnings/errors.
- Installed DLL MD5: `D76A82E140960CAD1F4FECCBD588994B`.
- Project: `WildWastelandMP.csproj`; assembly/display name WildWastelandMP; stable GUID/namespaces/save keys retained.
- Protocol overlay handshake revision4; both peers require this revision.
- No commit, push or release.
- Source includes untracked additions; do not discard them or export only tracked git diff.

## User-reported causes and changes

1. Native MP only strips hand props from player clones. The remote WW skin inherited Flexa's headband, armband, bags and extra renderers. MultiplayerAvatars now strips clone accessories like local Body.Create and keeps the main skinned renderer.
2. The character enum already contained six native models, but the UI placed all six plus None in one narrow choices row below male/female cards. Additional characters now use six full-width buttons in a two-column grid, available offline too.
3. The remote animation adapter never called Jump and trusted native MP's Falling-only Airborne flag. The motion extension sends jump counters and the actual Jump/Falling state at20Hz.
4. Climbing and final seated/wheel IK were local-player-only states. A small epoch-checked pose stream carries22 local bone rotations, hips and model placement for these special modes, sampled against native remote render time. Native MP still owns player roots/physics. Normal on-foot animation retains WW ModAPI.
5. CarSeat treated passengers as drivers and copied world-space driver bones. It now uses the actual vehicle ancestor, translates hips to the passenger seat, avoids passenger wheel control and hides native driver renderers only for the relevant replacement. Remote occupied drivers also hide their native mannequin through reversible renderer flags.
6. NPC custom bones were hard-written at10Hz while native Animator playback also wrote them. Receivers now retain/interpolate displayed poses and hold native Animator playback during the custom stream, restoring it after expiry. This is interpolated host pose playback, not a new autonomous client NPC brain.
7. The220-byte path cutoff omitted deep hand/weapon paths. Manifest paths now allow600 bytes, one per packet. Unique existing held meshes can be rebound to the authoritative hand path, with no object duplication; changed weapon parenting refreshes the manifest. Ambiguous matches are left alone.
8. Supplied Vista/Oldani MP's IsCreature/IsCorpse tests distinguish Food corpses. Decoded native human `_Dead` prefabs have jointed limbs but no Food FSM, so they were classified as creatures and all limbs made kinematic. The overlay recognizes jointed `_Dead` objects as corpses and retains native MP's local limb physics plus root corrections. No external DLL was edited.
9. Native UDP Update can dispatch HostClosing/Bye -> NetSession.Stop -> UdpTransport.Dispose, then dereference the closed socket on its next Available poll. Lifecycle guards now exit immediately after either direct/delayed dispatch closes the transport, skip later sends/updates, and make repeated Dispose harmless. Other exceptions remain visible.
10. Start-WildWastelandMP.bat / Launch-WildWastelandMP.ps1 launch graphical host/client from the game folder with independent configs/saves and shared deployed plugin assets. The production opt-in MultiplayerLaunch bridge handles direct hosting/join, local IDs and path redirection. It does not contain the external test driver or any combat fixtures.
11. Launcher saves use the native MP_ identity so SaveBus and guest sidecar synchronization accept them. The first launcher's noncanonical world/player registry copy forward without deleting originals.

## Runtime evidence and pending tests

Presentation build `731C5E252A9A63775E24BD22F8CE9874` launched installed host PID51616 and copied client PID17696. Logs confirm accepted revision4 handshake, UDP connection and108 shared objects; no presentation acceptance claim follows from that startup. The user quit/rehosted without F6, then identified that missing host shortcut. Later they reported the disposal exception above. Both applications were closed before deploying the latest D76 build.

The earlier93-check suite belongs to C1836DE4, before these source changes. It is historical evidence only. D76 compiled/deployed; the new UDP shutdown path still needs its runtime retest.

Retest both models and all six native choices; jump/climb from each camera; driver/passenger swaps in both views; steering, weapons and body visibility; NPC walking/combat/weapon holds; human and animal deaths on terrain; then quit/rehost/F6 and repeated join/disconnect. Confirm the host listener is present before diagnosing client retries. Camera key failures are not proven fixed: `[MP camera] Change Camera: allowed=..., paused=..., dead=..., camera=..., third=..., passenger=...` records the gating state when pressed.

Remaining broader gaps from step15 still apply: Steam, optional EngineSim, full perception/occlusion/multiple-player matrix, every fitting/door/consumption/offline load combination, refund of rejected native material costs and unresponsive-save-peer behavior.

Follow-up root cause: the client's actual refusal preceded the socket exception: `Join refused: Unable to read beyond the end of the stream`. F6 previously called native StartUdpHost, which opens a transport without creating NetSession.WorldInfo. WW's host-world check stayed false, so Welcome had no WW settings trailer. F5/F6 now register the current SaveLoadGame SaveFile/seed through native HostWorld (including ActiveSaveFile binding), then use the requested transport. This preserves the world rather than guessing a new seed. D76 compiled/deployed; fresh BAT restart host PID14228/client PID34460. Rehost/quit regression still requires an actual runtime retest.

Latest restart: canonical save migration logged, UDP listener confirmed on27861 in host PID14228; client PID34460 accepted the WW handshake and heartbeats (~12–31ms RTT). This verifies fresh startup only, not the interactive F6/quit regression or visual fixes.
