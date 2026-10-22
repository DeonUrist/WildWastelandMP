# MP logging and vertical menu — Testing

2026-10-18. Denis requested native MP routine logs only with WW global VerboseLog, English log text, and four vertical MULTIPLAYER actions: HOST (STEAM), HOST (IP), JOIN, DISCONNECT. The overflowing explanatory hint is removed. Denis explicitly approved the larger translation-table scope and accepted the native one-time startup lines before our bridge loads.

## Cause and implementation

ApocalypterMP writes directly through its own ManualLogSource, bypassing WW Log.Info. New Core MultiplayerLogging installs a Core prefix on BepInEx ManualLogSource.Log(LogLevel, object), following the existing Vistas exact-source pattern. It filters only the native Plugin.Logger instance. Info/Debug/Message follow the existing live Log.Verbose value; warnings/errors/fatal remain visible and are translated. The bilingual lag-simulation warning retains one English copy only with verbose on. Other log sources keep their original behavior.

Anchored translation templates cover the supplied native DLL's connection, Steam/UDP, save, player, object, vehicle, survival, merchant, world and diagnostic messages. Exact translations precede template matches; captured names, IDs, paths, numbers and exception details are preserved. Known multiline diagnostics are translated line by line. Unknown messages from newer MP versions pass through verbatim rather than disappearing. Native one-time Plugin.Awake messages already emitted before WW loads remain as explicitly accepted; no earlier hook or external DLL rewrite.

Multiplayer.cs adds one install call. MultiplayerLogging.cs contains the filter and translation table (about 290 lines). CleanHUD/Menu.cs replaces the two-column actions with four full-width rows in the requested order, expands the panel from 500 to 550 units and removes the hint (about 18 changed lines). No new config, packets, gameplay, saves or base-WW changes.

Native DLL inspected and left unchanged: SHA256 `BF0528817B0575A00E7A03F9E520DDACCBEE0322328369CE97351F433633CE2E`.

## Manual retest

1. Restart both copies with the new DLL. At title and ESC, open MULTIPLAYER: four vertical full-width actions in the requested order; no explanatory hint or text extending outside the panel. Confirm HOST (IP) and HOST (STEAM) still open the corresponding setup, JOIN still uses UDP, and DISCONNECT retains its confirmation.
2. With F3 > Debug > Core > Logging > VerboseLog off (Enable Debug must be on to expose Debug), host/join, enter/leave a car, pick up/drop an item, save and disconnect. Native routine MP logs should be absent after bridge startup. WW's own startup/state lines and real warnings/errors remain.
3. Enable that same VerboseLog live on both peers. Repeat: expect English native lines such as `UDP: waiting for players on port ...`, `Network: ... session started as host`, `Save: completed in ...` and `Cars: ...`. Names/IDs/paths/values should match the session. Turn verbose off and confirm routine output stops immediately.
4. If testing Steam, accept an invitation while the guest is at the main menu. With verbose on, expect `Steam: lobby created (...)` and `Steam: connected to ...`. These messages show the relevant native phase; they do not by themselves prove gameplay acceptance.
5. For an ordinary failure, join an address with no listening host and inspect diagnostics; warnings/errors must remain visible in English even with verbose off. Do not interpret silence during retry-only Info logging as an error being hidden.

Only a Windows Release build is authorized for agent verification; actual logging, Harmony installation and layout remain for Denis to test. No game launch, automation, harness, commit or push.

## Build and deployment

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true -v:minimal` passed: zero warnings/errors. Both game processes were closed; disabled WW manifest absent. Built, installed and unique stage DLL MD5: `8A3C596F699435C6650E47BDD1D4FE03`. Installed: `E:/SteamLibrary/steamapps/common/Apocalypter/BepInEx/plugins/WildWastelandMP/WildWastelandMP.dll`. Stage: `bin/Release/WildWastelandMP_logging-menu_20261018_211534.dll`. Runtime acceptance remains Testing. No game launch, harness, commit or push.
