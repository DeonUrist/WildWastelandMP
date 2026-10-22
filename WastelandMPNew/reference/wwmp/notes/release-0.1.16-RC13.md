# Wild Wasteland 0.1.16 RC13

The main menu now shows UPDATE AVAILABLE in the top right, labeled WILD WASTELAND, when the mod's own GitHub releases contain a version newer than the installed build. Clicking opens that release page. The check includes experimental prereleases and runs asynchronously once per game launch, using the request/version-comparison approach from Apocasetter without depending on its index or installer. It does not install updates automatically.

The button follows Clean menus and appears only on its title screen, outside settings and confirmation dialogs. Offline, timeout or GitHub API errors leave the button hidden and write a warning; gameplay and menus remain available. Disabling the CleanHUD module cancels an unfinished request. No config entries, assets or save formats change.

Install by extracting the archive into BepInEx/plugins. RC12 assets are retained; ChaseCamera, Engine Sim, Talk and Quests remain optional separate installs. This is an experimental prerelease; in-game status remains Testing.

Validation: Windows Release build with Deploy=true passed with zero errors and seven existing warnings. Built/deployed DLL MD5: B36AB2F0B4FAD88150352C785A9958D7. Agents do not run the game.

Test: start the game with Clean menus enabled and wait up to 20 seconds at the main menu. Expect Loading [Wild Wasteland 0.1.16] and `WW update check: installed 0.1.16; no newer release` while GitHub has no later version. After a newer release is published, restart: expect UPDATE AVAILABLE with WILD WASTELAND beneath it, plus `WW update check: installed 0.1.16; update available <version>`. Clicking must open that newer release page. Open settings or a confirmation dialog: the button must hide, and return on the main screen. It must remain absent in the pause menu. Without connectivity, expect a `WW update check:` warning and no button.
