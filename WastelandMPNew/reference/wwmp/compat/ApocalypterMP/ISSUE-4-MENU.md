# Issue #4 — MP UI (Testing)

Cause: Menu.BuildMain unconditionally created the right-side multiplayer panel, and ReplacementGuard unconditionally displayed missing optional mods at the title menu. Neither had the requested opt-in control. The issue has no comments; existing logs confirm CleanHUD title/pause attachment and native current-world hosting.

Changes: 33 added/removed code lines across src/Modules/CleanHUD/Menu.cs and src/Core/ReplacementGuard.cs, preserving pending #1/#3 work. Both title and ESC navigation now offer MULTIPLAYER; only clicking it creates the existing panel to the right. Main navigation stays visible, X/Escape returns to the main screen, and Escape while editing an endpoint cancels only that edit. The existing sub-screen/reset lifecycle removes the panel on scene/menu transitions. Confirmations retain panel ownership so notices do not overlap it. Host/join/disconnect behavior is unchanged.

New configuration entry: settings.cfg [Debug] Recommended mods, default false. The existing Debug > Core > Switches catalog presents it (Enable Debug required). Recommendation visibility and panel sizing read it live. Restart-required replacement warnings remain independent. No save format, endpoint config, replication schema or Harmony target changes.

Validation/deployment: Windows dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true succeeded with zero warnings/errors after Denis confirmed both games closed. Built/installed/unique staging MD5: 52A8C9ED1837040A94D92F452E6C236B. Installed DLL: E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll. Retained staging copy: V:\Repos\Apocalypter\_staging\wwmp-issue4-20261018-154253\WildWastelandMP_issue4_20261018-154253.dll. Apocasetter disabled manifest was absent. No game launch, runtime acceptance, commit/push or issue closure.

Denis's runtime checks:

1. Start at the title menu: neither recommendations nor the MP panel should appear by default. MULTIPLAYER opens the panel to the right of the still-visible main menu. HOST/HOST (STEAM) remain disabled here.
2. Close with X; open again and close with Escape. Edit HOST IP/UDP PORT and press Escape: only the edit cancels, retaining the panel. A second Escape closes it.
3. Load a world, open ESC: the panel starts closed. MULTIPLAYER opens it; HOST works while offline. Check JOIN's quit-to-menu confirmation and DISCONNECT's confirmation, including NO/Escape followed by X. Existing session safeguards remain.
4. Switch to SETTINGS, return, close/reopen ESC, and return to the title menu. The panel should require another MULTIPLAYER click. Existing Clean menus styling and accents apply.
5. Enable Debug, then Debug > Core > Switches > Recommended mods. At the title menu, missing optional recommendations should appear; opening MULTIPLAYER hides them until that panel closes. Toggle off again and confirm they disappear. If all recommended mods are active, there is no missing-mod list to show. Replacement restart warnings, when applicable, must still show with the toggle off.

No new runtime log lines are needed for this visibility change. Existing hosting confirmation remains `[Multiplayer] Hosted current world <slot> over UDP`. Visual/input acceptance belongs to Denis.
