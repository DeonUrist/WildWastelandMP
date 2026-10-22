# Ultimate Tools integration — Testing, 2026-10-20

Requested: preserve existing and future Ultimate Tools world edits, host ↔ client and client ↔ host ↔ client, including saves and later joins. Ultimate Tools stays a separately installed optional mod, with the existing `com.denis.apocalypter.partadjustment` soft dependency. Every peer must use the same WWMP and Ultimate Tools builds; handshake revision is now 25.

## Source proof

- Local source inspected: `V:/Repos/Apocalypter/ultimatetools`, Ultimate Tools 2.3.7. `AdjustmentSession.Change/Reset` changes the part's local transform; `NativeAdjustment` activates the native AdjustHinge FSM; `SuspensionAdjustment.Change/Apply/WriteSaveVariables/ReadSaveVariables` owns width, ride height, front/rear balance, reconstructed geometry and progressive damping.
- Native MP inspection: `_staging/apocapatrol-multiplayer-audit-20261011/mp-source/ApocalypterMP.Net/WorldObjects.cs`, FindAttachment/Capture/ApplyAttachment (3584/4003/3723). Non-locked hinge attachment carries the hinge identity, without the screwdriver part offset; native car snapshots do not carry the custom stance or adjustable child transforms. ItemVars.For/AddTarget supports scalar native load-action variables, not these Vector3 transforms or the source component's live fields.
- Game source: `Sources/fsm/prefabs/Duke.md:501` saves only its exhaust/engine/radiator hinge vectors through saveItemVar; `:1747` shows the native AdjustHinge move/rotate/reset states. `Sources/code/Assembly-CSharp-firstpass/ES3Types/ES3Type_GameObject.cs` shows that child serialization is conditional. An explicit companion record is needed for other editable child poses.
- Native ApplySpawn prefixes physical replica names with `MP_`. Ultimate Tools TailLights.IsTail/Variant previously only recognized unprefixed tail_light names, so guest tint/beam restoration also needs the lookup correction.

## Implemented behavior

`Core/UltimateToolsCompat.cs` resolves the installed source signatures and uses generated delegates, without a compile-time DLL reference or an exact-version rejection. With no Ultimate Tools plugin, it installs no job or hooks. Missing required source APIs fail bridge installation explicitly through the existing MP unavailable path.

Actual screwdriver changes and width/height/balance/reset operations submit reliable, epoch-scoped requests to the host. Host validates the peer, current target/mount, reach and values, assigns monotonically increasing revisions, applies once and fans out to every client. The existing native MP flow still owns pickup, wrench detach/attach, loose and held item poses, drops, physics and car ownership. The companion reapplies authored offsets after native attachment updates; attachment changes replace old offsets or tombstone them. Damping is reproduced by the original Ultimate Tools Apply method from the synchronized stance.

Host discovery captures already loaded native part poses, editable hinges and source suspension values, with bounded root/hierarchy queues. Late joiners request the current catalogue; packets that precede native object/kit/attachment creation wait in a bounded-work retry queue. Revision checks prevent older catalogue entries from replacing newer edits. Native identity and prefab IDs are unchanged. Tail-light lookup recognizes guest replica names without renaming physical objects.

The existing owner save barrier now waits for pending tool receipts and host discovery/application. The optional `WildWastelandMP.UltimateTools` key is written into the existing journaled host ES3 transaction, so named snapshots, autosaves and backups include it without another sidecar/publication step. This follows Denis's explicit requirement: real save plus its backup, no config or external save-data file. HostSaveFiles.Participants includes the real world file, and CopyVerified copies the entire file with hash verification. It stores native names plus child/mount paths and poses/stance; runtime network IDs are rebound on load. Native Ultimate Tools save keys are preserved. Old saves lacking the key import their current native restored values. Rehosting the same loaded generation preserves unsaved current changes. If the new key cannot be read, the original world is retained and a subsequent host save fails explicitly.

This cannot recover edits that an older save or native multiplayer session had already lost. Offline standalone saving remains owned by the existing native/Ultimate Tools paths; the new optional key is a host-world supplement.

## Denis's runtime checks

1. Start one host and two clients with identical DLLs and Ultimate Tools installed. Expect `[Multiplayer] Ultimate Tools sync ready: part/hinge poses and suspension; host relay and native saves` on each instance. Keep gameplay status Testing.
2. On the host, move/rotate/reset an engine/exhaust/radiator hinge and an ordinary mounted part. Both clients should see the same offsets. Repeat on client A, then client B, observing from the other two instances. Test while the car is host-owned and while another client drives it; ownership must remain native.
3. With a lift kit fitted, change width, ride height and front/rear balance; reset and remove/refit the kit from every role. All three should match geometry; the driving owner should retain source damping and stock restoration. Repeat on a buggy-family car and a lifted-family car.
4. Wrench-detach, carry, adjust, drop and reattach a part; mount a headlight/tail light on a free surface and a native slot. Check all observers, switch lights on/off, then detach/re-attach to the same and different mounts. Previous offsets must not reappear. Guest tail lights should retain red lens/beam behavior.
5. Save immediately after a client edit, including autosave and save/quit. Reload/rehost and join from both clients. Check the same exact poses, stance and mounted lights. Quit hosting and rehost without reloading after another unsaved edit: it must retain the current pose. A late joiner/reconnected client must see edits made before joining.
6. Load a pre-patch save with existing Ultimate Tools edits, host it, and join from both clients. Verify those native-restored values, save a new snapshot, then reload that snapshot. Also check ordinary play with Ultimate Tools absent, and rejection of a peer with the old WWMP build.
7. Save an edited setup A, change it to B and overwrite through the existing confirmed save flow. Restore the backup of A through SAVES, reload/rehost and reconnect both clients. Expect A's part offsets, hinge poses and stance on all three, while the newer B save retains B. Backups predating this patch use native restored state because they do not yet contain the optional supplement.

Verbose WW logging adds `[Multiplayer] Ultimate Tools committed: <object>/<path>, revision <N>`. A supplemented save logs `Ultimate Tools saved edits loaded for <N> objects`. A rejected request logs `Ultimate Tools edit rejected by host: target changed or is out of reach` on its sender.

Compilation and source-path review do not establish runtime synchronization, save integrity, visuals or performance. No game/harness automation is permitted for this task.

## Build handover

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=false -p:OutputPath=bin/Release/ultimate-tools_20261020a/` succeeded with zero warnings/errors. Exact stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_ultimate-tools_20261020a.dll`,79,995,392 bytes; build and stage MD5 `6E82DA0A4278867CC5B486F12E155CEB`. Version stays0.1.48; handshake25 requires every peer to update.

Not deployed: game running and current installation contains standard Wild Wasteland without the WildWastelandMP asset folder. No incomplete DLL-only MP installation was created. No game/harness launch, live save/config edits, external mod/base edits, commit, push or release. Runtime status remains Testing.
