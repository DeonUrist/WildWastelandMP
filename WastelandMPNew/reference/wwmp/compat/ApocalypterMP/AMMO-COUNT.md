# Guest ammo count — 2026-10-18, Testing/source only

The guest BepInEx log records `Rendered a flat icon for MP_akm_trash`. Native MP
ApplySpawn names physical replicas `MP_` plus the prefab ID (inspection source
ApocalypterMP.decompiled.cs:9252). CleanHUD Assets.KeyOf removed clone suffixes
but retained this prefix, missing its native weapon table. Its unknown-weapon
fallback checks Reload on the physical item; the native akm_trash item has no
Reload FSM (Sources/fsm/prefabs/akm_trash.md), because Reload/AmmoUI belong to
the first-person weapon under PlayerCameraHolder. The result is melee, and
Hud.TickWeapon disables both ammo numbers for that classification.

Assets.KeyOf now removes the leading MP_ from the local HUD lookup key after
normal clone suffix processing. This also reuses the existing native weapon
icons. Physical names, prefab IDs, native ammo values, replication and saves
are unchanged. One source file, 2 added/1 removed lines.

No build or deployment: the shared checkout records Denis's explicit DLL-build
deferral for shared fog, and both game copies are running. No new MD5, game
launch, harness, commit or push. Compilation and runtime remain pending.

After the next authorized build, host/join and draw the guest's AKM: expect
7.62MM and magazine/reserve counts. Fire/reload, switch slots, holster, reconnect
and observe host guns, another guest gun, melee and thrown weapons. Counts
must follow native ammo; melee must retain its dash. No new diagnostic lines.

# Passenger ground report — investigation only

Denis reports a transient passenger going below ground while seeing the car
and other player float; it later recovered. The cause is unconfirmed. Native
SafeTeleport.RescueTick already runs every 0.5 seconds during loaded MP play,
but skips LocalPlayer.InCar. It rescues an on-foot player >4 m below non-draft
terrain after a ceiling obstruction check. It does not regenerate terrain.
A blanket 10-second terrain reset is not justified by the current evidence.
The subsequent exit guard is recorded separately in PASSENGER-GROUND.md;
the original incident's precise cause remains unconfirmed.
