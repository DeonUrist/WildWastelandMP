# Wild Wasteland 0.1.17 RC14

The J quest journal now uses CleanHUD when Clean HUD is enabled. The previous bridge styled dialogue, notifications and the tracker but left the journal in the quest mod's original window.

The replacement uses CleanHUD's panel and atlas font, gold quest headings, the original completed count and full journal text, including reputation, rewards, objectives, directions, deadlines and giver names. Long journals scroll with the mouse wheel. Rows are reused and text is refreshed only when the source journal changes.

Apocalypter Quests retains its J/rebound-key open/close controls and unpaused behavior. Disabling Clean HUD or its module restores the original journal. Only the source journal drawing is suppressed while the replacement is visible; its tracker visibility and toast expiry remain intact. No config entries, assets or save formats change.

Install by extracting the archive into BepInEx/plugins. All RC13 assets are retained. Apocatalker 2.0.0 and Apocalypter Quests 1.8.0 remain separate optional installs, including their original data files; ChaseCamera and Engine Sim remain separate too. This is an experimental prerelease; in-game status remains Testing.

Test:

1. Restart and load a save with the two quest mods and Clean HUD enabled. Confirm `Loading [Wild Wasteland 0.1.17]` and `[CleanHUD] Quest journal bridge ready; CleanHUD off restores the original J window` in LogOutput.log.
2. Press J (or the rebound quest-log key). Expect one CleanHUD journal with gold quest headings, the correct completed count and all current quest/reputation text. The game continues running as before.
3. With enough active quests to exceed the panel height, scroll both ways with the mouse wheel; all entries should be readable. Press the quest-log key to close and reopen; it starts at the top. Objectives and deadlines should update while open.
4. Open ESC/settings or load another save: the journal must hide. Disable Clean HUD, then press the quest-log key in play: expect the original journal. Re-enable Clean HUD and repeat; no duplicate window should appear.

Validation: Windows Release build with Deploy=true passed with zero errors and seven existing warnings. Built/deployed DLL MD5: C7C78A9D97BF79270B60539916EE1937. Agents do not run the game.
