# WildWastelandMP

**RC19 · 0.1.55** — Wild Wasteland for multiplayer Apocalypter: shared exploration, vehicles, NPCs, quests, weather and survival, with host-managed saves and player recovery.

## Requirements

Install **BepInEx 5** first. For multiplayer, install the Multiplayer dependency on **every player's PC** as well:

- **[BepInEx 5 (Windows x64)](https://docs.bepinex.dev/articles/user_guide/installation/index.html)**.
- **[Apocalypter Multiplayer mod from Nexus Mods](https://www.nexusmods.com/apocalypter/mods/10)** — **VERSION 0.4.1** - required separately for hosting/joining; it is not included in this download.

All players need the **same WildWastelandMP release and mod versions**. Disable standard WildWasteland and the standalone mods replaced by its modules; keep their configs and customized assets.

When the Multiplayer dependency is missing, the main menu offers its Nexus download link, even with optional recommendations off.

Offline and host saves now drain pending terrain work, hold streaming through publication, then resume it. Location discoveries and snapshot publication use same-directory file replacement on Windows; integrity checks remain.

Offline saving uses the integrated named save library, backups/recovery and autosaves even without Apocalypter Multiplayer. The dependency is still required to host or join a multiplayer world.

Wild Wasteland MP uses plugin ID `com.denis.apocalypter.wildwastelandmp` and cannot run alongside single-player Wild Wasteland. Existing settings and saves are retained.

RC19 preserves your explicitly selected respawn seat, bed or bedroll through downing, revival and death. It follows the selected object until you choose another; respawning places you on a clear supported surface nearby. Optional Vistas World Gen compatibility now shares host generation settings, weather and wind timing, disables per-machine spawn tracks and dust devils during sessions, and respects vehicle/item ownership. Handshake 28 requires every peer to update. Runtime verification remains Testing.

RC18 fixes Windows error 1175 when saving personal or shared map exploration. Fog files use the same atomic replacement as the other save participants; real write failures still prevent a partial snapshot. After an already failed save, reload the last good snapshot before trying again. No save-format/config changes.

RC17 reconciles merchant-bus stock every three seconds: paid items remain available to other players, unpaid stock is free during Coyote hostility and costs money again when friendliness returns. Fresh trader buses have no ordinary loot; a bounded PhysicsLock-area audit runs only on their first spawn. Revisited buses retain saved cargo without another deletion pass, and future cargo snapshots exclude separately restored trader stock to prevent duplicate loose goods. Update all peers; runtime verification remains Testing.

RC16 prevents pending owner/mod synchronization from holding a host save forever: after 30 seconds, saving proceeds with the host's last confirmed state. A yellow top-left warning names the pending work, queue counts and elapsed wait, also recorded in the log. Unconfirmed changes may be absent from that snapshot; actual save-write failures still retain existing protections.

RC15 fixes offline loading with the Multiplayer dependency installed: the selected save remains pinned during scene startup, preventing the early new-game state from replacing its path and restoring the wrong world. This addresses the seed 8975309 save/load/fall report; in-game confirmation remains pending.

RC14 checks creature melee reach on the affected guest, restores hurt sound/flash and uses vanilla dog attack timing in multiplayer. Combat has a separate reliable lane. Joining streams objects in paced batches, save barriers handle loading peers, and passenger seats are arbitrated by the host. Object and avatar lifecycle fixes, shared-item reservations, native caches and reduced repeated network traffic are included. All peers must update to handshake 27; gameplay and performance remain Testing.

Non-Steam sessions now use a local per-install identity to avoid username collisions. Do not copy that identity between PCs. Older username-keyed records stay in the save but are not automatically claimed; Steam identities are unchanged.

RC13 added optional Ultimate Tools synchronization: host and client part/hinge adjustments and suspension width, ride height and balance are relayed through the host to all clients, including late joiners. Extra adjustment data is embedded in the real host world save and its backups, with no new config or external save-data file. Install Ultimate Tools separately on every peer if using it. The reported on-foot dog one-shot remains unverified after RC14's melee changes.

RC12 protects seated drivers and passengers from animal contact and enemy melee while the vehicle is moving or its engine is running. It also corrects false dog bite reach against seated guests and makes remote shout voices follow the speaker's character.

RC11 corrects the client crossbow hover text to show 50 damage with player projectiles enabled, matching the host and the existing projectile damage multiplier.

RC10 fixes purchased items still showing Buy on other players, extends default-off occupant collision protection to native crashes, and puts optional autosave warnings in the transparent top-left log. Set Warning seconds to 0 to disable pre-save warnings.

With nothing equipped and fists lowered, drivers grip and animate the wheel; passengers retain resting hands. Ready fists and seated attacks still use their upper-body poses.

## Install

1. Download the full ZIP from [Releases](https://github.com/DeonUrist/WildWastelandMP/releases).
2. Extract it into the game's `BepInEx/plugins` folder. The DLL should be at `BepInEx/plugins/WildWastelandMP/WildWastelandMP.dll`, with its asset folders beside it.
3. Start the game and follow any mod-conflict restart notice.

## How to play

- **Host:** at the title menu, open **MULTIPLAYER → HOST (IP)** or **HOST (STEAM)**. Choose a new world or an existing save, then host it.
- **Join by IP:** from the title menu, open **MULTIPLAYER → JOIN**, enter the host's address and matching port, then connect.
- **Join by Steam:** accept the host's invitation while at the title menu.
- **F3:** settings. **TAB:** inventory and player list. Guest Save requests go to the host. Local menus do not pause the shared world.
- **WORLD:** Fog of war on/off, plus Shared vision when Multiplayer is installed. Radius, edge fade and opacity are under **Debug → CleanHUD → Map fog of war**.
- **Host player controls:** TAB → PLAYERS → RECALL brings a guest or sleeping body beside you; RESET keeps its camp/body-removal behavior. Guests joining without a body spawn beside the host.

The host owns world saves. Reloading a save restores its matching player snapshot. RC19 is an official release candidate; gameplay and recovery checks are still in progress.

[Detailed multiplayer/local testing guide](README-MULTIPLAYER.md) · [RC19 release notes](notes/release-0.1.55-RC19.md) · [Inherited module guide](WILD-WASTELAND-GUIDE.md)
