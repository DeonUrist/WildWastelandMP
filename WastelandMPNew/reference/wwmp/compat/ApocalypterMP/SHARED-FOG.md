# Shared fog of war — 2026-10-18

Status: Testing, source changes only. Denis approved the larger scope and explicitly deferred the DLL build/deployment. No compilation, game launch, harness, commit or push was performed for this change.

Changed five source files (about 220 added/changed lines): `src/Core/UI/CorePages.cs`, `src/Core/MultiplayerHandshake.cs`, `src/Modules/CleanHUD/CleanHudModule.cs`, `src/Modules/CleanHUD/MapFog.cs` and new `src/Modules/CleanHUD/MapFogNetwork.cs`.

## Cause and behavior

`CorePages.NavigationSettings` explicitly exposed `FogRevealRadius` in WORLD and initial hosting setup. It is an exploration-distance tuning value. Its row now belongs to F3 > Debug > CleanHUD > Fog of war tuning; its existing config key, range and value remain intact.

`MapFog` previously contained only a local exploration grid. New WORLD > Shared vision appears immediately beneath Fog of war, including in initial HOST setup. `settings.cfg [CleanHUD / Map] SharedVision` defaults false and uses the existing host-authoritative navigation settings overlay. Guests cannot edit it, and their own preference is restored on leaving the session. Adding the world setting and fog messages requires matching handshake revision 13.

Sharing combines each player's personal explored cells with a separate shared grid for map fog and fog-gated markers. Actual local exploration continues recording while sharing is on, even when the player's fog display is off. Received exploration never writes to the personal grid. Turning sharing off invalidates the map window and restores personal fog; independently discovered location identities retain their existing personal behavior.

Personal saves retain `<slot>.fog.bin` and its unchanged IMFP v4 format. The host separately saves the cumulative shared union to `<slot>.shared.fog.bin` with the same seed-checked format and atomic replacement. Shared history survives saves, guests leaving and host reloads, but only becomes visible in multiplayer with Shared vision enabled. Guests do not save shared cells into either their personal file or a local shared file. Native saves are unchanged.

## Source direction checks

- WW 156 requests a fog snapshot: guests request the host union; the host requests personal contributions on enabling sharing.
- WW 157 carries guest personal blocks to the host; only a known peer with the matching world seed/session epoch is accepted.
- WW 158 distributes the host union to guests; guests accept it only from host peer 0, with matching seed/epoch.
- Messages have distinct unused IDs, fixed 512-byte cell blocks and validated part/coordinate bounds. They use existing ordered reliable transport. Packet payload is 540 bytes including the WW header, below its 1200-byte limit.
- Existing 0.2 s fog exploration job sends up to four personal blocks and, on host, four union blocks per tick. Dirty blocks and full join snapshots share round-robin queues. No extra scheduler job, scene scan, remote-position approximation or captured writer lambda was added.
- Sharing changes and epoch changes reset pending transfers; world load and module shutdown clear the shared cache. Guest transfers wait for the existing host-provided personal slot. A late guest requests the full union; later contributions are broadcast normally. Origin shifts do not alter the world-cell keys.

These are source checks, not runtime or performance verification.

## Denis's checks after a later build

1. Both peers need the matching rebuilt DLL (revision 13). Check WORLD and initial HOST setup: Shared vision is directly below Fog of war, default Off. Reveal radius is absent there and present under Debug > CleanHUD > Fog of war tuning.
2. With fog On and sharing Off, explore different, widely separated areas on each peer. Each map should retain its own explored area and conceal the other player's area.
3. Host enables Shared vision. Allow streamed history to arrive; both maps should show the combined exploration. Explore fresh ground on each peer and verify both directions update, including fog-gated map markers.
4. Host disables Shared vision while maps are open. Each map should return to personal coverage, retaining ground that player actually explored while sharing was on. Toggle back On and confirm combined coverage returns. Guest cannot change the world setting.
5. Join late, disconnect/rejoin, and save/reload with sharing On and Off. Personal coverage must remain separate; shared history should reappear only when enabled. A different seed/slot must not inherit the shared grid. Check map recentering/origin shifts and raised-binocular exploration as well.
6. With global VerboseLog enabled on the host, transitions log `[CleanHUD] Fog shared vision on; personal exploration retained separately` and `[CleanHUD] Fog shared vision off; personal exploration restored`. Guest debug remains governed by the existing host policy.

No deployed MD5 exists for this change because the build was deferred by Denis.
