# Step 12 — permission and save UI audit

Host world fog/marker/route/AI settings remain session overlays; guests retain HUD presentation and map/compass display switches. Guest Debug/Developer/Performance pages stay hidden, setters and hotkeys enforce the same policy, Performance is default off and locked off for every MP role. The module page now visibly disables its locked Performance switch. Settings changes are blocked during a server save. Guest Save is also disabled/dimmed on the native menu when Clean menus is off, and a Core full-screen SAVING fallback covers disabled/unavailable CleanHUD. Weather guests now install the native Azure fog presentation scope too.

Windows Release Deploy=false: zero warnings/errors. No deployment/commit/game verification. No new config keys in this batch; earlier draft changes default Performance to false and adds ShowPlayers. User saved offline choices are serialized as their originals, not as guest host-policy overlays.

Final review/package remains. Runtime manual acceptance must verify both native/CleanHUD guest Save styling, loading on every peer, world controls versus HUD controls, F9-F12 suppression, reconnect/offline preference restoration and optional plugin coexistence.

DLL MD5: 958F37B4ABA3B58FBD91ABD510D2A7D3
