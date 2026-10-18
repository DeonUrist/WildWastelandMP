# Stew packet routing (2026-10-18, Testing)

Stew used WW message kinds 140-143. Vehicle controls now use 140/141,
client save requests use 142, and player recovery uses 143. Multiplayer.Message
invokes its subscribers with one BinaryReader; a different handler could consume
the same payload or throw before Survival received it. Even an empty client save
request could reach the pot reader and fail its epoch read.

NetworkPots.cs now owns four named, previously unused message kinds:

| Kind | Message |
|---|---|
| 146 | Client drink intent |
| 147 | Host sip result / player benefits |
| 148 | Client ingredient request |
| 149 | Host boiling / pouring presentation |

All four senders and receive branches use these constants. Other handlers reject
these kinds before reading their payload. Host cooking, native item replication,
saved fields, validation, delivery reliability and gameplay rules are retained.
MultiplayerHandshake.Revision advances from 8 to 9 because the message mapping
changed; both peers must use the updated DLL.

Two source files, 10 added/9 removed code lines. No config, native save-format,
base-repository or external DLL changes. No game launch, harness, commit or push.

## Build and deployment

Windows Release / Deploy=false passed with zero warnings/errors while the games
were running. After Denis closed both copies, the staged DLL was copied to
`E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`.
Built/staged/installed MD5: `B5A87FE45A1CB52887FD6902FD65F0E9`.
Unique stage: `bin/Release/WildWastelandMP_stew-routing_20261018_213836.dll`.
Host and local client share the plugins folder through the client's directory
junction. Both processes were confirmed closed; disabled WW manifests are absent.

## Denis's runtime checks

1. Restart host and guest with the updated DLL. Put water and raw meat in a pot,
   including adding an ingredient as the guest. Both peers should show matching
   contents and cooking progress; the ingredient should be consumed once.
2. Heat the pot upright on a native fire. Both peers should see/hear boiling and
   see raw nutrition convert to cooked nutrition. Repeat with the host's ESC/F3
   open: cooking should continue.
3. Drink as the guest, then as the host. Volume and nutrition should agree on both
   peers; each drinker's thirst/hunger benefits should apply once. Simultaneous
   drinking should retain the existing exclusive pot access.
4. If Allow stew pot spilling is enabled, tilt/pour between two pots and onto the
   ground. Both peers should show the stream and matching final contents.
5. Use passenger radio/volume controls and send a guest Save request. Each should
   work independently of the pot. Leave the session running through a five-minute
   player checkpoint, then disconnect/rejoin.
6. Save the world on the host with partly cooked stew, reload it and rejoin.
   Liquid, raw/cooked nutrition, insanity and spoilage should match the save.

No new diagnostic messages. With Core VerboseLog enabled, a successful ingredient
request retains `[Multiplayer] Pot ingredient committed: player ... -> ...`.
Survival VerboseLog retains `[Survival] StewPot boiling started; spoilage reset:`
and `boiling stopped:`. Expect no `Rejected WW packet` / end-of-stream warning
from stew, vehicle controls, save requests or recovery. Runtime remains Testing.
