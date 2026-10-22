# Wild Wasteland MP RC13 (0.1.49) — Ultimate Tools synchronization

- **Host and client tool edits:** optional compatibility with separately installed Ultimate Tools synchronizes screwdriver part offsets, native adjustable hinges, and suspension width, ride height and front/rear balance. Client edits are validated and committed by the host, then sent to every client. Host edits and late joins use the same current state.
- **Real world saves and backups:** additional adjustment data is embedded in the real host `.es3` world save through the existing save transaction. Named snapshots, autosaves and backups carry it with the world. No new configuration or external save-data file. Native Ultimate Tools save keys remain intact, and older worlds import whatever adjustments their native save actually retained; previously lost edits cannot be reconstructed.
- **Native interaction retained:** wrench attachment/detachment, carrying, drops, car ownership and physics retain their existing native paths. Companion poses survive native attachment updates, old offsets are cleared when mounts change, and the source suspension code reproduces geometry and damping. Guest tail-light names are recognized without renaming physical items.

All players must update and restart: **handshake 25** requires matching WWMP and loaded mod builds. Ultimate Tools stays optional and is not bundled; when used, install the same Ultimate Tools build on every peer. Required multiplayer dependencies remain separately installed **BepInEx 5 x64** and [Apocalypter Multiplayer 0.4.1](https://www.nexusmods.com/apocalypter/mods/10). Extract the full ZIP into `BepInEx/plugins`, keeping standard Wild Wasteland disabled.

The new on-foot dog one-shot report has been audited, but its cause is not established without the incident's fresh host/client logs and pre-hit health. This release does **not** claim to fix it. RC12's occupant protection, seated dog reach and speaker-character shout corrections remain included. Backward placement on downing remains unresolved.

Gameplay, synchronization, save integrity, visuals and performance remain **Testing**. No game or harness was launched. Manual checks: with one host and two clients, edit/reset part and hinge poses and suspension from each role; detach/reattach parts and free-mounted lights; save immediately after a client edit; reload/rehost and reconnect; then restore an earlier backup and confirm its earlier poses and stance on all three. Check a pre-patch save and a late join too.

Source proof and full checks: [Ultimate Tools integration](../compat/ApocalypterMP/ULTIMATE-TOOLS.md), [on-foot dog audit](dog-on-foot-damage-audit-2026-10-20.md).

Windows Release/Deploy=false passed with zero warnings/errors. Exact stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.49-RC13a.dll`, built/staged DLL MD5 **4F0E627FA480AB6C0A0F3574C9573BBF**. Current standard-WW installation retained; no incomplete DLL-only MP deployment, game/harness launch or live config/save edits.

Runtime-only ZIP: 287 files, 236,669,612 bytes; SHA256 **87901eb68b4e5b73722ad637ae3287abd395b6ab241c69d9579ab865728bcde4**. CRC, exact packaged source bytes, safe unique paths, single plugin DLL, 18 loading screens and 156 retained baseline assets checked. Dependency DLLs, documentation and personal runtime data are excluded.
