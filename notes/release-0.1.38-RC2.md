# WildWastelandMP RC2 (0.1.38)

- Guests joining without a sleeping body now appear on a safe surface beside the host, including when the host previously removed their body. Saved inventory, stats and respawn selection are retained. Existing sleeping bodies still use the confirmed wake-up handoff.
- Guest respawns without an available selected spawn object now fall back beside the host instead of the initial camp. The host's own fallback remains the initial camp.
- The host's TAB > PLAYERS list now has RECALL beside RESET. Recall brings an active guest or sleeping body nearby. Active guests leave their vehicle safely; inventory, vitals and selected respawn remain intact. Confirmations identify the target; guests cannot initiate these actions. The host's own Recall is disabled.
- New-player placement now waits for main terrain and standing clearance, and connection notices/player reports wait for placement completion.

Install the full ZIP into `BepInEx/plugins`. Requires separately installed **BepInEx 5 (Windows x64)** and [Apocalypter Multiplayer](https://www.nexusmods.com/apocalypter/mods/10). All peers must restart with **0.1.38 / handshake 24** and matching mods.

RC2 is a prerelease. Windows Release compilation and archive integrity are checked; gameplay, networking, save integrity and performance remain Testing. No game or gameplay harness was launched.

Windows Release: zero warnings/errors. Built/staged DLL MD5: `FEAE526B05053ED70C06E09AB14FF74D`. Local deployment was withheld because the MP asset folder and separate Multiplayer dependency are absent from the current single-player installation; no incomplete DLL-only install was created.

Manual checks: join without a body while the host is far from camp; remove a disconnected guest's body and rejoin with the retained character; recall an on-foot, driving, passenger and downed guest; recall a sleeping body and rejoin there; cancel confirmations; save/reload afterward. Check a selected respawn object still takes precedence, and RESET retains its original camp/body-removal behavior. Relevant logs: `Player placed beside host`, `Host player recall requested/completed`, `Host recalled sleeping body`.
