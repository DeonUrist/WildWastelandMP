# Wild Wasteland 0.1.14 RC11

Ambiguous patrols on the compass now keep their vehicle icons and show every group in white, instead of replacing them with question marks. Ambiguous locations still use white question marks. Distances remain visible in both modes; map modes and quest flags keep their existing behavior.

Settings → Game → HUD → Patrols on compass / Locations on compass (1 km) each offer Off / Ambiguous / On. Existing choices are retained; no config keys, defaults or save formats changed. The AmbiguousPatrols description now reflects the white vehicle icons.

Install by extracting the archive into BepInEx/plugins. The archive retains RC10's assets; Engine Sim, ChaseCamera, Talk and Quests remain optional separate installs.

Verification: Windows Release build with Deploy=true succeeded with zero errors and seven existing warnings. Built/deployed DLL MD5: `71AA601997B1EE51DADFE07525902CDB`. In-game status remains Testing. With a patrol and a location in view, select Ambiguous for both: expect a white car and a white question mark, both with distances. Switch patrols to On: faction colors return. Switch either category Off: only that category disappears. Quest flags retain their icons/colors. The startup log should report Loading [Wild Wasteland 0.1.14]; no new feature-specific log lines are added.
