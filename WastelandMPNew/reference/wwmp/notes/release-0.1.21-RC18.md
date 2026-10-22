# Wild Wasteland 0.1.21 RC18

Parked merchant buses now switch ignition off, apply the handbrake and suspend driving, automatic engine restarts and startup/revive routines. Previously the route wait only applied brakes while the pilot could continue retrying a stopped engine. The streamed incident could not be confirmed from a runtime log; this change implements an explicit merchant wait.

The existing five-minute stop requested by Alt+Q or a vehicle horn remains active while the player is within 100 m, even after five minutes have elapsed. Once the player leaves that radius and the minimum wait has elapsed, the crew uses its normal startup path to resume the route. Setup shops remain parked; merchants that have reached their route endpoint remain stopped under the existing route lifecycle.

Stops use the existing VehicleIgnition service for a coupled Engine Sim engine and native controls otherwise. Only an enabled native START FSM is turned off; Engine Sim's disabled FSM remains under its ownership. No Engine Sim source or DLL changes, new config entries, save keys or save-format changes.

Extract the archive into BepInEx/plugins. All RC17 assets are retained. Optional Talk, Quests, ChaseCamera and Engine Sim remain separate installs. This is an experimental prerelease; in-game verification remains Testing.

Test:

1. Restart and confirm Loading [Wild Wasteland 0.1.21]. Stop a travelling merchant with Alt+Q within 30 m or your vehicle horn within 100 m.
2. Stay within 100 m for more than five minutes. The bus should remain parked with its handbrake on, engine off and no repeated cranking.
3. Move beyond 100 m after the five-minute minimum. The bus should release the handbrake, start normally and resume its route. Leaving before five minutes should retain the remaining minimum wait.
4. Save/load while waiting, and check a setup merchant remains parked. Repeat with Engine Sim absent and with its simulated drivetrain enabled.
5. With general WW VerboseLog enabled, look for Merchant: <car> parked; ignition off, handbrake on and Merchant: <car> wait ended; resuming route. No pilot restart attempts should occur for that bus while parked.

Validation: Windows Release build with Deploy=true passed with zero errors and seven existing warnings. Built, deployed and packaged DLL MD5: 0FD9CF75515B5D7B237D6ABF479B3B15. The archive retains all 172 unchanged RC17 entries. Archive SHA256: 2eabfadf479140a5c81e17e029f4e5712292b1b75a66111807b1a6c62e21d69d. Agents do not run the game.
