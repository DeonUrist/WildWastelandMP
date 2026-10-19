# Wild Wasteland MP RC11 (0.1.47) — Client crossbow tooltip

Fixes a client seeing **20 damage** when hovering over a crossbow while the host sees **50**. Apocalypter Multiplayer adds `MP_` to replicated item names; the tooltip's crossbow name check now recognizes that prefix. Applies to both ordinary hover text and the shop Buy tooltip.

This corrects the displayed number. The existing projectile calculation already recognizes replicated crossbows and applies the same 2.5× base-damage multiplier. Projectile damage, settings and save formats are unchanged. The tooltip correction remains conditional on Guns projectiles and player projectiles being enabled; disabling those retains vanilla text.

Includes the RC10 purchase-sync, occupant collision protection and autosave-warning fixes. Update and restart every peer with matching builds; handshake 24 remains. Extract the full ZIP into `BepInEx/plugins`, keeping standard Wild Wasteland disabled. Multiplayer requires separately installed **BepInEx 5 x64** and [Apocalypter Multiplayer 0.4.1](https://www.nexusmods.com/apocalypter/mods/10). No dependency DLLs are bundled.

Manual check: host and guest hover the same ordinary crossbow and an unpaid shop crossbow with player projectiles enabled; both should show 50. Repeat after purchase/drop and reconnect, and check another weapon's tooltip remains unchanged. With player projectiles disabled, confirm vanilla crossbow text remains 20. Runtime/visual acceptance remains **Testing**; no game or harness was launched.

Source: `src/Modules/Guns/WeaponDamageText.cs` now checks the crossbow name immediately after an optional native `MP_` prefix, preserving case-insensitive matching, literal text checks and finalizer restoration. No new patch target, hook, job, packet or config key.

Windows Release/Deploy=false passed with zero warnings/errors. Exact stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.47-RC11a.dll`, built/staged DLL MD5 **F7B44D8615C47F652869847749272DE9**. Current standard-WW installation/running game retained; no MP folder/dependency, no deployment or live config/save edits.

Runtime-only ZIP: 287 files, 236,668,234 bytes; SHA256 **f6cd3b783238b2b32ec022e3847a86902651ab3815ae7c0d424119016791b774**. CRC, exact source bytes, safe unique paths, single plugin DLL, 18 loading images and 156 retained baseline assets checked. Dependency DLLs, documentation and personal runtime data are excluded.
