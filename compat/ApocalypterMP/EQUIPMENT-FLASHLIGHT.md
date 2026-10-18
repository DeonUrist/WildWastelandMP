# Remote equipment and flashlight — Testing, 2026-10-18

Denis observed host equipment on the guest, but no guest backpack/attachments on the host; neither peer saw the other's flashlight illumination. The previous audit established routing for selected systems, not complete multiplayer presentation coverage. Its completeness claim was too broad.

## Proven causes

- Native `WorldObjects.OnSpawn` prefixes physical guest replicas with `MP_`. Equipment stripped clone suffixes but transmitted this prefix. The host prefab lookup cannot resolve these names. `MP/Client/BepInEx/LogOutput.log` lines 152–153 explicitly report `Objects: prefab 'MP_backpack_medium' not found` and `Objects: prefab 'MP_flashlight1' not found`. The launcher roles in this reproduction were reversed: MP/Client was hosting.
- Packet 83 contained item names and a drawn mask only. Native player state has no flashlight switch/light payload, and `Equipment.MountFlashlight` explicitly returned before creating remote glow. Neither direction could present a remote beam.

## Change

`MultiplayerEquipment` removes the native replication prefix from outbound and received equipment metadata, leaving physical objects unchanged. Existing sender validation and host relay remain. Packet 83 now includes the native flashlight switch, range, intensity, cone, RGB, player-relative aim and shadow setting; validated on receive. Existing equipment cadence sends changes up to five times per second and repeats stable state every three seconds. Handshake revision **12** requires matching peers.

New `EquipmentFlashlight` captures `PlayerCamera/Flashlight/Spot Light`, including when camera view hides its hierarchy while the flashlight remains switched on. Remote Equipment creates a spotlight plus existing lens glow at the mounted flashlight, takes the cookie from that item's native imageUI, and applies transmitted settings. Aim follows the remote yaw root with interpolation after the ordinary/special remote pose paths. Replacing/removing the mount destroys its lights. Native battery use and local flashlight simulation remain native.

Five source files, approximately 105 added/changed lines: Equipment.cs, new EquipmentFlashlight.cs, MultiplayerEquipment.cs, MultiplayerMotion.cs and MultiplayerHandshake.cs. No new config, save fields, external DLL or base-repository change.

Native evidence: `Sources/fsm/scene1_game/PlayerCameraHolder.md` Flashlight FSM at line 472 and SlotEmptyFull at 2063; `Sources/data/hierarchy/scene1_game/PlayerCameraHolder.txt` flashlight hierarchy at 2513. Item imageUI provides each flashlight's range, tint and cookie.

## Source direction audit

| System | Authority / directions checked |
| --- | --- |
| Equipment 83 / weapon motion 82 | Local owner → host validation → other guests; host → guests; cached equipment applied when avatar rebuilds. Both equipment directions normalize prefab names. Held weapon models use native weapon-child names. |
| Merchant fixtures 154/155 | Host → guests; guest physical sale/stock interactions use native object ownership, without guest stock creation. |
| Spider webs 150–153 / nest alarms | Host catalogue → guest, guest missing-mesh request → host, host chunks → guest; host alarm checks targetable remote players. |
| Dogs / weather / War Rig shelter | Host actor pose/weather selection → guests; remote player positions included in host decisions; shelter protection resolves remote players and guest presentation installs hooks. |
| Stew 146–149 | Guest drink/ingredient intent → host; sip result/presentation → guest; native physical variables and host save authority retained. |
| Vehicles 139–141 | Owner state → host relay; passenger request → host → owner. |

This is a source audit of these paths. In-game correctness, late join, visual fit and performance remain unverified.

## Build and deployment

Windows Release / Deploy=true: zero warnings/errors. Both games closed, disabled WW manifest absent. Built, installed and unique stage MD5 **4EBC4C4B73CE43CBA754F52736159D02**. Stage: `bin/Release/WildWastelandMP_equipment-light_20261018_221726.dll`. Local client plugins junction shares the install. Includes the shared checkout's concurrent starter-vehicle change. The intermediate name-only build `51E6AE6E2E2D5A07AA3551864B3DDCEC` is superseded. No game launch, harness, commit or push.

## Denis's in-game checks

1. Restart both copies with revision 12. On each peer equip/remove a backpack, stowed long/side weapons, blades, binoculars and flashlight. The other peer should see the same attachments; drawing/stowing should hide/restore only the matching mount. Repeat after reconnect and swap host role.
2. At night face a wall, toggle each flashlight type on/off and look up/down/turn. The observing peer should see lens glow and illumination moving with the owner's aim, in both directions. Repeat standing/crouched, first/third person, seated and after removing/replacing the flashlight.
3. Rejoin with the flashlight already on; after normal equipment resend, the observer should see the correct mount and beam. Check old/military/police range/tint/cookie appearance.
4. Regress both roles for held weapon/reload/throw presentation and the scenarios in GUEST-WORLD-SYSTEMS.md and STEW-PACKET-ROUTING.md. Host-authoritative hazards still run on the host, including effects initiated by guests.

Existing global VerboseLog + PlayerBody verbose mount lines report `Equipment mount ...`. The two `MP_... prefab not found` warnings above must no longer occur for equipment. No new per-packet log spam was added.
