# WildWastelandMP local testing

Active repo: `V:\Repos\Apocalypter\WildWastelandMP`. This fork contains the complete compatibility overlay from base `91bff52`, plus the current presentation and launcher fixes. Changes are uncommitted. The original WildWasteland repo is untouched.

Build from Windows PowerShell:

```powershell
dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true
```

The DLL deploys to `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`. Close both copies before rebuilding/deploying. Assets live beside that DLL. ApocalypterMP.dll remains a separate dependency.

Double-click `Start-WildWastelandMP.bat` in the repo or the game folder. It launches a visible host and a visible client, starts UDP hosting, waits for world readiness, then joins the client. It does not run automated gameplay tests. Optional arguments: `host` or `client` to launch only that role.

The host uses the main game executable. The client uses `MP\Client` inside the game folder, sharing the installed plugins/data through directory junctions, with separate BepInEx settings. Launcher saves/settings.xml live in `MP\Saves\host` and `MP\Saves\client`; launcher Player logs are `MP\Logs\host.log` and `client.log`. The default world save is `MP_WWMP-Local.es3`. An existing first-launch `WWMP_Local.es3` and its player registry copy forward when the canonical save is absent; the originals stay intact.

Direct launches can report SteamAPI initialization failure. This BAT uses UDP at `127.0.0.1:27861`, independently of Steam. Normal menu/Steam sessions keep their ordinary identity/path behavior.

After quitting/rehosting inside an already running host window, load the intended world and press F6 on the host to reopen UDP, then join the client with F7. The BAT auto-start behavior runs once per application launch. F8 disconnects. F9–F12 MP debug actions remain blocked.

The public mod/assembly is named WildWastelandMP. The original plugin GUID, namespaces and configuration/save keys remain for compatibility; do not load base WildWasteland alongside this build.

Current checkpoint and known verification gaps: `compat/ApocalypterMP/STEP-16-PLAYER-PRESENTATION.md`.
