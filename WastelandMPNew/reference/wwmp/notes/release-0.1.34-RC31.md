# Wild Wasteland 0.1.34 RC31

Player-facing changes since RC29:

- **Patrol spawning:** complete vehicle and War Rig builds are prepared during loading and reused for later spawns, including edited templates. Removes the old staged construction delays.
- **Saved patrols:** vehicles and their AI are restored before control returns to the player. Saved missing wheels/parts, part condition, crew injuries/deaths, remaining cargo and attached or detached trailers are retained.
- **Coyote defaults:** scouts use the Coyote bike; patrols use two Coyote PipeRats and a bike. Existing customized groups remain editable.
- **Loading screen:** a long progress bar uses the UI accent and frame. The module-list text above it is removed, and Recommended mods stays hidden during loading. The title-menu recommendations also match the CleanHUD style and use plain clickable mod names.
- **Fonts and inventory:** the game font is now the default. Debug has a **CleanFont** checkbox immediately after **Clean menus**, off by default. Equipment icons show their **DROP** hover hint again with Apocasetter installed; binoculars can be dropped too.
- **NPC navigation:** includes 41 captured maps covering all 39 vanilla location types. Compatible maps are reused across sessions and placements; custom or missing variants are cached after their first bake. NPC Debug offers custom-cache reset and vanilla export tools. The optional purple uncaptured-location diagnostic now defaults off.
- **Settings and performance:** Game gains **RESET**. Presets are labeled **PERFORMANCE / BALANCED / VISUALS / VANILLA**, with Vanilla as the fresh-install default; existing saved choices are preserved. **Performance and jobs**, including scheduler statistics and their reset button, is at the bottom of **Performance** rather than Modules. Fixes a traffic-cleanup error involving short tween names.
- **Scouting and routes:** high ground extends exploration and compass range, with visible locations and traffic identified through unobstructed views. Vehicle journeys favor roads and gentler continuous terrain, including Vistas replacement roads, and wait when no safe route is available.
- **Optional mods:** fixes Vistas compatibility activation, styles its region/landmark notifications, starts its debug overlay hidden, and lets WW own sandstorms while WW Weather is enabled. Newer Vistas, Talk, Engine Sim and ChaseCamera versions are attempted rather than rejected solely by version number.

Install into `BepInEx/plugins`, preserving customized assets and configuration. Includes the full existing asset set. Optional external mods remain separate installations. This is an experimental prerelease; gameplay and performance verification remain ongoing.
