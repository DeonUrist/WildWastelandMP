# Wild Wasteland 0.1.18 RC15

CleanHUD menu quality-of-life improvements:

- Controls now has a real draggable scrollbar. Dragging its thumb scrolls in the expected direction; dragging the list background no longer moves it. Mouse-wheel scrolling remains available.
- UPDATE AVAILABLE now has a dark weapon-info-style panel behind the notice and WILD WASTELAND label. Its existing version check and release-page link remain unchanged.
- Smaller DISCORD and GITHUB buttons stack vertically at the bottom right of the title and pause main screens, with matching dark frames and space for the MODS button below. They open https://discord.gg/f56btNbPg and https://github.com/DeonUrist/WildWasteland respectively. They hide in settings and confirmation dialogs and follow Clean menus.
- The red duplicate-key warning is removed from the main screen. Duplicate counts and conflicting bindings remain visible inside Controls.

Install by extracting the archive into BepInEx/plugins. All RC14 assets are retained. No config entries, assets or save formats change. Optional Talk, Quests, ChaseCamera and Engine Sim mods remain separate installs. This is an experimental prerelease; in-game status remains Testing.

Test:

1. Restart with Clean menus enabled. Confirm `Loading [Wild Wasteland 0.1.18]` and `Menu attached to Canvas_MM (Title)` in LogOutput.log.
2. Check the smaller framed DISCORD/GITHUB stack at the bottom right. Click each and confirm its destination; ensure MODS remains accessible below. Repeat in the pause menu.
3. Open Settings > Controls and choose a category with enough entries to scroll. Drag the thumb up/down, click the scrollbar track and use the mouse wheel. The list should follow correctly; dragging its background should leave it stationary. Rebinding and tab changes should still work.
4. With duplicate bindings present, the main screen must have no red key warning. Controls must retain its duplicate count and conflicting-binding information.
5. Settings and confirmation dialogs must hide the community buttons; returning to the main screen restores them. Clean menus off removes the custom menu controls.
6. When a newer release is available, check the framed UPDATE AVAILABLE notice and click it to open the newer release. On this matching release, expect `WW update check: installed 0.1.18; no newer release` until a later version exists.

Validation: Windows Release build with Deploy=true passed with zero errors and seven existing warnings. Built/deployed DLL MD5: CC979590833FD968FA4D17E29BC6D065. Agents do not run the game.
