# Hosting, settings navigation and save requests — Testing

## Starting vehicle selector (2026-10-18)

Denis requested a dropdown during initial hosting and chose the six normal starter vehicles. New world setup now has Starting vehicle: RANDOM (default), Crossbreed, Halfbreed, Junker, PipeRat, Poloska and TinyTyrant. The existing LedgerDropdown draws over the form and owns pointer input before underlying controls; its anchor converts out of the scrolled content into window coordinates. The choice belongs only to this setup page and pending new-world launch. Existing saves and ESC hosting do not offer it.

Native source evidence: Sources/fsm/scene1_game/Starter_Area.md:86-120 selects a CarSpawn prefab and index before native creation/naming/car registration/randomization/fuel. Its EngineSpawn_StarterBase getIndex state (:223-231) reads that index to choose compatible engines, radiators and motorcycle wheels. Sources/data/hierarchy/scene1_game/Starter_Area.txt:13 supplies the six vehicle prefabs in native index order. The hosting form previously had no selection or launch override. MultiplayerHosting now subscribes to the existing shared CreateObject prefix, filters the exact starter spawner while an explicit new-host choice is pending, resolves the chosen native prefab in its live CarSpawn list and updates the matching index before original creation. RANDOM runs the native path. The selection clears after use or launch completion/failure. A missing prefab warns and retains the native random result.

Three source files: Core/MultiplayerHosting.cs, Core/UI/HostSettingsPage.cs and Core/UI/ToolWindow.cs; 48 added/4 removed lines relative to the pre-task files. Shared hook inventory updated. No new config entries, protocol, save format, base-repo or external DLL changes. Prior pending work retained.

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true -v:minimal` passed with zero warnings/errors. Built, installed and unique stage MD5: `4EBC4C4B73CE43CBA754F52736159D02`. Stage: `bin/Release/WildWastelandMP_starter-vehicle_20261018_221618.dll`. Both game processes were closed; disabled WW manifest absent. No game launch, harness, commit or push. Runtime presentation and starter-kit matching remain Testing.

Denis's checks:

1. At title MULTIPLAYER > HOST (IP) or HOST (STEAM), select New world. Starting vehicle must show RANDOM. Open the dropdown, scroll to all six choices, select Junker, enter a world name/seed and HOST WORLD. Only the selected starter should spawn; expect `[Multiplayer] Starter vehicle selected: Junker` in LogOutput.log.
2. Repeat new-world creation for Poloska, PipeRat, Crossbreed, TinyTyrant and Halfbreed. Each selected model should spawn with its native compatible engine/spares and usual starter fuel/items. The confirmation line should name that selection once. A joined guest should see the same host-world vehicle.
3. Create a world with RANDOM after an explicit selection. Native random selection must return without the explicit-selection confirmation. Cancel/reopen setup: RANDOM must be the default again. Dropdown clicks must not toggle underlying world settings; outside clicks close it.
4. Choose Existing save and host an already loaded world through ESC: Starting vehicle must be absent, and saved vehicles must remain as saved. Save/reload a chosen-car world normally; selection must not spawn an additional starter.

## Host F3 follow-up (2026-10-18)

The creation form was retained in ToolWindow._current after CloseThen started hosting. F3 uses Open(null), which reused this transient HostSettingsPage; DrawWindow therefore kept hiding normal navigation. ToolWindow.Close now replaces that page with PLAYER and clears its scroll. Regular settings retain their last normal page. ToolWindow.Visible also hides WORLD for clients; host/offline pages and existing Debug/Developer visibility remain available.

Only ToolWindow.cs changed (2 added/1 removed code lines). No config, protocol, save or base-repo changes. Windows Release/Deploy=true succeeded with zero warnings/errors. Built/installed/staged MD5 `44EC620EFD187397735D5D5A16C1DC00`; stage `bin/Release/WildWastelandMP_host-f3_20261018_211733.dll`. Both game processes were closed and the disabled WW manifest was absent. No game launch, harness, commit or push. Earlier build hashes below are historical.

Manual checks: start hosting from a new world, an existing save and an already loaded world, then press F3: normal PLAYER/WORLD/HUD/Modules/Performance navigation must appear, plus Debug/Developer when their existing flags are enabled. HOST WORLD/CANCEL must appear only after explicitly opening host setup. Cancel setup with CANCEL, Escape or F3 and reopen F3: normal settings must appear. A joined client must see PLAYER/HUD and editable local preferences without WORLD. Disconnecting must restore WORLD. Normal page selection should survive close/reopen. Existing `[Core] Settings opened: CleanHUD atlas UI, 54-unit rows` confirms opening but does not certify the page layout; no new log line was added. Runtime acceptance remains Testing.

Latest follow-up: HOST is now a focused full-width setup screen without the F3 left navigation or accent palette, with persistent HOST WORLD/CANCEL footer actions. Hurricane density, sand-front width and storm depth moved from WORLD/host setup to F3 > Debug > Weather > Storm tuning in both repos, retaining their existing entries/values/ranges and removing generic Developer duplicates. Windows builds passed zero warnings/errors; current MP installed/staged MD5 `2F2808002CF64A2E9279A49AE9927A84`, stage `bin/Release/WildWastelandMP_host-ui_20261018_163222.dll`. Current base repo-only DLL MD5 `F8EBCFD6C9B10A780940F4A60F730F6B`; WW #144 checklist includes the weather placement. Runtime retest: HOST must show only its setup and HOST/CANCEL, with CANCEL returning without hosting; normal F3 still shows PLAYER/WORLD/HUD. With Enable Debug on, verify all three tuning entries under Weather, absent from WORLD/host setup, preserving edits after reopen/reload. Historical build details below describe the preceding checkpoint.

2026-10-18. Denis requested separate PLAYER/WORLD/HUD F3 pages, named host setup with cars/patrols/navigation policy, title-menu save/new-world selection with a seed, a BAT that stays at the title menu, correct host save targeting and player-name save requests from clients. He also requested the F3 split in base WW and one combined backport test issue on the Wild Wasteland board.

## Implementation

- CorePages registers PLAYER, WORLD and HUD separately. GamePage remains the PLAYER reference for existing menu/new-game callers; each page keeps the shared atlas, settings entries and RESET behaviour. World/navigation authority stays host-owned in MP. The host form reuses DrawWorld so its controls match F3.
- CleanHUD MULTIPLAYER > HOST / HOST (STEAM) opens HostSettingsPage. Title setup chooses a new native MP world with a seed or an existing valid MP/SaveGame1–10 file. It reads the native WorldStore list for names and enumerates the active save directory only when opening the form. No new scan/job runs each frame.
- MultiplayerHosting uses native WorldStore.Create and GameLauncher.Launch. Existing saves launch in place; it waits for the loaded isPlay world, then HostCurrentWorld reads that world's real seed and SaveFile before native HostWorld. The world name is the session display name, distinct from the selected save-file identity. ESC setup hosts the already loaded slot. Native MP world catalog/save formats remain unchanged.
- Host Save rebinds native SaveLoadGame.SaveFile/default ES3 path to ActiveSaveFile before the existing server-only save transaction. A guest receiving a host slot accepts only the bounded existing MP filename syntax or native SaveGame1–10. This lets hosting an existing native slot retain its identity through WW sidecars.
- CleanHUD and native client Save controls send reliable packet 142 with an empty payload. The host validates the connected player and rate-limits each sender to five seconds. Native NetSession.NameOf(peer) supplies the displayed name; Chat.Notice and the log show `<name> wants to save`. The client sees an acknowledgement. This request does not execute a save. Existing host save barriers/flush ordering remain.
- MultiplayerLaunch retains role identity, save/settings isolation, graphics and window labels, but removes automatic launch/hosting/joining and ready-file waits. The deployed BAT opens both copies at the title menu for manual HOST/JOIN. Script inheritance clears old WWMP_READY/TOKEN values.
- Handshake revision 6 requires both peers to use the new capabilities. No new config entries, dependencies or external DLL edits.

## Builds and deployment

MP Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true -v:minimal`: zero warnings/errors. Built/installed/unique stage MD5 `4C37D05799C80E1CD23382667218BAA1`. Stage: `bin/Release/WildWastelandMP_host-setup_20261018_162358.dll`. Installed DLL: `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`. Both launcher scripts copied to the game directory; source/installed PS1 MD5 `F9701211864E95AA26025AE667B500C4`.

Base WW: only PLAYER/WORLD/HUD backported now (CorePages/ToolWindow, 24 added/12 removed lines). Its earlier traveller hearing is already released in RC34 (`ecd378b`, changed in a separate session). Windows Release/Deploy=false: zero warnings/errors; current combined DLL MD5 `3E0EBDD4A38D1164149A20BB949E0790`. No base deployment. One combined backport test issue: https://github.com/DeonUrist/WildWasteland/issues/144, Wild Wasteland project 4, status Testing. No MP hosting/pause/BAT/save-request code backported.

Prior pending changes are retained. No game launch, automated gameplay test, commit, push or release performed by this task.

## Denis's MP retest

1. Start the installed BAT. Both windows must remain at the title menu: no world load, listener or join before an explicit action. Expect `[MP launcher] <role> ready at the title menu; choose HOST or JOIN manually`.
2. In HOST's title MULTIPLAYER panel choose HOST, enter a name and seed, choose cars/foot patrols/map/compass policy and start. Wait for the loaded world, then manually JOIN from the client. Both peers must pass revision 6; guests inherit world policy. Try an empty name/bad seed/missing save: setup should reject it without starting.
3. On a separate launch choose an existing MP save and then an existing native SaveGame slot. Confirm the real saved world/seed loads rather than a newly generated world or imported copy. HOST from an offline loaded world through ESC should retain that same slot.
4. Press host Save: the normal barrier should run; `Guardado: ... (<selected file>)` and `Server save: gameplay paused/resumed` should identify the selected slot. Reload it and confirm changes persist. Never treat an untouched different save slot as the target.
5. Press client Save in CleanHUD, and with Clean menus disabled in the native ESC menu: only a request should be sent. The host must see the actual connected name followed by ` wants to save`; repeated presses within five seconds should not spam it. The host still decides when to save.
6. Check F3 left-side PLAYER/WORLD/HUD, previews, existing config persistence/reset and host-only world navigation policy. UIs must retain the previous MP live-world behaviour. Base standalone checks live in issue #144.

Actual UI, reflection binding, Steam/UDP lifecycle, slot persistence and gameplay acceptance remain unverified until Denis performs these checks. Keep status Testing.
