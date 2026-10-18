# Remote weapon animations — 2026-10-18, Testing

Denis reported missing remote reloads, requested confirmation of tracers/grenade/blast-lance animations, then authorized building the bridge for his in-game test.

## Cause and implementation

The installed native MP dependency matches the inspected source DLL (MD5 `E9FA0B1CDCCBCD29D65C20964277C1B4`). LocalActions.Fill sends Action from BusyWith, which observes ItemAnim/eating rather than weapon reloads. MultiplayerAvatars therefore never reached its Action-contains-reload trigger. QuickItems/grenade is outside the held-weapon/kick counter filter, so it never triggered the remote Throw command either. Blast lances increment the held-weapon counter, but the generic remote throw command does not reproduce the local timed release/weapon pose.

The existing 20 Hz interpolated pose stream (packet 82) now has mode 4 for on-foot reload, grenade/lance throw and cocking poses. It captures the local body's 22 bones before FitFirstPerson adds camera-only offsets/lean, plus the held weapon name and model-relative position/rotation. Receivers suspend their generic animation graph during these poses, apply the snapshot and weapon transform, and rebuild the ordinary held prop on exit. Attack/kick/jump counters advance during pose playback to avoid replaying them when ordinary animation resumes. Existing seated/climbing streams remain in use. Tracers and host-owned thrown/explosion effects retain their existing paths.

Source: MultiplayerMotion.cs, MultiplayerAvatars.cs, one capture call in Body.cs, and the MultiplayerHandshake.cs revision constant: about 60 added/removed lines for this task. Revision **8** requires both peers to update; packet 82's mode-4 payload is new. No config entry, persistent save format, external dependency or base-repo change. No new Harmony targets/jobs/scans. No new diagnostic log lines.

## Build and deploy

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true` passed with **zero warnings/errors**. Both game copies were closed; the disabled WW manifest was absent. Built, installed and staged MD5: **`2E8D428EE5282157D38E09C4D5248CF6`**.

Stage: `bin/Release/WildWastelandMP_weapon-animations_20261018_213300.dll`. Installed: `E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`. No game launch, harness, commit or push. Runtime presentation/performance remain **Testing**.

## Denis's in-game test

1. Restart both peers using this DLL. Host/join normally, with PlayerBody and Guns/projectiles/tracers enabled. Each player observes the other; repeat with the sender in first person and third person.
2. Fire and reload an AK/pistol, then a revolver/shotgun with several missing rounds and a crossbow. Stand, walk and crouch. Expect remote tracers/bolts plus moving reload arms and a weapon that follows the hands, including shell-by-shell reloads and cocking. Cancel a reload/change weapons; expect no stuck reload or phantom attack afterward.
3. Throw a quick grenade with a gun drawn and with empty hands. Expect the left-arm throw, continued right-hand gun where applicable, one flying grenade and one explosion.
4. Throw each blast-lance variant while stationary and moving. Expect the right-arm throw, the lance leaving the hand, one flying lance/explosion, and no repeated throw when returning to ordinary movement. Check gun/hand positioning during each action and when it ends.
5. Regress driver/passenger reloads/throws, fists/melee/fire/kick, jump/climb, weapon switching, save/resume and reconnect. With a third peer, observe a guest from the other guest as well. Existing seated animations/effect paths still need visual acceptance; the build is not gameplay verification.

If something fails, state which weapon/action, observer/sender role and sender camera view; retain that run's host/client logs and verify the installed DLL hash before interpreting missing behavior.
