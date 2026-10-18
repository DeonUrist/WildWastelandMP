# Issue #3 — Multiplayer UI (Testing)

2026-10-18. Denis approved the approximately 100–150-line Core/CleanHUD scope. No commit, push, issue closure or game automation.

Cause: WW suppressed ApocalypterMP's native UI while retaining F5/F6/F7/F8 connection shortcuts. CleanHUD had no corresponding actions. Native JoinWorld rejects an already-loaded game; the title-menu restriction must be respected. Hosting must register the existing SaveFile/seed through HostWorld instead of opening an unbound transport.

Changes: approximately 145 changed code lines in src/Core/Multiplayer.cs and src/Modules/CleanHUD/Menu.cs (excluding concurrent vehicle changes). Both Clean menus screens show the shared multiplayer panel, session status, IPv4/port editing and HOST, HOST (STEAM), JOIN and DISCONNECT. Hosting is disabled without a loaded world; connection/edit actions are disabled during an existing session. ESC JOIN confirms returning to the title menu, where JOIN starts the connection. DISCONNECT uses native End, including its connected-guest return to the title menu, and native host closing notification. MP F5–F8 are removed; existing F9–F12 suppression remains. The offline native status hint now points to the menus.

No new configuration entries or save format. Endpoint changes write the existing ApocalypterMP UdpAddress/UdpPort entries. The BAT's one-time auto-start remains intact. Clean menus must be enabled. The new optional Harmony target is NetSession.get_Status; all delegates are cached at initialization.

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true` succeeded: zero warnings, zero errors. Built/installed DLL MD5: `3EBE1810C8AA5D96CB507312BE18D19B`. Installed at E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll. Unique retained build: V:\Repos\Apocalypter\_staging\wwmp-issue3-20261018-153019\WildWastelandMP_issue3_20261018-153019.dll. Build includes the shared tree's concurrent vehicle work; that work was not changed by this task.

Denis's runtime checks:

1. At the title menu, verify HOST and HOST (STEAM) are disabled. Load/start a world, open ESC, and verify they become enabled while offline.
2. With no other listener on the chosen port, click HOST. Expect HOSTING, disabled host/join/address/port buttons, and enabled DISCONNECT. For local testing use port 27861 on both copies. The BAT auto-hosts once, so disconnect before testing manual hosting.
3. On the second copy at the title menu, click HOST IP and type 127.0.0.1; Enter applies. Enter matching UDP port 27861 and click JOIN. Expect CONNECTING followed by the host world loading and CONNECTED. Invalid IP/port should show a validation message without opening a session; Escape while editing cancels without closing ESC.
4. Guest ESC > DISCONNECT > YES should return the guest to the title menu. Host ESC > DISCONNECT > YES should stop hosting while preserving its loaded world. HOST and JOIN should work again without restarting either application.
5. In an offline loaded world, ESC > JOIN > NO stays in that world; YES returns to the title menu. Save first. Click JOIN there to connect. F5–F8 must no longer start/stop/join MP; unrelated settings hotkeys retain their behavior.
6. Check both title and ESC layouts, address/port edit cancellation, settings/modals, loading-screen input blocking, and optional Steam hosting in a normal Steam session. No runtime/visual/Steam acceptance is claimed by the build.

Confirming log lines: `[Multiplayer] Hosted current world <slot> over UDP`, `[Multiplayer] role: host (WW world authority)`, and on a matching client `Matching WW patch accepted; host world settings applied before world launch`. A missing line must be interpreted against the installed DLL MD5 because other sessions can deploy concurrently. Earlier step16 checks remain pending independently of this UI change.
