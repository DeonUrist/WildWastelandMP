# Wild Wasteland MP RC7 (0.1.43)

Fog of war on/off is back in WORLD for offline players and hosts, without enabling Debug. Shared vision on/off is alongside it when Multiplayer is installed. Host setup uses the same controls. Reveal radius, edge fade and opacity stay under Debug > CleanHUD > Map fog of war; guest HUD guidance now points to World.

This corrects the earlier interpretation that all fog controls belonged in Debug. Only CorePages presentation changes (eight changed lines); existing config keys, values, defaults, save files, host authority and handshake24 stay intact. Includes RC6's integrated offline save system without the ApocalypterMP dependency. Standard WW is untouched.

Requires BepInEx5 x64; hosting/joining also requires separately installed Nexus Apocalypter Multiplayer0.4.1. All peers use matching WWMP releases. The full ZIP includes runtime assets, launchers and the required asset license; documentation/history, personal files and dependency DLLs are excluded.

Manual checks for Denis: offline without Multiplayer, open F3 > WORLD with Debug off and toggle Fog of war; repeat as host and through host setup. With Debug enabled, verify radius/edge/opacity remain under CleanHUD and on/off rows appear only in WORLD. Guests continue following host fog settings. Visual/runtime behavior remains Testing; no game launch or automated game verification.

Windows Release/Deploy=false: zero warnings/errors. Unique stage `V:/Repos/Apocalypter/_releases/WildWastelandMP_0.1.43-RC7a.dll`, built/staged MD5 **DCAA9797CB6B9AD954BC116C9A325097**. ZIP287 files/236,668,736 bytes, SHA256 **68837c629e97e4600ac8f117f735ac9efa453f864134d3f5f05ef95c55ed1ec3**; CRC/unique paths/all source bytes/18 loading images/single plugin DLL/156 unchanged baseline assets verified by the package script. Game running, so not deployed; no live configs/saves changed.
