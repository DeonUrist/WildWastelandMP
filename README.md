# WildWastelandMP

**RC1 · 0.1.37** — Wild Wasteland for multiplayer Apocalypter: shared exploration, vehicles, NPCs, quests, weather and survival, with host-managed saves and player recovery.

## Requirements

Install these on **every player's PC** first:

- **[BepInEx 5 (Windows x64)](https://docs.bepinex.dev/articles/user_guide/installation/index.html)**.
- **[Apocalypter Multiplayer mod from Nexus Mods](https://www.nexusmods.com/apocalypter/mods/10)** — **VERSION 0.4.1** - required separately; it is not included in this download.

All players need the **same WildWastelandMP release and mod versions**. Disable standard WildWasteland and the standalone mods replaced by its modules; keep their configs and customized assets.

## Install

1. Download the full ZIP from [Releases](https://github.com/DeonUrist/WildWastelandMP/releases).
2. Extract it into the game's `BepInEx/plugins` folder. The DLL should be at `BepInEx/plugins/WildWastelandMP/WildWastelandMP.dll`, with its asset folders beside it.
3. Start the game and follow any mod-conflict restart notice.

## How to play

- **Host:** at the title menu, open **MULTIPLAYER → HOST (IP)** or **HOST (STEAM)**. Choose a new world or an existing save, then host it.
- **Join by IP:** from the title menu, open **MULTIPLAYER → JOIN**, enter the host's address and matching port, then connect.
- **Join by Steam:** accept the host's invitation while at the title menu.
- **F3:** settings. **TAB:** inventory and player list. Guest Save requests go to the host. Local menus do not pause the shared world.

The host owns world saves. Reloading a save restores its matching player snapshot. RC1 is a prerelease; gameplay and recovery checks are still in progress.

[Detailed multiplayer/local testing guide](README-MULTIPLAYER.md) · [RC1 release notes](notes/release-0.1.37-RC1.md) · [Inherited module guide](WILD-WASTELAND-GUIDE.md)
