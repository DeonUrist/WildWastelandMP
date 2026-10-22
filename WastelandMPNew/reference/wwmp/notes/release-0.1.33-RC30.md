# Wild Wasteland 0.1.33 RC30

Experimental prerelease containing all pending changes after RC29. Gameplay, visual acceptance and performance remain Testing.

- NPC structure navigation grids now persist as compatible prefab templates. Camps, buildings, caves, wrecks, Starter_Area and supported custom static POIs reuse maps across sessions and placements, including yaw/elevation changes. Map I/O runs within bake slices; invalid data falls back to baking. Patrols uses the grid's transformed floor points for location anchors.
- Read-only vanilla-pack loading, custom-cache reset and vanilla-map export tools are included. No accepted vanilla map binaries are bundled yet: missing maps still bake locally. Custom maps live under `config/WildWasteland/NPC/NavMaps/Custom`; export candidates go to `VanillaExport`. See [navigation map documentation](../src/Modules/NPC/NavMaps/README.md).
- **Collection-build diagnostic:** new tools.cfg `[NPC / Debug] HighlightUnbakedMapPois` defaults true. Visible map icons for uncaptured POI types are light purple, overriding allegiance/ambiguity colors while respecting fog and marker visibility. Normal colors return after a successful bake save. Disable **Highlight unbaked POIs on map (light purple)** on NPC Debug (or its F3 Debug switch) to restore normal colors immediately. Compass colors are unchanged.
- Game-page RESET restores bound Game, Performance, Debug and Developer config entries to their defaults, including Enable Debug and the optional ChaseCamera HUD switch. Other module master switches, custom asset/pose files and saves remain intact; Performance and POI masters are included in reset. Config callbacks still apply changes live.
- Performance presets display PERFORMANCE / BALANCED / VISUALS / VANILLA. Vanilla is the fresh-install default and disables preset-dependent visual/simulation changes and sliced item cleanup. Existing saved Low/Balanced/Max/Vanilla choices remain compatible and are preserved until changed or reset.
- The typed `iTween.Stop(GameObject,string)` call now guards shorter tween names before its native prefix comparison. Matching tweens retain the original stop/disposal behavior, including traffic cleanup. Startup confirms `iTween stop guard ready (always on)`.
- The title-menu Recommended mods panel uses CleanHUD's shared panel, atlas font and accent, with clickable plain mod names and no `> NEXUS` suffix. It lists optional mods not currently active and hides during menu modals, WW settings/editors and Apocasetter input blocking.
- Includes the complete RC29 asset set. Optional external mods are not bundled. Preserve customized assets/configs when installing into `BepInEx/plugins`. No saved component, prefab ID or native game-save format changes; the navigation cache is new shared prefab data rather than per-slot save data.

## Check after installing

1. Confirm `Loading [Wild Wasteland 0.1.33]`, normal module startup and `iTween stop guard ready (always on)` in LogOutput.log.
2. On the title screen, check Recommended mods matches CleanHUD, follows accent changes and shows plain clickable names. Open a mod link, then open settings/MODS and verify the notice yields correctly.
3. On F3 Performance, check the four labels. Existing choices must survive an update; fresh settings should select VANILLA. Switch away and back to Vanilla and check original visual/simulation settings are restored.
4. With settings you are willing to reset, click F3 Game > RESET. Check Game/Performance/Debug/Developer values restore, Enable Debug turns off, and other module masters and asset/pose/save files remain intact. Optional ChaseCamera HUD enable resets too.
5. Enable NPC verbose logging and approach an uncaptured POI. Expect `Nav: saved map <prefab>` and a `.wwnav` in Custom, followed by the icon's normal color. Restart and revisit: expect `Nav: loaded map <prefab>` with custom-cache counts increasing. Test NPC movement through entrances and around walls, another placement at a different yaw/elevation, cave floors and Patrols arrival/departure anchors.
6. Check that purple icons respect map fog/Off, and turning HighlightUnbakedMapPois off immediately restores normal colors. The log reports `Nav: unbaked POI map tint off; normal colors restored`.
7. NPC Debug > Reset custom pathfinding baked data should clear generated custom maps and rediscover them while retaining shipped maps and exports. Export vanilla pathfinding maps reports missing native POIs and writes capture candidates; acceptance requires clean vanilla geometry and in-game testing before embedding.
8. Exercise normal traffic cleanup and compare frame times on the same camp/driving route. There is no runtime-performance acceptance claim for this build.

Windows Release build/deploy passed with zero warnings and zero errors. Built, installed and uniquely staged DLL MD5: `4BACBA056CCB356E044B277D35790C6D`. Gameplay and performance remain pending.
