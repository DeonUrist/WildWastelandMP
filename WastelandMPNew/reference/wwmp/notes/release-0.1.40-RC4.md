# Wild Wasteland MP RC4 (0.1.40)

When Apocalypter Multiplayer is missing or disabled, the main menu now shows **REQUIRED MOD MISSING** and a **Download Apocalypter Multiplayer** button linking to [the original Nexus page](https://www.nexusmods.com/apocalypter/mods/10).

The notice uses the existing recommended-mods/conflict warning panel, font, colors and scrolling. It remains visible when optional recommended mods are turned off. Existing conflict warnings and optional recommendations follow below it. It stays hidden during loading, settings and menu modals; when the required plugin is loaded, this dependency notice is absent.

The Multiplayer DLL remains a separate download. The existing official Apocasetter entry retains its dependency requirement, distinct MP plugin identity and mutual exclusion with single-player Wild Wasteland. Existing settings, saves, handshake24 and RC3 gameplay are unchanged; all peers must restart with matching builds.

Requires **BepInEx 5 (Windows x64)** and **Apocalypter Multiplayer 0.4.1**. Extract the full ZIP into `BepInEx/plugins` with single-player Wild Wasteland disabled. This is an official release candidate; in-game layout, interaction, networking, saves and performance remain Testing. No game or gameplay harness was launched.

Windows Release: zero warnings/errors. Built/staged DLL MD5: `14499795E93C67EC859202B9573E47DD`. Local RC3 has a removal queued in Apocasetter; that operation was preserved and RC4 was not deployed over it.

Manual check: with the dependency absent/disabled and Recommended mods off, start at the title menu and verify the notice and download link. Repeat with recommendations on and a replaced standalone conflict present; all sections should remain readable/scrollable. Open settings/MULTIPLAYER and check the panel yields, then return. Install/enable Apocalypter Multiplayer and restart: the missing-mod notice should disappear.
