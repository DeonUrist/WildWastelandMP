# Wild Wasteland MP RC3 (0.1.39)

- Wild Wasteland MP now has its own plugin ID: `com.denis.apocalypter.wildwastelandmp`. Apocasetter can identify it separately from single-player Wild Wasteland and offer the correct updates.
- The plugin declares incompatibility with single-player Wild Wasteland (`com.denis.apocalypter.wildwasteland`), preventing both overhauls from running together. Disable one before using the other.
- Existing `WildWasteland` settings, module data, saves and namespaces remain unchanged. On first startup, a missing MP top-level config is copied from the previous plugin config; the original is retained and existing MP config is never overwritten.
- Includes RC2's host-near bodyless spawning and host Recall controls, with all previous assets.

Install the full ZIP into `BepInEx/plugins`. Requires separately installed **BepInEx 5 (Windows x64)** and **Apocalypter Multiplayer 0.4.1** from [Nexus Mods](https://www.nexusmods.com/apocalypter/mods/10). All players must restart with **0.1.39**, the same Multiplayer version and matching other mods. Handshake remains24; the strict mod manifest rejects older plugin identities/builds.

RC3 is the official Apocasetter-index distribution. Its RC label still indicates ongoing gameplay testing; Windows compilation/package checks do not verify networking, save recovery or performance. No game or gameplay harness was launched.

Windows Release: zero warnings/errors. Built/staged DLL MD5: `3236E4F6B1D08928FA76AC595C93D745`. Not deployed locally because the current single-player installation has neither MP assets nor the separate Multiplayer dependency.

Manual checks: upgrade an existing MP installation and confirm F3 key/debug settings, module settings and saves remain; verify Apocasetter shows separate Wild Wasteland and Wild Wasteland MP rows and switching variants queues the other for disabling without removing configs. With both manually present, BepInEx must reject the incompatible MP plugin. Restart all peers and check host/guest join, bodyless spawn, Recall and save/reload.
