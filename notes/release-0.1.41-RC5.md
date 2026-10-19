# Wild Wasteland MP RC5 (0.1.41) — driver wheel grips

Drivers with empty hands and fists lowered again grip and animate the wheel/handlebars. Unarmed passengers continue resting their hands on their legs. Ready fists/punches, tool/free-hand grip, missing wheels and seated melee/lance/grenade/gun animations retain their paths.

The fists follow-up remapped both ready `hands` and the neutral `wheel` owner to `rest`; LateWheelArms therefore bypassed the driver's existing wheel solver. Body.cs now restricts that remap to actual readied fists. One removed condition per repo, matching standard WW1.0.2. The separate CarSeat.Passenger guard still selects knee-rest for passengers; existing final-bone MP pose publication carries the driver's correction to observers. No packet, handshake24, ownership, config, native save, wheel profile, new hook/job or dependency changes.

Requires BepInEx5 x64 and separately installed Apocalypter Multiplayer0.4.1 from https://www.nexusmods.com/apocalypter/mods/10. Extract the complete ZIP into BepInEx/plugins with SP Wild Wasteland disabled. All peers restart with matching builds. Official RC remains Testing.

Both Windows Release/Deploy=false builds passed zero warnings/errors. No game launch/harness/live config/save edits. Current MP folder has no loaded/installed native Multiplayer dependency, so no incomplete local deployment. Both release ZIPs exclude documentation/history while retaining runtime assets and mandatory license notice; RC5 retains the launcher scripts.

Denis tests driver empty/lowered fists -> wheel grips and steering, ready fists -> punch -> lower -> restored grips, passenger empty -> knee-rest. Repeat host driver/guest passenger, guest driver/host and second guest observing, cars/bikes, first/third person, characters, tools, missing wheel and all seated weapon attacks. Existing verbose wheel-owner/solver traces help confirm the path; visual fit, synchronization and performance remain unverified.

Release package evidence: 236664955 bytes / 287 files; DLL MD5 `011EDF677A37ED4B96D61CF09CFABC49`; ZIP SHA256 `40f44dc9461198069aef9511724b016462b3ab4631af32610abc76e5e52d3290`. Exact unique stage: `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.41-RC5a.dll`. CRC/unique entries/replacement bytes and retained runtime assets verified; no docs/personal data/dependency DLLs.
