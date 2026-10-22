# Multiplayer menus leave simulation running — Testing

2026-10-18. Denis explicitly requested that ESC, menus, F3, inventory and other UI never freeze multiplayer simulation. This follow-up applies only to WildWastelandMP; the base-repo hearing fix remains separate.

## Cause

- Native MP PauseControl already suppresses Menu/UseSave ScaleTime and keeps ordinary MP time at 1. The native Menu FSM remains in `pause` while ESC is open (`Sources/fsm/scene1_game/__GameManager__.md`, Menu states).
- WW GameRefs.InGame still required Menu `play`. Patrol.InGame consequently stopped WorldTraffic updates (including foot travel orders), with similar local-menu gates in weather, POI, quests and stew simulation.
- EditorSession already skips its own pause in MP, but Apocasetter InputBlocker.Set independently sets timeScale to 0. Its settings LateUpdate writes 0 again while open. Source: `V:\Repos\Apocalypter\Apocasetter\Plugin.cs` InputBlocker.Set; `SettingsUI.cs` LateUpdate. This also affects F3/inventory when they borrow the blocker.

## Change

46 code lines changed (26 added/20 removed) in 15 files. GameRefs.WorldRunning checks loaded isPlay, positive time and the save barrier independently of local Menu state, falling back to existing InGame offline. Existing input/presentation gates stay local. Patrols, POI, weather, quest progression/preparation, NPC appearance preparation and stew simulation use WorldRunning; quest/stew interactions retain local input gating.

The optional Apocasetter bridge post-hooks its Set and settings LateUpdate to leave a loaded MP world's time at 1, while retaining input/cursor ownership. Frozen saves keep time at 0, including if a UI closes during a save. No Apocasetter DLL/source edits, new jobs, config entries, save formats or protocol changes. Native MP's existing ESC ScaleTime protection is retained.

## Build and deploy

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true -v:minimal` passed with zero warnings/errors after confirming both game processes were closed. Built/installed/unique staged MD5: `22E98DDC7F5F3873786F84C26DA12CDC`.

Stage: `bin/Release/WildWastelandMP_ui-live_20261018_161053.dll`. Installed: `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`.

Previous hearing and concurrent MP work are retained. No base repo edit for this request, game launch, automated runtime test, commit or push.

## Denis's runtime retest

1. Join host/client, stand near a moving foot group or car patrol. Open host ESC for at least 15 seconds; the guest should see continuous AI movement/combat. Resume: there should be no catch-up jump caused by menu-paused traffic orders.
2. Repeat with host F3, TAB inventory and Apocasetter/MODS UI. Confirm the host's cursor and UI work while player movement/fire do not leak through; the guest sees an uninterrupted world. Repeat on the guest.
3. Open settings from ESC, close back to ESC, then resume. Check no lingering pause, lost cursor lock or unwanted movement/fire.
4. Check weather and an active stew continue while menus are open. A server save must still freeze both peers for its transaction and resume them; existing `Server save: gameplay paused` / `gameplay resumed` logs confirm the save barrier remains.
5. After disconnecting to offline, verify the normal ESC/F3 pause still works. Check startup contains no Apocasetter hook errors or patch conflicts. Gameplay and actual optional-hook installation remain unverified until these human checks.
