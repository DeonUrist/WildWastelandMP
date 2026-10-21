# SP load selection / terrain fall (2026-10-21, Testing)

Denis reproduced: new SP game with seed 8975309, save, load, fall without ground. He approved the Core fix and deployment. No game launch, harness, live save/config changes, commit, push or release.

## Evidence and cause

- The installed 0.1.50 log records the original world as seed 8975309 and working file `MP_sp-live-5ca43c29bde541848eef000e2d84a034.es3`. After loading it records seed 12345 and a different working file, `MP_sp-live-fb6c77fa768a44a3bbbd2efd1c2fbce5.es3`.
- The published MyTest snapshot `MP_sp-save-9fdf72d1289d47c8a34a1db734dddbf7.es3` still contains global seed 8975309 and the original working identity. The file was inspected read-only; it was not modified.
- Native scene SaveLoadGame starts with a 0.2-second real-time wait before `setSeed`. The menu's existing-save loader sets SaveFile, then waits 0.5 real seconds before broadcasting LoadGame (`Sources/fsm/scene1_game/SaveLoadGame.md`, `Sources/fsm/scene0_menu/LoadGame_Canvas.md`). The early new-game state therefore occurs during an existing-save launch.
- SinglePlayerSaving.StateChanged allocates a new working identity on setSeed unless OfflineSaveRuntime.SelectedFile is set. The dependency-absent launcher already sets that marker; the dependency-present SP launcher did not. It could consequently replace the launcher's active file, ES3 default path and game SaveFile before the load broadcast. Default seed 12345 after loading is consistent with failed restoration, not evidence that the saved snapshot lost its seed.

## Change

SinglePlayerSaving now routes both title-menu and return-to-menu loads through one small Launch helper. It sets the existing selection marker before either launcher runs, clears it on failed/throwing launch, and uses the existing isPlay cleanup on success. An always-on `[System] Loading offline save: <file>` line identifies the chosen snapshot. Native terrain regeneration/load ordering stays intact; no speculative teleport, terrain reset or timeout is added. No config/save/protocol/version change.

## Human verification

1. Restart with the deployed DLL. Load the existing MyTest snapshot from the title menu. Expect the original world, player position, inventory and solid ground. Log must identify the selected snapshot and report seed 8975309 on world readiness.
2. Start a fresh SP world with seed 8975309, save on foot, return to title, and load it. Expect the same seed and ground, without falling.
3. Load that save from a running SP world using the confirmation flow; expect the same result. Repeat with a different seed to check isolation.
4. Start another genuinely new SP game after a successful load. Expect a new working identity. Existing saves must retain their original worlds. Also retest seated saves through the existing parked-car/standing placement flow.

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true -p:OutputPath=bin/Release/sp-load-20261021a` succeeded with zero warnings/errors. The game was closed, MP disabled manifest/pending installer file absent, and full MP assets/native dependency present. Exact build deployed to `E:/SteamLibrary/steamapps/common/Apocalypter/BepInEx/plugins/WildWastelandMP/WildWastelandMP.dll`; unique stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_sp-load_20261021a.dll`. Built/staged/installed MD5: `AED962C6E8CE13768AA26C0946B86202`.

Runtime acceptance remains Denis's; the source fix and correct saved seed do not by themselves prove the reported fall resolved.
