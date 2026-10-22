# Wild Wasteland 0.1.20 RC17

Fog of war now hides the traffic map overlay in unexplored areas. Routes are drawn in 50 m pieces only where their midpoints are explored; route labels, merchants and battle skulls in fog are skipped. With fog off or CleanHUD absent, the existing traffic overlay remains visible normally. Patrols queries the shared map service without referencing CleanHUD types.

Fog exploration now saves immediately when SaveLoadGame enters wait/SaveGame. Save-and-quit returns to the title before isPlay, so the previous AfterSave callback could be lost. The fog file is written through a temporary file and replaced atomically, with a confirmation log. Native ES3 save handling is unchanged. Location discovery retains its existing separate sidecar and save handling.

Install by extracting the archive into BepInEx/plugins. All RC16 assets are retained. Optional Talk, Quests, ChaseCamera and Engine Sim remain separate installs. No config entries, native save keys or sidecar formats change. This is an experimental prerelease; in-game verification remains Testing.

Test:

1. Restart and confirm Loading [Wild Wasteland 0.1.20]. Enable Map, Fog of war and Map Routes. Explore a small area, then open the map: route segments, labels, merchants and battle skulls must be hidden over unexplored terrain, while explored portions remain visible.
2. Toggle fog off and confirm the traffic overlay returns. Turn it back on and confirm unexplored portions hide again. Quest flags must retain their existing visibility.
3. Explore a new area, save-and-quit to the title, then reload the same slot. The newly explored area should remain revealed. Confirm Fog of war saved: appears before leaving gameplay, and Fog of war loaded from identifies the slot file on reload.
4. Repeat with an ordinary save and an Apocasaver autosave if installed. Check that fog survives without changing the native save flow or the existing location icons/discovery.

Validation: Windows Release build with Deploy=true passed with zero errors and seven existing warnings. Built, deployed and packaged DLL MD5: AE0792C7DA58D8F87DF481EAC9754B0D. The archive retains all 172 unchanged RC16 entries. Archive SHA256: 6caf7129e97ec1845553f0cc7dbcdcf1d17a54b28a8559a1eb03a71d5b086b3e. Agents do not run the game.
