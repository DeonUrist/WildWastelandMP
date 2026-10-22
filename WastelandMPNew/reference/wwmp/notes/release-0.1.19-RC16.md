# Wild Wasteland 0.1.19 RC16

Locations reveal their real icons and colors on both map and compass when approached within 100 horizontal metres. Discovery continues while markers, ambiguity or fog are switched off, and survives display toggles and save/load. Cleared red scrapyard markers become white using Patrols' existing saved cleared state.

This release also includes the pending CleanHUD changes in the deployed build:

- Saved fog of war, with a Fog of war switch under F3 Settings > Game > HUD. Exploration imports from Immersive Map 1.12.0 when no WW fog file exists. A loaded standalone Immersive Map disables WW fog only.
- CleanHUD map framing and a glyph hint. Location markers in unexplored cells are hidden; quest markers remain visible.
- Fresh settings default map markers/routes and compass patrols/locations to Ambiguous. Existing saved settings are preserved.
- Water, food and sleep icons/numbers flash yellow at 80–90 and red above 90.
- DISCORD and GITHUB links share one background, avoiding overlapping frames.

Install by extracting the archive into BepInEx/plugins. All RC15 assets are retained. Optional Talk, Quests, ChaseCamera and Engine Sim remain separate installs. This is an experimental prerelease; in-game verification remains Testing.

Config: adds CleanHUD / Map entries FogOfWar (true), FogRevealRadius (500 m), FogEdgeFade (200 m) and FogOpacity (1). Fog settings migrate from Immersive Map where applicable. Location discovery adds no config entries.

New per-slot sidecars are config/WildWasteland/CleanHUD/Saves/<slot>.locations.txt (seed and discovered location identities) and <slot>.fog.bin (exploration). Discovery and fog are separate; both save through SaveBus.AfterSave. Native game save keys remain unchanged.

Test:

1. Restart and confirm Loading [Wild Wasteland 0.1.19]. Enable Ambiguous locations on both map and compass. Approach an unknown location within 100 m: both should reveal its real symbol/color. Move away, switch Off/On/Ambiguous and confirm it remains identified.
2. Visit another location while markers or ambiguity are off, then restore Ambiguous. It should already be identified. Save, wait for completion, reload and confirm both discoveries remain. Another save slot/new world must not inherit them.
3. Clear a red scrapyard: its map and compass symbols should become white. Save/reload and confirm they remain white; an uncleared scrapyard stays red after discovery.
4. Travel with fog enabled, open the map, toggle fog off/back on, then save/reload. Explored areas should remain revealed. Repeat across terrain/floating-origin boundaries; check map framing, symbols and quest flags. A previous Immersive Map slot should import once when the WW fog file is absent.
5. Check needs at 80–90 and above 90 for yellow/red flashing. Check the shared community-link background on title and pause menus.

With general VerboseLog enabled, marker confirmations include Location discovered:, Location cleared: and Location discoveries saved:. Location discoveries loaded: reports the loaded count and seed. Fog of war loaded from reports the exploration source.

Validation: Windows Release build with Deploy=true passed with zero errors and seven existing warnings. Built, deployed and packaged DLL MD5: 7CCD7D6C689C15A89170EEC8ECA1A846. The archive retains all 172 unchanged RC15 entries. Archive SHA256: 6d9e373a3220de0b0ff762052ba737944b204b62e82e6605296a2b9316110003. Agents do not run the game.
