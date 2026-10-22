# Issue #1 — passenger pose and shared vehicle controls

2026-10-18. Denis explicitly approved exceeding the normal 50-line limit for this issue in PlayerBody and the MP bridge. No other issue is addressed by these changes. Concurrent menu work in the checkout is preserved.

## Cause and implementation

- CarSeat copied the driver's static arm bones for passengers; skipping wheel IK left that pose intact. Passenger arms now use the existing rigid two-bone solver to rest above their respective knees. Weapons, throws, binoculars and native arms retain ownership; a tool yields the right arm. The existing mode-3 stream captures the resulting pose after final IK, so observers receive it too.
- Native protocol-21 CarSnapshot includes Handbrake, but ApplyCarVisuals copies only steering/throttle/brakes. Its Handbrake FSM remains independent, writes input.Handbrake itself and supplies WW's HUD status. MultiplayerVehicles now reconciles the FSM and input from the received snapshot, including TakeOverCarState. Replica control changes request the actual car owner through the existing ordered WW channel.
- Native snapshots do not include cassette OnOff or volume. Owner capture now publishes a small epoch/sequence-checked companion packet (139); host verifies and relays owner state. Replica playback/volume changes use ordered requests (140/141), validated against the nearby player, current owner and fitted radio identity. A periodic native car capture refreshes late joiners and retries a radio not yet attached/registered. Caches clear on session/save generation changes and sweep destroyed cars. Native ignition synchronization (ApplyCarExtras/KeepDashboard) is retained.
- Matching peers now require handshake revision 5. No external DLL, config entry, save format, prefab ID or namespace changes. Cassette playback position/seek timing is outside this issue's status synchronization.

Source evidence: existing external `V:\Repos\Apocalypter\_staging\ww-mp-20261018\inspection\ApocalypterMP.decompiled.cs`, CurrentCarState/ApplyCarVisuals/ApplyCarExtras/KeepDashboard/TakeOverCarState; native `Sources/fsm/prefabs/Rustallion.md` Handbrake and `Sources/fsm/prefabs/radio_car.md` OnOff/Volume. The current host/client logs confirm the fitted passenger seat and live shared Poloska; they do not certify the new changes.

## Denis's in-game verification (Testing)

1. Restart both host and client with this matching DLL. Seat both players in a car with driver/passenger seats. Empty-handed passengers should rest their hands over their knees in both views and on the other peer; the driver should retain wheel hands.
2. Apply/release the handbrake from the driver and then the passenger using its physical control. Both peers should converge on the same lever, HUD indicator and braking state. Repeat while stationary and moving.
3. Fit a cassette/radio. Start/stop it and change volume from each seat, using both hotkeys and the physical controls. Playback status and volume should agree on both peers. Remove/refit the radio and reconnect a client; repeat after attachment finishes.
4. Toggle ignition/start/stop from each peer and verify both status displays. Swap driver/passenger roles; the new driver should retain the previous handbrake/cassette state and passengers should return to the knee-rest pose. Check a weapon/tool temporarily takes its normal arm ownership.
5. Startup confirmation: `Passenger vehicle controls ready (handbrake and cassette; native ignition retained)`. With global WW VerboseLog, `Vehicle control applied: <car>, control <n>, value <v>` identifies accepted remote changes (1 handbrake, 2 cassette playback, 4 volume). PlayerBody logging identifies `passenger knee-rest pose` on entry.

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true` passed with zero warnings/errors. Built, installed and uniquely staged DLL MD5: `3EBE1810C8AA5D96CB507312BE18D19B`. Approximately 260 source lines across MultiplayerVehicles.cs, Multiplayer.cs (installation only), MultiplayerHandshake.cs, Body.cs, WheelArms.cs and CarSeat.cs, plus these checkpoint notes. Concurrent menu edits were preserved and are included in the shared-checkout build; they are not part of this issue's fix. Runtime acceptance belongs to Denis. No game launch, commit/push/release.
