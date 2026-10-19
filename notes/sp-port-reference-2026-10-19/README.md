# Singleplayer port reference snapshot — 2026-10-19

Captured 2026-10-19T11:57:32.840479+00:00; read-only snapshot of installed MP assets and WW settings. See [handover](../SINGLEPLAYER-PORTING-2026-10-19.md), [settings documentation](../SP-PORT-CURRENT-SETTINGS-2026-10-19.md) and [loading artwork/tips](../SP-PORT-LOADING-SCREENS-2026-10-19.md).

- `LoadingScreens/`: all 18 numbered PNGs, exact screens.json and separate BG_MAIN menu background.
- `Settings/main`, `Settings/client`, `Settings/client2`: exact persisted settings.cfg, tools.cfg and public main.cfg bytes, stored with a `.txt` suffix so ignored `*.cfg` files are not silently omitted from this handover. `main.cfg.txt` is originally `com.denis.apocalypter.wildwasteland.cfg`. Profiles retain original relative paths under each role's `profiles/` folder.
- [file-manifest.tsv](file-manifest.tsv): original absolute path, capture byte count, SHA-256, source modification time in UTC and category for every copied file. Each source was re-read at capture to detect changes. This is a sequential file snapshot, not an atomic runtime settings capture.
- [settings-values.tsv](settings-values.tsv): every parsed key/value, recorded default/type and description, including legacy/MP-only entries. Personal local character names are retained as requested current settings; this is not a ready-to-distribute config preset.
- [loading-screens.tsv](loading-screens.tsv): dimensions, sizes, hashes, usage and exact supplied tip text.

Select compatible settings for SP; preserve existing destination user values and identity. Do not install role configs, share player identities or apply the legacy main-file module values wholesale. The snapshot contains no saves, DLLs, other mods' configs, generated pose source or terrain/navigation caches. It does not port code, validate gameplay, change live files or authorize a release.
