# Wild Wasteland 0.1.15 RC12

F3 → Debug starts with a Logging section: general VerboseLog first, followed by the other Wild Wasteland verbose/log switches with their section names. Those switches are removed from their former positions lower in Debug. Existing config keys, defaults, saved values and logging behavior are preserved; NPC verbose traces remain independent of general logging. Enable Debug in F6 to show the Debug page.

CleanHUD's optional ChaseCamera 0.2.5 speed/RPM/gear display no longer inherits the camera's scope/binocular SpecialView gate. The diagnostic reproduction stayed at `special view` during normal driving in both views, then changed to `live` with 880 RPM immediately after ESC. Instruments now follow live driving, their existing HUD setting, pause and focus checks. Explicit source telemetry capture still supports first-person driving, and ChaseCam HUD Off/On remains at the bottom of Game → HUD. The speculative extra native-hide render callback was reverted. The original ChaseCamera camera behavior and DLL remain unchanged.

Install by extracting the archive into BepInEx/plugins. RC11 assets are retained; ChaseCamera, Engine Sim, Talk and Quests remain optional separate installs. No new config entries or save formats are introduced. In-game status remains Testing.

Validation: Windows Release build with Deploy=true passed with zero errors and seven existing warnings. Built/deployed DLL MD5: F27D066F3CB761350960206ED84EE256. Agents do not run the game.

Test: enable Debug in F6, open F3 → Debug, and check general VerboseLog first with the module log switches immediately below. Toggle a switch, reopen the menu and confirm it persists without duplicate rows lower in Debug. Restart, spawn a car and sit without pressing ESC: speed/RPM/gear should appear in first and third person. Check the ChaseCam HUD Off/On control, pause/resume and leaving the car. With general VerboseLog on, expect `ChaseCamera readout state: live`, snapshot age 0 and RPM active True while driving, even if raw special view remains True. Startup should report Loading [Wild Wasteland 0.1.15].
