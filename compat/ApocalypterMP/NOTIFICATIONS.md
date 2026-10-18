# MP loading overlays and top-left system notices — Testing

2026-10-18. Denis requested no extra MP messages on loading screens and normal top-left notifications during gameplay.

## Source and change

Supplied native source (`_staging/ww-mp-20261018/inspection/ApocalypterMP.decompiled.cs`) shows ModRunner.OnGUI drawing the raw network status label before seven UI calls. Chat.Notice stores system lines for its lower-left chat history; Hud.Show stores large centre-screen messages without a loading guard.

Core MultiplayerNotifications now wraps the existing native GUI pass. It forwards the same seven UI methods, in order, through cached delegates and their existing Harmony gates, omitting the raw debug label. The complete MP pass is skipped while LoadingProgress.MenusBlocked or SaveBus load/save flags are active. Chat input/history for actual player chat, gameplay prompts and downed/revive rendering retain their normal native paths outside loading.

Chat.Notice messages before world readiness or during loading are discarded rather than queued. Live Chat.Notice and Hud.Show messages use CleanHUD's existing MessageHook/MessageLog through a provider owned by CleanHUD's Init/Shutdown. They receive the same top-left style, repeat handling and expiry as other notifications, without duplicate native system history/centre text. Empty Hud.Show still clears its native state. Native title errors and CleanHUD-disabled fallback remain available outside loading.

Four source files: Core/MultiplayerNotifications.cs (new), Core/Multiplayer.cs (install), CleanHUD/CleanHudModule.cs (provider lifecycle), CleanHUD/MessageLog.cs (provider). Roughly 50 added lines. No new config entries, jobs, UI canvas, protocol/save format, base-repo changes or external DLL edits.

## Build/deploy

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true -v:minimal`: zero warnings/errors; game processes were closed. Built/installed/unique stage MD5 `2E8E57D6DD745C55AB1079E845D72ED8`. Stage `bin/Release/WildWastelandMP_notifications_20261018_164013.dll`; installed DLL `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`.

## Denis's retest

1. Host a new/existing world, then join from the other copy. Loading artwork/progress must have no raw network status, chat-system history, centre MP notices or other MP overlays. Messages received during loading must not replay afterward.
2. Once loaded, join/leave notifications and a client's Save request/acknowledgement should appear in the same top-left log as ordinary game notifications, without duplicate native notices. The host request must retain the sender's name.
3. Save with a guest connected: the normal shared loading/save presentation remains, with no extra MP overlays. Save ordering/freeze/release must behave as before.
4. Verify ordinary chat typing, interaction/revive prompts and downed UI still work outside loading. Confirm F3/inventory/ESC retain world simulation behaviour.
5. Check startup has no `Multiplayer notification hook failed`, Harmony patch error or lost native UI method. No new confirmation log is emitted by the presentation bridge; existing save-request logs can identify a named request independently of its display.

Only compilation/deployment are verified. Denis performs runtime checks; no game was launched or automated, and nothing was committed/pushed by this task.
