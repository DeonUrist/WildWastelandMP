# Guest world systems (2026-10-18, Testing)

Denis requested fixes for all five gaps from the source audit. This change retains
the other chat's holding-pose/equipment implementation (handshake 10) and advances
the handshake to 11. Both peers need this build.

## Changes and ownership

- Mobile merchants: NetworkMerchants snapshots the host's existing merchant
  records, demand boards, faction trading gate and actual car-relative fixture
  poses over unused WW 154/155. Guests construct the same station/board/boxes/sale
  anchors through MerchantHost's presentation branch. That branch creates no
  stock, guard, traffic brain or layout file. Native item replication supplies the
  goods. MerchantTrigger retains native sale/confirmation/payment actions but
  admits only the item simulation owner, avoiding payments on an observer's
  wallet; guest ownership requests retry through native trigger-stay handling.
  Removing a guest fixture detaches tracked goods before destroying the fixture.
- Spider nests: build/retention distances now consider all players; host web-touch
  checks include targetable guests and their own car state. The original host
  alarm/cooldown/boost/target logic is retained. Web catalogues use WW 150/151;
  guests request missing meshes through 152 and receive bounded 900-byte reliable
  chunks through 153. Host mesh vertices/colors are compressed once at creation,
  before Unity drops the mesh's CPU copy. Guests reconstruct the exact strand
  geometry and use the original local day/night/flashlight lighting. No guest
  spider brain or web collision/damage logic runs. Each peer receives at most four
  mesh chunks per transfer tick; late joins use the same catalogue/request path.
- War Rig shelter: host protection resolves remote player presence before testing
  the existing tanker bounds. Guests install the existing native shelter hooks
  and local dust tick through Patrols' presentation lifecycle. Host damage/force
  filtering covers sheltered guests; leaving the tanker restores exposure.
- Dog poses: the existing actor manifest/pose stream accepts explicitly supplied
  non-Mixamo bones for dogs, including the visual Animator root. DogAnimations
  registers the sampled bones while it owns the pose, unregisters when yielding,
  and re-registers after a session epoch change. Eligibility range considers the
  nearest player. Guests use the existing native Animator suspension/timeout
  restoration. No second dog simulation, damage path or custom attack packet.
- Weather: the host can track up to the native 64-anchor capacity around all
  players. The complete region set is replicated, while each camera selects its
  nearest four for rendering and the existing four-region atmosphere shader.
  Hurricane simulation remains host-owned and processes the selected world
  anchors, including those near guests. Ground caches and diagnostic readers
  accept the expanded set; old diagnostic ground arrays remain readable. Native
  encounter/save anchors remain intact. Reconciliation uses WorldRunning so a
  host menu does not stop discovering guest storms.

18 source files, approximately 400 added/removed lines. Existing namespaces,
prefab IDs, config entries and native save keys/formats remain. Webs retain their
existing host layout-cache/rebuild policy; merchant stock and world weather use
their existing host save paths. No base repository or external DLL changes.

## Build and deployment

Windows `dotnet build WildWastelandMP.csproj -c Release -p:Deploy=true` succeeded
with zero warnings and zero errors. Both game processes were closed and the
disabled WW manifest was absent. Installed DLL:
`E:\SteamLibrary\steamapps\common\Apocalypter\BepInEx\plugins\WildWastelandMP\WildWastelandMP.dll`.
The local client's plugins junction shares this installation.

Built/installed MD5: `9DAC05B17C33DFE4AF865F6B514B011F`.
Unique stage: `bin/Release/WildWastelandMP_guest-systems_20261018_220132.dll`.
No game launch, harness, commit, push or release. Runtime/performance remains
Testing; compilation does not establish visual or transaction acceptance.

## Denis's in-game checks

1. Restart both copies with this DLL. Confirm the bridge initializes and these
   lines appear on both roles: `[Multiplayer] Merchant fixture mirror ready
   (host layouts/stock; owner-only native sales)` and `[Multiplayer] Spider web
   mirror ready (host meshes; guest lighting; host alarms)`.
2. Visit each mobile merchant type. Both peers should see matching boards,
   boxes/layout and stock on the moving and parked bus. Buy and sell from each
   peer, including liquids: only the acting wallet changes, each item is consumed
   once, prices agree, faction hostility gates trading. Late join and host
   save/reload should retain the same shop without duplicate goods.
3. Visit a spider nest together, then with only the guest nearby. Allow mesh
   chunks to arrive. Web strands should match on both peers; guest flashlight
   lighting stays local. Touch hanging and ground webs as each peer: the host
   should log the existing `Nests: you touched a ... web at ...` alarm, and spiders
   should drop/chase/boost toward the touching player. A guest in a car should not
   trigger the on-foot web-touch alarm. Check late join and reload/rebuild.
4. Watch native hounds rest, rise, run and bite from both peers, including near a
   guest away from the host. Confirm matching custom poses and one host damage
   result. Check animation recovery after NPC toggle/disconnect/rejoin. If Dog
   Companion is installed, check its supported matching rig/seat poses as well.
5. Put a guest inside a War Rig during a hurricane; compare the sheltered host.
   Neither should receive storm lift/damage inside. Step outside: native hazards
   should resume. Rejoin while the tanker is loaded and repeat.
6. Separate players near different loaded natural storms, including more than
   four active world anchors. Each player should see the nearest storms and get
   the matching host-owned hazards. Open host ESC/F3 while the guest approaches
   another storm. Save/reload and reconnect; natural anchors should remain native
   saved encounters. Manual previews remain unsaved, with four preview slots.
7. Regress the other chat's held weapon poses/equipment, normal NPC poses, car
   ownership transfers and stew. Watch for Rejected WW packet, missing prefab,
   mesh/merchant snapshot errors, duplicated items/payments or stale fixtures.
