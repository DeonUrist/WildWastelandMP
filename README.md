# WildWastelandMP

**RC8 · 0.1.44** — Wild Wasteland for multiplayer Apocalypter: shared exploration, vehicles, NPCs, quests, weather and survival, with host-managed saves and player recovery.

## Requirements

Install **BepInEx 5** first. For multiplayer, install the Multiplayer dependency on **every player's PC** as well:

- **[BepInEx 5 (Windows x64)](https://docs.bepinex.dev/articles/user_guide/installation/index.html)**.
- **[Apocalypter Multiplayer mod from Nexus Mods](https://www.nexusmods.com/apocalypter/mods/10)** — **VERSION 0.4.1** - required separately for hosting/joining; it is not included in this download.

All players need the **same WildWastelandMP release and mod versions**. Disable standard WildWasteland and the standalone mods replaced by its modules; keep their configs and customized assets.

When the Multiplayer dependency is missing, the main menu offers its Nexus download link, even with optional recommendations off.

Offline and host saves now drain pending terrain work, hold streaming through publication, then resume it. Location discoveries and snapshot publication use same-directory file replacement on Windows; integrity checks remain.

Offline saving uses the integrated named save library, backups/recovery and autosaves even without Apocalypter Multiplayer. The dependency is still required to host or join a multiplayer world.

RC8 uses plugin ID `com.denis.apocalypter.wildwastelandmp` and cannot run alongside single-player Wild Wasteland. Existing settings and saves are retained.

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

The host owns world saves. Reloading a save restores its matching player snapshot. RC8 is an official release candidate; gameplay and recovery checks are still in progress.

[Detailed multiplayer/local testing guide](README-MULTIPLAYER.md) · [RC8 release notes](notes/release-0.1.44-RC8.md) · [Inherited module guide](WILD-WASTELAND-GUIDE.md)
