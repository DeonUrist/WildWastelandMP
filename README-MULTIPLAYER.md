# WildWastelandMP local testing

Active repo: `V:\Repos\Apocalypter\WildWastelandMP`. This fork contains the complete compatibility overlay from base `91bff52`, plus the current presentation, hosting and launcher changes. Denis requested committing the integrated checkpoint on 2026-10-18. Denis separately requested traveller-hearing and PLAYER/WORLD/HUD backports in the base WildWasteland repo; MP hosting remains in this fork.

Build from Windows PowerShell:

```powershell
dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true
```

The DLL deploys to `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`. Close both copies before rebuilding/deploying. Assets live beside that DLL. ApocalypterMP.dll remains a separate dependency.

Double-click `Start-WildWastelandMP.bat` in the repo or the game folder. It opens two visible copies at the title menu. It does not load a world, host or join automatically, or run automated gameplay tests. Optional arguments: `host` or `client` to launch only that role.

The host uses the main game executable. The client uses `MP\Client` inside the game folder, sharing the installed plugins/data through directory junctions, with separate BepInEx settings. Launcher saves/settings.xml live in `MP\Saves\host` and `MP\Saves\client`; launcher Player logs are `MP\Logs\host.log` and `client.log`. Choose the world in HOST setup. An existing first-launch `WWMP_Local.es3` and its player registry copy forward to `MP_WWMP-Local.es3` when the canonical save is absent; originals stay intact.

Direct launches can report SteamAPI initialization failure. This BAT uses UDP at `127.0.0.1:27861`, independently of Steam. Normal menu/Steam sessions keep their ordinary identity/path behavior.

The title and ESC menus have MULTIPLAYER; click it to open the panel to the right. HOST (STEAM) or HOST (IP) opens setup for WORLD NAME and the existing cars, foot-patrol, POI/weather and map/compass world settings. At the title menu, choose an existing save or a new world with a seed. Existing saves are loaded in place; ESC hosting uses the current save. Host Save writes to that selected world slot. Client Save sends the host a message such as `Alex wants to save`; it does not save automatically. F3 has separate PLAYER, WORLD and HUD navigation pages.

On the other copy, use MULTIPLAYER > JOIN at the title menu with the host's IPv4 address and matching UDP port; the local BAT defaults to 127.0.0.1:27861. Both copies require handshake revision 15 and identical loaded mod versions/builds (this build). Click an address/port row and type to replace it, Enter to apply or Escape to cancel. X/Escape closes the panel; Escape during endpoint editing only cancels that edit. JOIN from ESC offers to return to the title menu first; save beforehand, then click JOIN there. DISCONNECT stops hosting or cancels joining; a connected guest returns to the title menu. F5–F8 no longer control multiplayer; F9–F12 MP debug actions remain blocked. These controls use Clean menus (enabled by default). Local menus/F3/inventory do not pause an MP world; the shared save transaction still freezes gameplay while writing.

PLAYER offers eight portrait-only model choices in one row, with Female/Gungirl/Shoota together; TAB uses the selected model's portrait too. Duplicate Mechanic/Pistoleer choices map to Merchant/Bodyguard for legacy configs. HUD has separate local Floating names, Players on compass and Players on map switches, including a colored self arrow on the map. Automatic update checks are suppressed for now. Latest Windows Release/deployed DLL MD5: `747D3E92FA5400531E4D9CD36B73EDA4`; gameplay remains Testing. See [character and TAB tests](compat/ApocalypterMP/CHARACTER-BODIES.md) and [player HUD/mod matching tests](compat/ApocalypterMP/PLAYER-HUD-AND-MOD-CHECK.md).

Player recovery (Testing): every five minutes, the host writes a small player checkpoint without starting a world save or pausing gameplay. It covers native carried weapons/gear, ammo, health and armor; clean client exits send a final report. An unclean reconnect to the still-running host recovers the latest validated player record and displays a top-left CleanHUD notice. Every full world save embeds all known players, including those who left. **Every world reload restores everyone to that save's player snapshot**, discarding newer checkpoints even after a host crash. Existing worlds need one full save with this build to establish rollback protection. Details, storage paths and manual tests: [PLAYER-RECOVERY.md](compat/ApocalypterMP/PLAYER-RECOVERY.md).

Optional mod recommendations are hidden by default. Enable them through Wild Wasteland settings > Debug > Core > Switches > Recommended mods (Enable Debug must be on). This adds settings.cfg `[Debug] Recommended mods = false`; it does not change multiplayer endpoint settings.

The MULTIPLAYER panel has a NAME field for your player display name (1-48 characters). Click it and type, then Enter to save or Escape to cancel; HOST/JOIN also apply it. Change it before connecting. The name persists in settings.cfg `[Multiplayer] Name`; an empty config entry retains native Steam/launcher naming. This changes display names, including chat and save requests, while preserving player/save identity.

The public mod/assembly is named WildWastelandMP. The original plugin GUID, namespaces and configuration/save keys remain for compatibility; do not load base WildWasteland alongside this build.

Current checkpoint and known verification gaps: `compat/ApocalypterMP/STEP-16-PLAYER-PRESENTATION.md`.

Native ApocalypterMP routine logs follow WW settings > Debug > Core > Logging > VerboseLog. After the bridge initializes, known native log messages are shown in English; warnings/errors stay visible with verbose off. The native one-time startup messages precede this filter. MULTIPLAYER actions are four vertical rows: HOST (STEAM), HOST (IP), JOIN, DISCONNECT. JOIN uses UDP; Steam guests accept an invitation from the main menu. See [logging/menu retest](compat/ApocalypterMP/LOGGING-MENU.md).
