# Step 6 — host save barrier and shared pause

Server-only save entry points route to MP WorldSaver. The wrapper pauses scaled gameplay on all connected peers, skips WW gameplay jobs while keeping Core/transport/presentation alive, drains WW part operations, requests a final bounded owner item/car/player flush, then runs the original server save. Each peer uses the existing CleanHUD artwork with SAVING until the server releases the barrier. Disconnected peers are removed from the wait; slow peers do not cause a speculative partial save. Native real-time save waits are preserved. No guest world serialization is requested.

Windows Release Deploy=false: zero warnings/errors. No deployment, commit, server or gameplay test.

Remaining acceptance: actual network loss/disconnect, menus-off saving artwork, native/car interpolation settling before serialization, external autosavers/direct FSM save entry paths, abort/error cleanup, and the headless host/join/save/offline reload matrix. This is a source/compile checkpoint, not verified MP save behavior.

DLL MD5: 24376E8AFE1540B49B155533780E7EFD
