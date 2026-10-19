# Current Wild Wasteland settings reference

Captured 2026-10-19T11:57:32.840479+00:00 from the installed MP setup. These are persisted file values, not a runtime inspection or proposed SP defaults. Full exact copies and all parsed entries are in [sp-port-reference-2026-10-19](sp-port-reference-2026-10-19/README.md). No source settings, saves or game files were changed.

The main game root is `E:/SteamLibrary/steamapps/common/Apocalypter`. The launcher calls this role **host**; it can also run offline or join as a guest. Client and client2 use `MP/Client` and `MP/Client2`, with separate BepInEx configs. Plugin/artwork folders are junction-shared; configs are only initially copied when missing. Their persisted values can differ.

## Which files own the settings

- `BepInEx/config/WildWasteland/settings.cfg`: player-facing/module settings, including current SYSTEM saving and cleanup settings.
- `BepInEx/config/WildWasteland/tools.cfg`: advanced module tuning, debug switches and technical values. Preserve customized values even when their rows are hidden.
- `BepInEx/config/com.denis.apocalypter.wildwasteland.cfg`: active General/Keys bindings plus legacy module entries. `Plugin.Awake` binds General/Apocasetter, General/Enable Debug and Keys/In-Game Settings Button here; module settings bind the two WildWasteland files. Do not let stale legacy module entries overwrite current values.
- Captured PlayerBody `.txt` profiles and WheelGrips profiles retain current aim/ignition/weapon/vehicle fits. The generated `BuiltinPoses.generated.cs`, terrain/navigation caches, saves and other mods' configs are outside this reference. CleanHUD layout/key-backup files are included (a zero-byte key backup is preserved as-is).

MP-specific Name, shared vision, player displays and role-specific values remain reference material. A standalone port must select compatible settings, retain destination user values and SP's newer **I** ignition default, and omit MP-only keys. Config comments record defaults at the last write, which may be stale; they do not establish the current source default or runtime effect. In particular, MP forces some behavior (Performance off, unpaused multiplayer), and quest authored voices are temporarily suppressed in code even though their stored switch is on. Native/game graphics and input settings and other mods are not captured here.

## Current main-root selections

All module master switches are on except Performance; character is Male. Debug visibility is off in main.cfg, and VerboseLog, Developer mode and Recommended mods are off in settings.cfg. The complete values follow below.

Autosave is on at **20 minutes** (source default 10), with a **30-second** warning, five retained autosaves, five backups per save, screenshots on and autosave while driving off. All three optional save-cleanup toggles are off; minimum distance is 1,000 metres and abandonment time ten minutes. Manual vehicle saves remain supported independently of the driving-autosave switch.

Map FogEdgeFade is **499.3507**, FogOpacity **0.463468**, Routes **Ambiguous** and SharedVision true. Map fog controls now live under Debug > CleanHUD > Map fog of war. SharedVision is excluded from an SP port. Map controls do not control scene atmosphere; the Weather haze bypass is code behavior, with no new setting.

## Capture coverage

| Role | File | Entries | Sections |
| --- | --- | ---: | ---: |
| main | settings.cfg | 239 | 49 |
| main | tools.cfg | 730 | 76 |
| main | main.cfg | 144 | 25 |
| client | settings.cfg | 239 | 49 |
| client | tools.cfg | 738 | 78 |
| client | main.cfg | 144 | 25 |
| client2 | settings.cfg | 239 | 49 |
| client2 | tools.cfg | 730 | 76 |
| client2 | main.cfg | 144 | 25 |

## Client differences from the main root

Only differences are listed; a missing key is distinct from an empty value. Exact copies preserve comments, ordering and all values.

| Role | File | Section | Key | Main value | Role value |
| --- | --- | --- | --- | --- | --- |
| client | settings.cfg | CleanHUD / Map | FogEdgeFade | 499.3507 | 200 |
| client | settings.cfg | CleanHUD / Map | FogOpacity | 0.463468 | 1 |
| client | settings.cfg | CleanHUD / Map | Routes | Ambiguous | Off |
| client | settings.cfg | CleanHUD / Menu | Accent | Yellow | Green |
| client | settings.cfg | Multiplayer | Name | Deon | Client |
| client | settings.cfg | Performance | Enabled | false | true |
| client | settings.cfg | System / Saving | Autosave minutes | 20 | 10 |
| client | tools.cfg | NPC / Webs | TestLifetime | (missing) | 900 |
| client | tools.cfg | NPC / Webs | TestMax | (missing) | 24 |
| client | tools.cfg | Patrols / Driving | StuckPedalTakeoverSeconds | (missing) | 4 |
| client | tools.cfg | Patrols / Spawner | HeatIntervalKm | (missing) | 10 |
| client | tools.cfg | Patrols / Spawner | MaxHeat | (missing) | 3 |
| client | tools.cfg | Patrols / Traffic tuning | FootPatrolMaxRouteMetres | (missing) | 2500 |
| client | tools.cfg | PlayerBody / Camera | ThirdPersonCarDistance | 4.17 | 3.67 |
| client | tools.cfg | PlayerBody / Camera | ThirdPersonDistance | 2.23 | 2.53 |
| client | tools.cfg | PlayerBody / First person | FirstPersonJumpKneeCorrection | (missing) | 1 |
| client | tools.cfg | PlayerBody / Vehicles | HeadlightsKey | (missing) | X |
| client2 | settings.cfg | CleanHUD / Map | FogEdgeFade | 499.3507 | 200 |
| client2 | settings.cfg | CleanHUD / Map | FogOpacity | 0.463468 | 1 |
| client2 | settings.cfg | CleanHUD / Map | Routes | Ambiguous | Off |
| client2 | settings.cfg | CleanHUD / Menu | Accent | Yellow | Green |
| client2 | tools.cfg | PlayerBody / Camera | ThirdPersonDistance | 2.23 | 1.96 |

## Complete main-root persisted values

The [parsed TSV](sp-port-reference-2026-10-19/settings-values.tsv) includes all three roles, stored defaults, types and descriptions. This table is the main-root reference only. Blank values are shown as `(empty)`; `(not recorded)` means no default comment exists.

### settings.cfg

| Section | Key | Current value | Recorded default |
| --- | --- | --- | --- |
| CleanHUD | Enabled | true | true |
| CleanHUD / HUD | HUD | true | true |
| CleanHUD / HUD | ShowNeeds | true | true |
| CleanHUD / HUD | ShowHealthNumbers | true | true |
| CleanHUD / HUD | ShowInsanity | true | true |
| CleanHUD / HUD | ShowConvoys | true | true |
| CleanHUD / HUD | ShowLocations | true | true |
| CleanHUD / HUD | ShowPlayers | true | true |
| CleanHUD / HUD | ShowPlayersOnMap | true | true |
| CleanHUD / HUD | AmbiguousPatrols | true | true |
| CleanHUD / HUD | AmbiguousLocations | true | true |
| CleanHUD / HUD | Compass | true | true |
| CleanHUD / HUD | DrivingHudOnlyThirdPerson | true | true |
| CleanHUD / HUD | Scale | 1 | 1 |
| CleanHUD / Map | Enabled | true | true |
| CleanHUD / Map | Markers | Ambiguous | Ambiguous |
| CleanHUD / Map | TrafficMarkersMode | Ambiguous | Ambiguous |
| CleanHUD / Map | Routes | Ambiguous | Off |
| CleanHUD / Map | FogOfWar | true | true |
| CleanHUD / Map | SharedVision | true | false |
| CleanHUD / Map | FogRevealRadius | 500 | 500 |
| CleanHUD / Map | FogEdgeFade | 499.3507 | 200 |
| CleanHUD / Map | FogOpacity | 0.463468 | 1 |
| CleanHUD / Map | BinocularZoom | 1 | 1 |
| CleanHUD / Map | TrafficMarkers | true | (not recorded) |
| CleanHUD / Menu | Enabled | true | true |
| CleanHUD / Menu | CleanFont | false | false |
| CleanHUD / Menu | Accent | Yellow | Yellow |
| CleanHUD / Menu | EnableCheats | false | false |
| Debug | VerboseLog | false | false |
| Debug | Developer mode | false | false |
| Debug | Recommended mods | false | false |
| EngineSimCompat | Enabled | true | true |
| General | Apocasetter | true | (not recorded) |
| Guns | Enabled | true | true |
| Guns | Projectiles | true | true |
| Guns | PerGunMagazines | true | true |
| Guns | Unloading | true | true |
| Guns | BackpackOverflow | true | true |
| Guns | ArmorOverflow | true | true |
| Guns | BossHealth | true | true |
| Guns | MonsterProjectilesHitWalls | true | true |
| Guns | MeleeOnWheels | true | true |
| Guns | NpcAim | true | true |
| Guns | IronsightWheelZoom | true | true |
| Guns | ToggleIronsights | false | false |
| Guns | SmoothWeaponSwitch | true | true |
| Guns / Ammo | HoldSeconds | 0.35 | 0.35 |
| Guns / Bosses | AdjustHumanBossHP | 50 | 50 |
| Guns / Projectiles | BulletSpeed | 250 | 250 |
| Guns / Projectiles | BoltSpeed | 60 | 60 |
| Guns / Projectiles | HeadshotMultiplier | 1.5 | 1.5 |
| Guns / Projectiles | Tracers | true | true |
| Guns / Projectiles | PlayerGunTracers | true | true |
| Guns / Projectiles | VehicleDamage | true | true |
| Guns / Projectiles | WheelPopOff | true | true |
| Guns / Ranges | PistolRange | 60 | 60 |
| Guns / Ranges | SmgRange | 70 | 70 |
| Guns / Ranges | RifleRange | 120 | 120 |
| Guns / Ranges | SniperRange | 250 | 250 |
| Guns / Ranges | ShotgunRange | 35 | 35 |
| Guns / Ranges | CrossbowRange | 90 | 90 |
| HUD | Enabled | true | true |
| HUD | Crosshair | 1 | 1 |
| HUD | SelectionDot | true | true |
| HUD | HitFeedback | true | true |
| HUD | HitMarker | true | true |
| HUD | FloatingDamage | 2 | 2 |
| HUD | HeadshotSound | true | true |
| HUD | HeadshotVolume | 100 | 100 |
| Keys | Observe (third person) | LeftAlt | LeftAlt |
| Keys | Climb (alternative key) | None | None |
| Keys | Cassette start/stop | Z | Z |
| Keys | Cassette volume down | Minus | Minus |
| Keys | Cassette volume up | Equals | Equals |
| Keys | Spawn female raider | None | None |
| Keys | Shout | Q | Q |
| Keys | Shout modifier | LeftAlt | LeftAlt |
| Keys | Guns: unload fallback key | R | R |
| Keys | Patrol editor | None | None |
| Keys | Template recorder | None | None |
| Keys | Bus passenger pose editor | None | None |
| Keys | Merchant item editor | None | None |
| Keys | Quests: quest log | J | J |
| Keys | Quests: dismiss follower | Delete | Delete |
| Keys | Quests: talk | F | F |
| Keys | NPC shout | Q | (not recorded) |
| Keys | NPC shout modifier | LeftAlt | (not recorded) |
| Keys | NPC test web | None | (not recorded) |
| Keys | Tool window | F7 | (not recorded) |
| Multiplayer | Name | Deon | (not recorded) |
| Multiplayer | FloatingNames | true | true |
| NPC | Enabled | true | true |
| NPC | EnableNpcDetection | true | true |
| NPC | EnableNpcPathfinding | true | true |
| NPC | NpcAim | true | true |
| NPC | EnableIdleBehavior | true | true |
| NPC | CoyotesIdle | true | true |
| NPC | FriendsPassThrough | true | true |
| NPC | Storms | true | true |
| NPC | Burrow | true | true |
| NPC | Ambush | true | true |
| NPC | Pounce | true | true |
| NPC | SpiderNests | true | true |
| NPC | DogBite | true | true |
| NPC | DogAnimations | true | true |
| NPC | DogsLieDown | true | true |
| NPC | DogJumpAttack | true | true |
| NPC | DogCompanionCompat | true | true |
| NPC / Detection | SightCone | 100 | 100 |
| NPC / Detection | SightRange | 100 | 100 |
| NPC / Detection | DarkSightRange | 5 | 5 |
| NPC / Gungirl | Model | Models/Flexa_female.glb | Models/Flexa_female.glb |
| NPC / Gungirl | Texture | Models/flexa_female.png | Models/flexa_female.png |
| NPC / Gungirl | HideParts | beard, headband, armband | beard, headband, armband |
| NPC / Gungirl | Voice | Sounds/Gungirl | Sounds/Gungirl |
| NPC / Gungirl | VoiceMatchLoudness | true | true |
| NPC / Gungirl | VoiceVolume | 1 | 1 |
| NPC / Gungirl | VoiceIntervalMin | 2 | 2 |
| NPC / Gungirl | VoiceIntervalMax | 8 | 8 |
| NPC / Pathfinding | ScaleWithActors | false | false |
| NPC / Shoota | Model | Models/Sprokka_female.glb | Models/Sprokka_female.glb |
| NPC / Shoota | Texture | Models/sprokka_female.png | Models/sprokka_female.png |
| NPC / Shoota | HideParts | armor2, armband | armor2, armband |
| NPC / Women of Wasteland | EnableFemaleNpc | true | true |
| NPC / Women of Wasteland | FemalePopulation | 33 | 33 |
| NPC / Women of Wasteland | FemaleCrew | true | true |
| Patrols | Enabled | true | true |
| Patrols | Automatic spawns | true | true |
| Patrols | Faction battles | true | true |
| Patrols | Merchants | true | true |
| Patrols | Human turrets | true | true |
| Patrols | Ranged combat | true | true |
| Patrols | Ram damage | true | true |
| Patrols | Ram damage in car | false | false |
| Patrols | Raider music | true | true |
| Patrols | Custom paintjobs | true | true |
| Patrols | Always explode cars | false | false |
| Patrols | Cleanup | true | true |
| Patrols | Raiders overheat | false | false |
| Patrols | Fury Road | true | true |
| Patrols | Simple convoy bandits AI | true | true |
| Patrols | Foot patrols | true | true |
| Patrols | Return to car | true | true |
| Patrols | Tactical driving | true | true |
| Patrols | War Rig spawns | true | true |
| Patrols | War Rig ground recovery | false | false |
| Patrols | World traffic | true | (not recorded) |
| Patrols | Spawn warning | true | (not recorded) |
| Patrols / Balance | MinPartHealth | 80 | 80 |
| Patrols / Balance | MaxPartHealth | 100 | 100 |
| Patrols / Balance | ExplodedCarPartsPercent | 20 | 20 |
| Patrols / Combat | NPC ram damage multiplier | 4 | 4 |
| Patrols / Combat | ShootDistance | 40 | 40 |
| Patrols / Combat | WitnessMeChance | 10 | 10 |
| Patrols / Merchants | Max merchants | 2 | 2 |
| Patrols / Merchants | Merchant spawn chance | 100 | 100 |
| Patrols / Merchants | Merchant check interval (minutes) | 5 | 5 |
| Patrols / Spawning | PatrolSizePercent | 100 | 100 |
| Patrols / Spawning | Max patrols | 3 | 3 |
| Patrols / Spawning | Max convoys | 1 | 1 |
| Patrols / Spawning | Car spawn limit | true | true |
| Patrols / Spawning | Max cars in event | 5 | 5 |
| Patrols / Spawning | Max cars in double event | 6 | 6 |
| Patrols / Spawning | MinConvoyCooldown | 5 | (not recorded) |
| Patrols / Spawning | MaxConvoyCooldown | 60 | (not recorded) |
| Patrols / Spawning | PatrolSpawnChancePercent | 80 | (not recorded) |
| Patrols / War Rig | WarRigPercent | 100 | (not recorded) |
| Patrols / World traffic | PatrolSpeedKmh | 55 | 55 |
| Patrols / World traffic | ConvoySpeedKmh | 38 | 38 |
| Patrols / World traffic | Camp traffic range | 2500 | 2500 |
| Performance | Enabled | false | false |
| PlayerBody | Enabled | true | true |
| PlayerBody | Character | Male | Male |
| PlayerBody | ThirdPerson | true | true |
| PlayerBody | ToggleObserving | true | true |
| PlayerBody | DynamicCrosshair | true | true |
| PlayerBody | BodyFirstPerson | true | true |
| PlayerBody | FullBodyOnlyOnFoot | false | false |
| PlayerBody | FirstPersonArms | true | true |
| PlayerBody | FirstPersonArmsOnlyOnFoot | false | false |
| PlayerBody | FirstPersonArmSkin | true | true |
| PlayerBody | ReplaceDriver | true | true |
| PlayerBody | EquipmentOnBody | true | true |
| PlayerBody | FemalePainSounds | true | true |
| PlayerBody | SmoothWeaponSway | true | true |
| PlayerBody | SmoothAimDownSights | true | true |
| PlayerBody | PickAssist | true | true |
| PlayerBody | FirstPersonJumpLegs | true | (not recorded) |
| PlayerBody / Camera culling | CameraCulling | false | false |
| PlayerBody / Camera culling | CameraCullingInVehicles | false | false |
| PlayerBody / Climbing | Climbing | true | true |
| PlayerBody / Climbing | JumpTriggersClimbing | true | true |
| PlayerBody / Climbing | AutomaticStepUp | false | (not recorded) |
| PlayerBody / Vehicles | VehicleStatusHint | true | true |
| PlayerBody / Vehicles | VehicleHotkeyHint | true | true |
| PlayerBody / Vehicles | IgnitionKey | true | true |
| PlayerBody / Vehicles | LanceIgnoresOwnCar | true | true |
| PlayerBody / Vehicles | RustlinerDoorways | true | (not recorded) |
| POI | Enabled | true | true |
| POI / World | Spawn chance (%) | 2 | 2 |
| Quests | Enabled | true | true |
| Quests | MaxActiveQuests | 5 | 5 |
| Quests | RewardMultiplier | 1 | 1 |
| Quests | ShowTracker | true | true |
| Quests / Generated | Enabled | true | true |
| Quests / Generated | Types | fetch,kill,goto,talk,clear,takeover,deliver | fetch,kill,goto,talk,clear,takeover,deliver |
| Quests / Hire | Enabled | true | true |
| Quests / Hire | RideInCars | true | true |
| Quests / Hire | ShootFromCar | true | true |
| Quests / Items | CountCarriedItems | true | true |
| Quests / RepVendor | Enabled | true | true |
| Quests / Voice | Enabled | true | true |
| Quests / Voice | Volume | 5 | 5 |
| Quests / Voice | Folder | (empty) | (not recorded) |
| QuestsCompat | Enabled | true | (not recorded) |
| Survival | Enabled | true | true |
| System / Cleanup | Cleanup on save | false | false |
| System / Cleanup | Non-resource zombie remains | false | false |
| System / Cleanup | Exhausted disposable packs | false | false |
| System / Cleanup | Minimum distance metres | 1000 | 1000 |
| System / Cleanup | Abandoned minutes | 10 | 10 |
| System / Saving | Autosave | true | true |
| System / Saving | Autosave minutes | 20 | 10 |
| System / Saving | Warning seconds | 30 | 30 |
| System / Saving | Autosave while driving | false | false |
| System / Saving | Autosaves to keep | 5 | 5 |
| System / Saving | Backups to keep | 5 | 5 |
| System / Saving | Save screenshots | true | true |
| Weather | Enabled | true | true |
| Weather / Colors | External wall color | B89A7DFF | B89A7DFF |
| Weather / Colors | Main tint color | 805936FF | 805936FF |
| Weather / Colors | Hurricane color | A3907FFF | A3907FFF |
| Weather / Colors | Vanilla sand effect color | 572F0CFF | 572F0CFF |
| Weather / Map | Storm areas | true | true |
| Weather / Storms | Storm darkening | 0.75 | 0.75 |
| Weather / Storms | Hurricanes per km² | 0.8 | 0.8 |
| Weather / Storms | Front width (km) | 20 | 20 |
| Weather / Storms | Depth (km) | 4 | 4 |

### tools.cfg

| Section | Key | Current value | Recorded default |
| --- | --- | --- | --- |
| Core | Frame budget (ms) | 2 | 2 |
| Guns / Ammo | AnimationTimeout | 4 | 4 |
| Guns / Ballistics | PlayerGuns | true | true |
| Guns / Ballistics | FullDamageUntil | 50 | 50 |
| Guns / Ballistics | NpcHitRadius | 0.05 | 0.05 |
| Guns / Ballistics | NpcShotgunDamage | 1.7 | 1.7 |
| Guns / Ballistics | ShotgunPellets | 3 | 3 |
| Guns / Ballistics | ShotgunPelletSpread | 1 | 1 |
| Guns / Ballistics | PlayerAccuracy | 100 | 100 |
| Guns / Ballistics | NpcAccuracy | 50 | 50 |
| Guns / Ballistics | PlayerBodyRadius | 0.22 | 0.22 |
| Guns / Ballistics | PlayerHeadRadius | 0.14 | 0.14 |
| Guns / Ballistics | VehicleDamagePer1 | 20 | 20 |
| Guns / Ballistics | WheelDamageMultiplier | 5 | 5 |
| Guns / Melee | StabWeapons | knife,shiv,machete | knife,shiv,machete |
| Guns / Melee | StabSound | Sounds/knifestab.wav | Sounds/knifestab.wav |
| Guns / NPC aim | NpcAimAtBody | true | true |
| Guns / NPC aim | LeadTargets | true | true |
| Guns / NPC aim | LeadAccuracy | 0.75 | 0.75 |
| Guns / NPC aim | LeadError | 30 | 30 |
| Guns / NPC aim | MaxLeadTime | 1.5 | 1.5 |
| Guns / Tracers | TracerColor | FFC759FF | FFC759FF |
| Guns / Tracers | Glow | 1.5 | 1.5 |
| Guns / Tracers | TracerLength | 4 | 4 |
| Guns / Tracers | TracerWidth | 0.1 | 0.1 |
| Guns / Tracers | BoltColor | D9B880FF | D9B880FF |
| Guns / Tracers | BoltLength | 0.8 | 0.8 |
| Guns / Tracers | BoltWidth | 0.04 | 0.04 |
| Guns / Tracers | MaxTracers | 300 | 300 |
| Guns / Tracers | ImpactEffects | true | true |
| Guns / Tracers | MetalSparks | true | true |
| Guns / Tracers | MetalSparksScale | 0.1 | 0.1 |
| HUD / Crosshair | DotScale | 0.3 | 0.3 |
| HUD / Crosshair | SearchSeconds | 1 | 1 |
| HUD / Headshot sound | Gain | 1.3 | 1.3 |
| HUD / Headshot sound | MinInterval | 0.075 | 0.075 |
| HUD / Hit feedback | DamageFontSize | 14 | 14 |
| HUD / Hit feedback | HitMarkerSize | 22 | 22 |
| HUD / Hit feedback | NumberSeconds | 1.2 | 1.2 |
| HUD / Hit feedback | ListSeconds | 4 | 4 |
| HUD / Hit feedback | ListLines | 8 | 8 |
| HUD / Hit feedback | MarkerSeconds | 0.18 | 0.18 |
| HUD / Hit feedback | MergeSeconds | 0.05 | 0.05 |
| NPC / Ambush | Prefabs | Scorpion_Small,Scorpion_Big,Spider_Small,Spider_Big | Scorpion_Small,Scorpion_Big,Spider_Small,Spider_Big |
| NPC / Ambush | MinDistance | 3 | 3 |
| NPC / Ambush | MaxDistance | 5 | 5 |
| NPC / Behaviour | DogIdlePoseMinSeconds | 10 | 10 |
| NPC / Behaviour | DogIdlePoseMaxSeconds | 25 | 25 |
| NPC / Behaviour | DogLieFrontLift | 0 | 0 |
| NPC / Brain | ReactionTime | 100 | 100 |
| NPC / Brain | TurnRate | 210 | 210 |
| NPC / Brain | AimPose | true | true |
| NPC / Brain | CrouchChance | 50 | 50 |
| NPC / Brain | FeelerLength | 3.5 | 3.5 |
| NPC / Brain | MeleeFeelerLength | 2.5 | 2.5 |
| NPC / Brain | FeelerAngle | 60 | 60 |
| NPC / Brain | FeelerCount | 7 | 7 |
| NPC / Brain | DropCheck | true | true |
| NPC / Brain | AdvanceChance | 10 | 10 |
| NPC / Brain | AdvanceMin | 2 | 2 |
| NPC / Brain | AdvanceMax | 4 | 4 |
| NPC / Brain | StuckBackupSeconds | 0.8 | 0.8 |
| NPC / Brain | StuckMemorySeconds | 5 | 5 |
| NPC / Brain | StuckGiveUpCount | 3 | 3 |
| NPC / Brain | ApocaplayerClips | true | true |
| NPC / Brain | StrafeAngle | 50 | 50 |
| NPC / Brain | CoverBelow | 50 | 50 |
| NPC / Brain | CoverRange | 25 | 25 |
| NPC / Brain | CoverMin | 20 | 20 |
| NPC / Brain | CoverMax | 40 | 40 |
| NPC / Brain | MaxDistance | 150 | 150 |
| NPC / Burrow | Prefabs | Scorpion_Small | Scorpion_Small |
| NPC / Burrow | MinSeconds | 10 | 10 |
| NPC / Burrow | MaxSeconds | 30 | 30 |
| NPC / Burrow | AmbushMin | 3 | 3 |
| NPC / Burrow | AmbushMax | 5 | 5 |
| NPC / Burrow | SeenFrom | 5 | 5 |
| NPC / Burrow | BurrowSeconds | 0.9 | 0.9 |
| NPC / Burrow | RiseSeconds | 0.7 | 0.7 |
| NPC / Burrow | Depth | 0.75 | 0.75 |
| NPC / Burrow | SandKeywords | sand | sand |
| NPC / Burrow | UseTextureLayers | false | false |
| NPC / Burrow | TerrainIsSand | true | true |
| NPC / Burrow | FxScale | 0.3 | 0.3 |
| NPC / Burrow | FxVolume | 0.35 | 0.35 |
| NPC / Burrow | FxPitch | 1.5 | 1.5 |
| NPC / Burrow | FxRange | 45 | 45 |
| NPC / Burrow | Log | false | false |
| NPC / Debug | VerboseLog | false | false |
| NPC / Debug | NavDump | false | false |
| NPC / Debug | ShowNavigation | false | false |
| NPC / Debug | HighlightUnbakedMapPois | false | false |
| NPC / Detection | SightConeInsects | 210 | 210 |
| NPC / Detection | SightConeBeasts | 190 | 190 |
| NPC / Detection | SightConeBrutes | 210 | 210 |
| NPC / Detection | SightConeBosses | 300 | 300 |
| NPC / Detection | SightConeBurrowers | 360 | 360 |
| NPC / Detection | ConeInsects | Spider,Scorpion,Wasp,Arachnid | Spider,Scorpion,Wasp,Arachnid |
| NPC / Detection | ConeBeasts | Rat,Hound,Skinwal,Nightwalker | Rat,Hound,Skinwal,Nightwalker |
| NPC / Detection | ConeBrutes | Professor,Teacher,Juggernaut | Professor,Teacher,Juggernaut |
| NPC / Detection | ConeBurrowers | Burrower | Burrower |
| NPC / Dogs | Prefabs | Wild Hound,Yard Hound,Grimhound,Nightwalker,Alpha_Nightwalker | Wild Hound,Yard Hound,Grimhound,Nightwalker,Alpha_Nightwalker |
| NPC / Dogs | Log | false | false |
| NPC / Hunt | Prefabs | Scorpion_Big | Scorpion_Big |
| NPC / Hunt | Range | 15 | 15 |
| NPC / Hunt | MaxDistance | 60 | 60 |
| NPC / Hunt | Behind | 2.5 | 2.5 |
| NPC / Hunt | Speed | 5 | 5 |
| NPC / Hunt | Cooldown | 6 | 6 |
| NPC / Hunt | DiveSeconds | 1.6 | 1.6 |
| NPC / Hunt | RiseSeconds | 1 | 1 |
| NPC / Hunt | FallbackMin | 3 | 3 |
| NPC / Hunt | FallbackMax | 5 | 5 |
| NPC / Hunt | TravelSpeedFactor | 2 | 2 |
| NPC / Hunt | BurrowSlowdown | 2 | 2 |
| NPC / Hunt | FxScale | 0.9 | 0.9 |
| NPC / Hunt | FxVolume | 1 | 1 |
| NPC / Hunt | FxPitch | 1 | 1 |
| NPC / Idle | ReturnDelay | 0 | 0 |
| NPC / Idle | ReturnTries | 10 | 10 |
| NPC / Idle | RetrySeconds | 5 | 5 |
| NPC / Idle | TravelRunBeyond | 25 | 25 |
| NPC / Idle | WalkRadius | 200 | 200 |
| NPC / Movement | FullSpeedAngle | 30 | 30 |
| NPC / Movement | SlowestAngle | 120 | 120 |
| NPC / Movement | SlowestSpeed | 0.1 | 0.1 |
| NPC / Movement | BlockedRatio | 0.33 | 0.33 |
| NPC / Movement | BlockedSeconds | 0.5 | 0.5 |
| NPC / Movement | BlockedMemory | 1.5 | 1.5 |
| NPC / Nav | BakeRange | 200 | 200 |
| NPC / Nav | CellSize | 0.5 | 0.5 |
| NPC / Nav | Margin | 4 | 4 |
| NPC / Nav | MaxStep | 0.25 | 0.25 |
| NPC / Nav | BakeBudgetMs | 1 | 1 |
| NPC / Nav | FieldSeconds | 1 | 1 |
| NPC / Nests | Prefabs | Spider_Small,Spider_Big | Spider_Small,Spider_Big |
| NPC / Nests | Sitters | Spider_Big | Spider_Big |
| NPC / Nests | BuildRange | 520 | 520 |
| NPC / Nests | FreeRange | 750 | 750 |
| NPC / Nests | PlaceRadius | 80 | 80 |
| NPC / Nests | MaxRadius | 35 | 35 |
| NPC / Nests | MinHangingWebs | 3 | 3 |
| NPC / Nests | MaxHangingWebs | 7 | 7 |
| NPC / Nests | MinGroundWebs | 4 | 4 |
| NPC / Nests | MaxGroundWebs | 7 | 7 |
| NPC / Nests | SpotsTried | 70 | 70 |
| NPC / Nests | FrameBudgetMs | 1 | 1 |
| NPC / Nests | AlarmSpeed | 1.5 | 1.5 |
| NPC / Nests | AlarmSeconds | 20 | 20 |
| NPC / Nests | AlarmCooldown | 5 | 5 |
| NPC / Nests | SitterReach | 1 | 1 |
| NPC / Nests | Log | false | false |
| NPC / NpcAim | AimTimeScale | 50 | 50 |
| NPC / NpcAim | FacingTolerance | 8 | 8 |
| NPC / NpcAim | EngagePercent | 85 | 85 |
| NPC / NpcAim | Magazines | true | true |
| NPC / NpcAim | MagazineTable | 22_pipe_pistol=1, 22_pipe_revolver=5, 22_pipe_smg=30, akm_drum=70, akm_trash=30, akms=30, borz_smg=25, crossbow=1, folk_17=17, m16a1=30, redmark_m11=4, redmark_m11_scoped=4, rochester_m24=2, rochester_m24_chopped=2, slamberg_500=5, slamberg_500_chopped=5, slamfire_shotgun=1 | 22_pipe_pistol=1, 22_pipe_revolver=5, 22_pipe_smg=30, akm_drum=70, akm_trash=30, akms=30, borz_smg=25, crossbow=1, folk_17=17, m16a1=30, redmark_m11=4, redmark_m11_scoped=4, rochester_m24=2, rochester_m24_chopped=2, slamberg_500=5, slamberg_500_chopped=5, slamfire_shotgun=1 |
| NPC / NpcAim | SingleShotReload | 1 | 1 |
| NPC / NpcAim | ReloadVolume | 1 | 1 |
| NPC / NpcAim | ReloadSoundRange | 35 | 35 |
| NPC / NpcAim | RoundSeconds | 0.6 | 0.6 |
| NPC / NpcAim | ReloadSeconds | 2.5 | 2.5 |
| NPC / Pounce | Prefabs | Spider_Small | Spider_Small |
| NPC / Pounce | ZigzagFromMin | 8 | 8 |
| NPC / Pounce | ZigzagFromMax | 11 | 11 |
| NPC / Pounce | LeapFromMin | 5 | 5 |
| NPC / Pounce | LeapFromMax | 6 | 6 |
| NPC / Pounce | ZigzagAngle | 35 | 35 |
| NPC / Pounce | ZigzagSeconds | 0.35 | 0.35 |
| NPC / Pounce | ZigzagSpeed | 1.3 | 1.3 |
| NPC / Pounce | LeapSpeed | 10 | 10 |
| NPC / Pounce | Pitch | 70 | 70 |
| NPC / Pounce | RunOffMin | 1.5 | 1.5 |
| NPC / Pounce | RunOffMax | 2.5 | 2.5 |
| NPC / Pounce | CooldownMin | 4 | 4 |
| NPC / Pounce | CooldownMax | 7 | 7 |
| NPC / Pounce | Log | false | false |
| NPC / Senses | NightHours | 20=100, 21=75, 22=35, 23=0, 4=35, 5=75, 6=100 | 20=100, 21=75, 22=35, 23=0, 4=35, 5=75, 6=100 |
| NPC / Senses | FlankDistance | 15 | 15 |
| NPC / Senses | FlankWidth | 10 | 10 |
| NPC / Senses | NoticeSeconds | 0.2 | 0.2 |
| NPC / Senses | NoticeFar | 2 | 2 |
| NPC / Senses | LoseSeconds | 5 | 5 |
| NPC / Senses | FoughtMemorySeconds | 90 | 90 |
| NPC / Senses | Guesses | 3 | 3 |
| NPC / Senses | GuessRadius | 8 | 8 |
| NPC / Senses | Trackers | Hound,Grimhound,Pup,Nightwalker,Spider,Arachnid,Scorpion | Hound,Grimhound,Pup,Nightwalker,Spider,Arachnid,Scorpion |
| NPC / Senses | TauntMin | 12 | 12 |
| NPC / Senses | TauntMax | 25 | 25 |
| NPC / Senses | GunshotResponders | 4 | 4 |
| NPC / Senses | GunshotNearRange | 25 | 25 |
| NPC / Senses | SearchSeconds | 30 | 30 |
| NPC / Senses | GhostTimeout | 60 | 60 |
| NPC / Senses | ArriveDistance | 1.5 | 1.5 |
| NPC / Senses | LookInterval | 0.15 | 0.15 |
| NPC / Senses | ShotRangePistol | 80 | 80 |
| NPC / Senses | ShotRangeSmg | 120 | 120 |
| NPC / Senses | ShotRangeRifle | 150 | 150 |
| NPC / Senses | ShotRangeSniper | 150 | 150 |
| NPC / Senses | ShotRangeShotgun | 150 | 150 |
| NPC / Senses | ShotRangeCrossbow | 15 | 15 |
| NPC / Senses | NpcShotRanges | Flexa=150, Gungirl=150, Lugnut=150, Scrud=150, Boltjaw=120, Sprokka=80, Shoota=80, Pistoleer=80, Gunnar=150, Lugger=150 | Flexa=150, Gungirl=150, Lugnut=150, Scrud=150, Boltjaw=120, Sprokka=80, Shoota=80, Pistoleer=80, Gunnar=150, Lugger=150 |
| NPC / Senses | AllClearRange | 30 | 30 |
| NPC / Senses | TauntRange | 15 | 15 |
| NPC / Senses | ShoutBlocksButtons | Kick,ShiftDown | Kick,ShiftDown |
| NPC / Senses | PlayerShoutRange | 25 | 25 |
| NPC / Senses | ShoutCooldown | 1.5 | 1.5 |
| NPC / Senses | StormSight | 80 | 80 |
| NPC / Senses | StormHearing | 50 | 50 |
| NPC / Senses | StormRadius | 2500 | 2500 |
| NPC / Senses | ShoutVolume | 1 | 1 |
| NPC / Senses | HumanFactions | Scrapyard,Coyotes | Scrapyard,Coyotes |
| NPC / Senses | EngineMinRange | 50 | 50 |
| NPC / Senses | EngineMaxRange | 150 | 150 |
| NPC / Senses | EngineMinHp | 40 | 40 |
| NPC / Senses | EngineMaxHp | 300 | 300 |
| NPC / Senses | EngineIdleFactor | 50 | 50 |
| NPC / Senses | BailOutAware | true | true |
| NPC / Senses | BailOutAwareRange | 150 | 150 |
| NPC / Senses | ExplosionRange | 150 | 150 |
| NPC / Senses | BlastRange | 150 | 150 |
| NPC / Senses | BlastPrefabs | Explosion_Grenade,Explosion_Can,Explosion_BlastRat,Explosion_BlastZombie | Explosion_Grenade,Explosion_Can,Explosion_BlastRat,Explosion_BlastZombie |
| NPC / Senses | ThrowRange | 10 | 10 |
| Patrols / AI | Throttle | 1 | 1 |
| Patrols / AI | DriveByOffset | 5 | 5 |
| Patrols / AI | LeadTime | 1 | 1 |
| Patrols / AI | CommitSeconds | 0.5 | 0.5 |
| Patrols / AI | SteerRate | 1.5 | 1.5 |
| Patrols / AI | SteerAngle | 30 | 30 |
| Patrols / AI | MaxSteerAtSpeed | 0.35 | 0.35 |
| Patrols / AI | TurnSafeSpeed | 18 | 18 |
| Patrols / AI | RamDistance | 15 | 15 |
| Patrols / AI | PassWidth | 12 | 12 |
| Patrols / AI | RunOutMeters | 30 | 30 |
| Patrols / AI | RunOutMaxSeconds | 4 | 4 |
| Patrols / AI | ReverseSeconds | 2 | 2 |
| Patrols / AI | ReverseThrottle | 0.6 | 0.6 |
| Patrols / AI | StuckSeconds | 2 | 2 |
| Patrols / AI | RecoverWindow | 30 | 30 |
| Patrols / AI | MaxRecovers | 2 | 2 |
| Patrols / AI | WaitSeconds | 4 | 4 |
| Patrols / AI | FeelerRange | 10 | 10 |
| Patrols / AI | FeelerSpeedFactor | 0.6 | 0.6 |
| Patrols / AI | FrontOffset | 2 | 2 |
| Patrols / AI | MaxSlopeDeg | 35 | 35 |
| Patrols / AI | AvoidGain | 1.2 | 1.2 |
| Patrols / AI | IgnoreMassBelow | 40 | 40 |
| Patrols / AI | GiveUpDistance | 700 | 700 |
| Patrols / AI | InvertSteering | false | false |
| Patrols / Audio | AudioVoices | 64 | 64 |
| Patrols / Cleanup | RemoveAfterMinutes | 10 | 10 |
| Patrols / Cleanup | MaxCars | 30 | 30 |
| Patrols / Cleanup | MinDistance | 800 | 800 |
| Patrols / Combat tuning | FireArcHalfAngle | 100 | 100 |
| Patrols / Combat tuning | MaxAimPitch | 35 | 35 |
| Patrols / Combat tuning | AimTurnSpeed | 180 | 180 |
| Patrols / Combat tuning | FireBurstSeconds | 3 | 3 |
| Patrols / Combat tuning | FireIntervalMin | 8 | 8 |
| Patrols / Combat tuning | FireIntervalMax | 20 | 20 |
| Patrols / Combat tuning | RamDamageMultiplier | 1 | 1 |
| Patrols / Combat tuning | RamDamageByBody | Junker=50, Rust*=70, Scrapwagon=70, PigPen=50 | Junker=50, Rust*=70, Scrapwagon=70, PigPen=50 |
| Patrols / Combat tuning | RamFullSpeedKmh | 30 | 30 |
| Patrols / Combat tuning | RamPushStrength | 1 | 1 |
| Patrols / Combat tuning | RamInCarFactor | 1 | 1 |
| Patrols / Combat tuning | RiderRange | 35 | 35 |
| Patrols / Combat tuning | RiderMaxThrowAngle | 20 | 20 |
| Patrols / Combat tuning | RiderJumpRange | 15 | 15 |
| Patrols / Combat tuning | RiderIntervalMin | 5 | 5 |
| Patrols / Combat tuning | RiderIntervalMax | 9 | 9 |
| Patrols / Combat tuning | RiderLeapReachPercent | 80 | 80 |
| Patrols / Combat tuning | RiderBailRange | 6 | 6 |
| Patrols / Combat tuning | SuspensionLiftCenterOfMass | true | true |
| Patrols / Combat tuning | RiderLeapSpeed | 11 | 11 |
| Patrols / Combat tuning | RiderBlastDamage | 60 | 60 |
| Patrols / Combat tuning | RiderBlastRadius | 7 | 7 |
| Patrols / Combat tuning | RiderLanceSpeed | 30 | 30 |
| Patrols / Debug | TurretSpotEditor | false | false |
| Patrols / Debug | Bus passenger pose editor | false | false |
| Patrols / Debug | Merchant item editor | false | false |
| Patrols / Debug | AllowDebugSpawns | true | true |
| Patrols / Debug | AiOverlay | false | false |
| Patrols / Debug | Hide missing-FSM warnings | true | true |
| Patrols / Debug | ExitSpeedKmh | 30 | 30 |
| Patrols / Driving | StuckPedalChance | 5 | 5 |
| Patrols / Driving | BailChance | 25 | 25 |
| Patrols / Driving | StuckBailChance | 100 | 100 |
| Patrols / Loot | MinPartsFill | 15 | 15 |
| Patrols / Loot | MaxPartsFill | 60 | 60 |
| Patrols / Pose | OffsetX | 0 | 0 |
| Patrols / Pose | OffsetY | -0.3 | -0.3 |
| Patrols / Pose | OffsetZ | 0 | 0 |
| Patrols / Pose | Enabled | true | true |
| Patrols / Pose | ThighAngle | 85 | 85 |
| Patrols / Pose | KneeAngle | 30 | 30 |
| Patrols / Pose | ArmAngle | 65 | 65 |
| Patrols / Pose | LeftLegCloser | 10 | 10 |
| Patrols / Pose | RightLegCloser | 10 | 10 |
| Patrols / Pose | LeftArmCloser | 10 | 10 |
| Patrols / Pose | RightArmCloser | 10 | 10 |
| Patrols / Pose | BikeArmAngle | 55 | 55 |
| Patrols / Pose | BikeArmSpread | 22 | 22 |
| Patrols / Pose | ElbowAngle | 25 | 25 |
| Patrols / Pose.Boltjaw | LeftArmVertical | 80 | 80 |
| Patrols / Pose.Boltjaw | LeftArmHorizontal | 35 | 35 |
| Patrols / Pose.Boltjaw | LeftArmRotation | -15.6 | -15.6 |
| Patrols / Pose.Boltjaw | LeftElbowVertical | 25 | 25 |
| Patrols / Pose.Boltjaw | LeftElbowHorizontal | 0 | 0 |
| Patrols / Pose.Boltjaw | LeftElbowRotation | 0 | 0 |
| Patrols / Pose.Boltjaw | LeftHandVertical | 0 | 0 |
| Patrols / Pose.Boltjaw | LeftHandHorizontal | 0 | 0 |
| Patrols / Pose.Boltjaw | LeftHandRotation | 0 | 0 |
| Patrols / Pose.Boltjaw | RightArmVertical | -20 | -20 |
| Patrols / Pose.Boltjaw | RightArmHorizontal | 50 | 50 |
| Patrols / Pose.Boltjaw | RightArmRotation | -20 | -20 |
| Patrols / Pose.Boltjaw | RightElbowVertical | 125 | 125 |
| Patrols / Pose.Boltjaw | RightElbowHorizontal | 0 | 0 |
| Patrols / Pose.Boltjaw | RightElbowRotation | 25 | 25 |
| Patrols / Pose.Boltjaw | RightHandVertical | 0 | 0 |
| Patrols / Pose.Boltjaw | RightHandHorizontal | 25 | 25 |
| Patrols / Pose.Boltjaw | RightHandRotation | -25 | -25 |
| Patrols / Pose.Boltjaw | WeaponPositionX | 0 | 0 |
| Patrols / Pose.Boltjaw | WeaponPositionY | 0 | 0 |
| Patrols / Pose.Boltjaw | WeaponPositionZ | 0 | 0 |
| Patrols / Pose.Boltjaw | WeaponRotationX | -15 | -15 |
| Patrols / Pose.Boltjaw | WeaponRotationY | 20 | 20 |
| Patrols / Pose.Boltjaw | WeaponRotationZ | 0 | 0 |
| Patrols / Pose.Flexa | LeftArmVertical | 80 | 80 |
| Patrols / Pose.Flexa | LeftArmHorizontal | 35 | 35 |
| Patrols / Pose.Flexa | LeftArmRotation | -15.652 | -15.652 |
| Patrols / Pose.Flexa | LeftElbowVertical | 25 | 25 |
| Patrols / Pose.Flexa | LeftElbowHorizontal | 0 | 0 |
| Patrols / Pose.Flexa | LeftElbowRotation | 0 | 0 |
| Patrols / Pose.Flexa | LeftHandVertical | 0 | 0 |
| Patrols / Pose.Flexa | LeftHandHorizontal | 0 | 0 |
| Patrols / Pose.Flexa | LeftHandRotation | 0 | 0 |
| Patrols / Pose.Flexa | RightArmVertical | 5 | 5 |
| Patrols / Pose.Flexa | RightArmHorizontal | -10 | -10 |
| Patrols / Pose.Flexa | RightArmRotation | 0 | 0 |
| Patrols / Pose.Flexa | RightElbowVertical | 120 | 120 |
| Patrols / Pose.Flexa | RightElbowHorizontal | 0 | 0 |
| Patrols / Pose.Flexa | RightElbowRotation | 0 | 0 |
| Patrols / Pose.Flexa | RightHandVertical | 0 | 0 |
| Patrols / Pose.Flexa | RightHandHorizontal | 25 | 25 |
| Patrols / Pose.Flexa | RightHandRotation | -45 | -45 |
| Patrols / Pose.Flexa | WeaponPositionX | 0 | 0 |
| Patrols / Pose.Flexa | WeaponPositionY | 0 | 0 |
| Patrols / Pose.Flexa | WeaponPositionZ | 0 | 0 |
| Patrols / Pose.Flexa | WeaponRotationX | -15 | -15 |
| Patrols / Pose.Flexa | WeaponRotationY | 20 | 20 |
| Patrols / Pose.Flexa | WeaponRotationZ | 0 | 0 |
| Patrols / Pose.Lugnut | LeftArmVertical | 85 | 85 |
| Patrols / Pose.Lugnut | LeftArmHorizontal | 34 | 34 |
| Patrols / Pose.Lugnut | LeftArmRotation | 0 | 0 |
| Patrols / Pose.Lugnut | LeftElbowVertical | 0 | 0 |
| Patrols / Pose.Lugnut | LeftElbowHorizontal | 0 | 0 |
| Patrols / Pose.Lugnut | LeftElbowRotation | 0 | 0 |
| Patrols / Pose.Lugnut | LeftHandVertical | 5 | 5 |
| Patrols / Pose.Lugnut | LeftHandHorizontal | 15 | 15 |
| Patrols / Pose.Lugnut | LeftHandRotation | -10 | -10 |
| Patrols / Pose.Lugnut | RightArmVertical | 80 | 80 |
| Patrols / Pose.Lugnut | RightArmHorizontal | 60 | 60 |
| Patrols / Pose.Lugnut | RightArmRotation | 20 | 20 |
| Patrols / Pose.Lugnut | RightElbowVertical | 20 | 20 |
| Patrols / Pose.Lugnut | RightElbowHorizontal | -140 | -140 |
| Patrols / Pose.Lugnut | RightElbowRotation | 0 | 0 |
| Patrols / Pose.Lugnut | RightHandVertical | -40 | -40 |
| Patrols / Pose.Lugnut | RightHandHorizontal | 50 | 50 |
| Patrols / Pose.Lugnut | RightHandRotation | 0 | 0 |
| Patrols / Pose.Lugnut | WeaponPositionX | 0 | 0 |
| Patrols / Pose.Lugnut | WeaponPositionY | -0.1 | -0.1 |
| Patrols / Pose.Lugnut | WeaponPositionZ | 0 | 0 |
| Patrols / Pose.Lugnut | WeaponRotationX | -15 | -15 |
| Patrols / Pose.Lugnut | WeaponRotationY | 0 | 0 |
| Patrols / Pose.Lugnut | WeaponRotationZ | 0 | 0 |
| Patrols / Pose.Scraffa | LeftArmVertical | 65 | 65 |
| Patrols / Pose.Scraffa | LeftArmHorizontal | 10 | 10 |
| Patrols / Pose.Scraffa | LeftArmRotation | 0 | 0 |
| Patrols / Pose.Scraffa | LeftElbowVertical | 25 | 25 |
| Patrols / Pose.Scraffa | LeftElbowHorizontal | 0 | 0 |
| Patrols / Pose.Scraffa | LeftElbowRotation | 0 | 0 |
| Patrols / Pose.Scraffa | LeftHandVertical | 0 | 0 |
| Patrols / Pose.Scraffa | LeftHandHorizontal | 0 | 0 |
| Patrols / Pose.Scraffa | LeftHandRotation | 0 | 0 |
| Patrols / Pose.Scraffa | RightArmVertical | 65 | 65 |
| Patrols / Pose.Scraffa | RightArmHorizontal | -10 | -10 |
| Patrols / Pose.Scraffa | RightArmRotation | 0 | 0 |
| Patrols / Pose.Scraffa | RightElbowVertical | 25 | 25 |
| Patrols / Pose.Scraffa | RightElbowHorizontal | 0 | 0 |
| Patrols / Pose.Scraffa | RightElbowRotation | 0 | 0 |
| Patrols / Pose.Scraffa | RightHandVertical | 0 | 0 |
| Patrols / Pose.Scraffa | RightHandHorizontal | 0 | 0 |
| Patrols / Pose.Scraffa | RightHandRotation | 0 | 0 |
| Patrols / Pose.Scrud | LeftArmVertical | 78 | 78 |
| Patrols / Pose.Scrud | LeftArmHorizontal | 20 | 20 |
| Patrols / Pose.Scrud | LeftArmRotation | 30 | 30 |
| Patrols / Pose.Scrud | LeftElbowVertical | 25 | 25 |
| Patrols / Pose.Scrud | LeftElbowHorizontal | 0 | 0 |
| Patrols / Pose.Scrud | LeftElbowRotation | 0 | 0 |
| Patrols / Pose.Scrud | LeftHandVertical | 0 | 0 |
| Patrols / Pose.Scrud | LeftHandHorizontal | 0 | 0 |
| Patrols / Pose.Scrud | LeftHandRotation | 0 | 0 |
| Patrols / Pose.Scrud | RightArmVertical | 75 | 75 |
| Patrols / Pose.Scrud | RightArmHorizontal | -20 | -20 |
| Patrols / Pose.Scrud | RightArmRotation | -30 | -30 |
| Patrols / Pose.Scrud | RightElbowVertical | 25 | 25 |
| Patrols / Pose.Scrud | RightElbowHorizontal | 0 | 0 |
| Patrols / Pose.Scrud | RightElbowRotation | 0 | 0 |
| Patrols / Pose.Scrud | RightHandVertical | 0 | 0 |
| Patrols / Pose.Scrud | RightHandHorizontal | 0 | 0 |
| Patrols / Pose.Scrud | RightHandRotation | 0 | 0 |
| Patrols / Pose.Scrud | WeaponPositionX | 0 | 0 |
| Patrols / Pose.Scrud | WeaponPositionY | 0 | 0 |
| Patrols / Pose.Scrud | WeaponPositionZ | 0 | 0 |
| Patrols / Pose.Scrud | WeaponRotationX | -25 | -25 |
| Patrols / Pose.Scrud | WeaponRotationY | 35 | 35 |
| Patrols / Pose.Scrud | WeaponRotationZ | 0 | 0 |
| Patrols / Pose.Spanna | LeftArmVertical | 65 | 65 |
| Patrols / Pose.Spanna | LeftArmHorizontal | 10 | 10 |
| Patrols / Pose.Spanna | LeftArmRotation | 0 | 0 |
| Patrols / Pose.Spanna | LeftElbowVertical | 25 | 25 |
| Patrols / Pose.Spanna | LeftElbowHorizontal | 0 | 0 |
| Patrols / Pose.Spanna | LeftElbowRotation | 0 | 0 |
| Patrols / Pose.Spanna | LeftHandVertical | 0 | 0 |
| Patrols / Pose.Spanna | LeftHandHorizontal | 0 | 0 |
| Patrols / Pose.Spanna | LeftHandRotation | 0 | 0 |
| Patrols / Pose.Spanna | RightArmVertical | 65 | 65 |
| Patrols / Pose.Spanna | RightArmHorizontal | -10 | -10 |
| Patrols / Pose.Spanna | RightArmRotation | 0 | 0 |
| Patrols / Pose.Spanna | RightElbowVertical | 25 | 25 |
| Patrols / Pose.Spanna | RightElbowHorizontal | 0 | 0 |
| Patrols / Pose.Spanna | RightElbowRotation | 0 | 0 |
| Patrols / Pose.Spanna | RightHandVertical | 0 | 0 |
| Patrols / Pose.Spanna | RightHandHorizontal | 0 | 0 |
| Patrols / Pose.Spanna | RightHandRotation | 0 | 0 |
| Patrols / Pose.Sprokka | LeftArmVertical | 105 | 105 |
| Patrols / Pose.Sprokka | LeftArmHorizontal | 10 | 10 |
| Patrols / Pose.Sprokka | LeftArmRotation | 0 | 0 |
| Patrols / Pose.Sprokka | LeftElbowVertical | 0 | 0 |
| Patrols / Pose.Sprokka | LeftElbowHorizontal | 0 | 0 |
| Patrols / Pose.Sprokka | LeftElbowRotation | 0 | 0 |
| Patrols / Pose.Sprokka | LeftHandVertical | 0 | 0 |
| Patrols / Pose.Sprokka | LeftHandHorizontal | 0 | 0 |
| Patrols / Pose.Sprokka | LeftHandRotation | 0 | 0 |
| Patrols / Pose.Sprokka | RightArmVertical | 15 | 15 |
| Patrols / Pose.Sprokka | RightArmHorizontal | -27 | -27 |
| Patrols / Pose.Sprokka | RightArmRotation | -30 | -30 |
| Patrols / Pose.Sprokka | RightElbowVertical | 25 | 25 |
| Patrols / Pose.Sprokka | RightElbowHorizontal | 0 | 0 |
| Patrols / Pose.Sprokka | RightElbowRotation | 0 | 0 |
| Patrols / Pose.Sprokka | RightHandVertical | 0 | 0 |
| Patrols / Pose.Sprokka | RightHandHorizontal | 0 | 0 |
| Patrols / Pose.Sprokka | RightHandRotation | 0 | 0 |
| Patrols / Pose.Sprokka | WeaponPositionX | 0 | 0 |
| Patrols / Pose.Sprokka | WeaponPositionY | 0 | 0 |
| Patrols / Pose.Sprokka | WeaponPositionZ | 0.03 | 0.03 |
| Patrols / Pose.Sprokka | WeaponRotationX | 0 | 0 |
| Patrols / Pose.Sprokka | WeaponRotationY | 15 | 15 |
| Patrols / Pose.Sprokka | WeaponRotationZ | 0 | 0 |
| Patrols / Spawner | SpawnDistance | 350 | 350 |
| Patrols / Traffic tuning | DebugMap | true | true |
| Patrols / Traffic tuning | Real patrol names | true | true |
| Patrols / Traffic tuning | hide route type | false | false |
| Patrols / Traffic tuning | FollowRoads | true | true |
| Patrols / Traffic tuning | MaxRoadConnectorMetres | 350 | 350 |
| Patrols / Traffic tuning | MaxGroups | 24 | 24 |
| Patrols / Traffic tuning | MaxRealCars | 24 | 24 |
| Patrols / Traffic tuning | LoadRange | 750 | 750 |
| Patrols / Traffic tuning | UnloadRange | 1000 | 1000 |
| Patrols / Traffic tuning | DestinationGraceSeconds | 180 | 180 |
| Patrols / Traffic tuning | ConvoyGuardRange | 300 | 300 |
| Patrols / Traffic tuning | PatrolMemorySeconds | 20 | 20 |
| Patrols / Traffic tuning | FootPatrolMaxActive | 2 | 2 |
| Patrols / Traffic tuning | FootPatrolSpawnChance | 35 | 35 |
| Patrols / Traffic tuning | FootPatrolCheckSeconds | 60 | 60 |
| Patrols / Traffic tuning | FootPatrolSpeed | 1.3 | 1.3 |
| Patrols / Traffic tuning | FootPatrolLoadRange | 280 | 280 |
| Patrols / Traffic tuning | FootPatrolUnloadRange | 400 | 400 |
| Patrols / Traffic tuning | FootPatrolMinPoiMetres | 1000 | 1000 |
| Patrols / Traffic tuning | FootPatrolMinSourceDistance | 500 | 500 |
| Patrols / Traffic tuning | StrandedIdleSeconds | 30 | 30 |
| Patrols / Traffic tuning | ReturnToCarTimeoutSeconds | 120 | 120 |
| Patrols / Traffic tuning | ReturnToCarMaxDistance | 300 | 300 |
| Patrols / Traffic tuning | DismountMemorySeconds | 90 | 90 |
| Patrols / Traffic tuning | FootTargetRange | 350 | 350 |
| Patrols / Turret pose | TurretThigh | 80 | 80 |
| Patrols / Turret pose | TurretKnee | 130 | 130 |
| Patrols / Turret pose | TurretHipHeight | 0.4 | 0.4 |
| Patrols / Turret pose | RiderLanceOffset | 0, 0, 0 | 0, 0, 0 |
| Patrols / Turret pose | RiderLanceRotation | 0, 0, 0 | 0, 0, 0 |
| Performance / Game logic | Culler | On | On |
| Performance / Game logic | Hinge polls | On | On |
| Performance / Game logic | Vehicle reflection | On | On |
| Performance / Game logic | Wait-until polls | On | On |
| Performance / Game logic | Light flicker | On | On |
| Performance / Game logic | HUD FSMs | On | On |
| Performance / Game logic | Coyote broadcast | On | On |
| Performance / Game logic | Duplicate Rigidbody | On | On |
| Performance / Game logic | Data FSM restart | On | On |
| Performance / Game logic | Option toggles | On | On |
| Performance / Game logic | Player capsule | On | On |
| Performance / PlayMaker | Targeted events | On | On |
| Performance / PlayMaker | Fast FSM list removal | On | On |
| Performance / PlayMaker | Broadcast events | On | On |
| Performance / PlayMaker | GUI loop | On | On |
| Performance / PlayMaker | Trigger events | On | On |
| Performance / PlayMaker | Shared camera pick | On | On |
| Performance / PlayMaker | Number to text | On | On |
| Performance / PlayMaker | FSM sleep | On | On |
| Performance / PlayMaker | Compiled properties | On | On |
| Performance / PlayMaker | Compiled CallMethod | On | On |
| Performance / PlayMaker | Fast action loading | On | On |
| Performance / Preset | Preset | Vanilla | Vanilla |
| Performance / Rendering | Light shadows | On | On |
| Performance / Rendering | Dead post effects | On | On |
| Performance / Rendering | Anti-aliasing | On | On |
| Performance / Rendering | Sun shadows | On | On |
| Performance / Rendering | Terrain detail | On | On |
| Performance / Rendering | Particles | On | On |
| Performance / Rendering | Small shadow casters | On | On |
| Performance / Rendering | Size culling | On | On |
| Performance / Rendering | Layer cull distances | On | On |
| Performance / Rendering | Far clip | On | On |
| Performance / Rendering | Texture quality | On | On |
| Performance / Telemetry | Periodic census | false | false |
| Performance / Tuning | Light flicker / Rate (Hz) | -1 | -1 |
| Performance / Tuning | Light flicker / Distance (m) | -1 | -1 |
| Performance / Tuning | Light shadows / Shadowed point lights | -1 | -1 |
| Performance / Tuning | Light shadows / Shadow radius | -1 | -1 |
| Performance / Tuning | Light shadows / Shadow resolution | -1 | -1 |
| Performance / Tuning | Light shadows / Cull factor | -1 | -1 |
| Performance / Tuning | Light shadows / Cull minimum | -1 | -1 |
| Performance / Tuning | Light shadows / Pixel lights | -1 | -1 |
| Performance / Tuning | Light shadows / Headlight range | -1 | -1 |
| Performance / Tuning | Light shadows / Headlight shadow | -1 | -1 |
| Performance / Tuning | Anti-aliasing / Mode | -1 | -1 |
| Performance / Tuning | Anti-aliasing / SMAA quality | -1 | -1 |
| Performance / Tuning | Anti-aliasing / Stop NaN | -1 | -1 |
| Performance / Tuning | Sun shadows / Distance | -1 | -1 |
| Performance / Tuning | Sun shadows / Cascades | -1 | -1 |
| Performance / Tuning | Sun shadows / Resolution | -1 | -1 |
| Performance / Tuning | Sun shadows / Skin weights | -1 | -1 |
| Performance / Tuning | Sun shadows / Layer cull scale | -1 | -1 |
| Performance / Tuning | Terrain detail / Pixel error | -1 | -1 |
| Performance / Tuning | Terrain detail / Base map distance | -1 | -1 |
| Performance / Tuning | Terrain detail / Grass distance | -1 | -1 |
| Performance / Tuning | Terrain detail / Grass density | -1 | -1 |
| Performance / Tuning | Particles / No prewarm | -1 | -1 |
| Performance / Tuning | Particles / Off-screen culling | -1 | -1 |
| Performance / Tuning | Particles / Low collision | -1 | -1 |
| Performance / Tuning | Particles / No particle lights | -1 | -1 |
| Performance / Tuning | Particles / Max particles | -1 | -1 |
| Performance / Tuning | Small shadow casters / Half extent (m) | -1 | -1 |
| Performance / Tuning | Size culling / LOD bias | -1 | -1 |
| Performance / Tuning | Size culling / Injected scale | -1 | -1 |
| Performance / Tuning | Layer cull distances / Scale | -1 | -1 |
| Performance / Tuning | Far clip / Far plane | -1 | -1 |
| Performance / Tuning | Texture quality / Texture limit | -1 | -1 |
| Performance / Tuning | Texture quality / Anisotropic cap | -1 | -1 |
| Performance / Tuning | Car LOD / Kinematic distance (m) | -1 | -1 |
| Performance / Tuning | Car LOD / Presentation distance (m) | -1 | -1 |
| Performance / Tuning | MapMagic budget / Threads | -1 | -1 |
| Performance / Tuning | MapMagic budget / Objects per frame | -1 | -1 |
| Performance / Tuning | MapMagic budget / Apply budget (ms) | -1 | -1 |
| Performance / Tuning | Time and sleep / Max delta time | -1 | -1 |
| Performance / Tuning | Time and sleep / Sleep threshold | -1 | -1 |
| Performance / Tuning | Animator culling / Cull NPC animators | -1 | -1 |
| Performance / Tuning | Animator culling / IK distance | -1 | -1 |
| Performance / Tuning | Azure weather / Interval (s) | -1 | -1 |
| Performance / Tuning | GC time slice / Slice (ms) | -1 | -1 |
| Performance / World | Car LOD | On | On |
| Performance / World | MapMagic budget | On | On |
| Performance / World | CCD sweep | On | On |
| Performance / World | Time and sleep | On | On |
| Performance / World | Animator culling | On | On |
| Performance / World | Origin jump | On | On |
| Performance / World | Azure sun | On | On |
| Performance / World | Azure weather | On | On |
| Performance / World | GC time slice | On | On |
| PlayerBody / Animation | JumpClipStart | 0.2 | 0.2 |
| PlayerBody / Animation | StrikeWindup | 0.08 | 0.08 |
| PlayerBody / Animation | HipsDrift | 0.25 | 0.25 |
| PlayerBody / Animation | BundleWalkSpeed | 1.4 | 1.4 |
| PlayerBody / Animation | BundleRunSpeed | 3.8 | 3.8 |
| PlayerBody / Animation | BundleCrouchSpeed | 1 | 1 |
| PlayerBody / Animation | BundleSprintSpeed | 5.5 | 5.5 |
| PlayerBody / Animation | RunFrom | 3.2 | 3.2 |
| PlayerBody / Animation | SprintFrom | 4.3 | 4.3 |
| PlayerBody / Animation | UpperPhase | 0 | 0 |
| PlayerBody / Animation | TurnClipAngle | 70 | 70 |
| PlayerBody / Animation | TurnStartRate | 8 | 8 |
| PlayerBody / Animation | PumpClipSpeed | 2 | 2 |
| PlayerBody / Animation | DirBlendSpeed | 360 | 360 |
| PlayerBody / Animation | FootIK | true | true |
| PlayerBody / Animation | RunLegsIn | 10 | 10 |
| PlayerBody / Animation | WalkToStop | true | true |
| PlayerBody / Animation | RunLegsTurn | 75 | 75 |
| PlayerBody / Animation | CrouchDrop | 0.42 | 0.42 |
| PlayerBody / Animation | AimPitch | 0.9 | 0.9 |
| PlayerBody / Animation (fallback clips) | IdleClip | merchant_idle | merchant_idle |
| PlayerBody / Animation (fallback clips) | RunClip | enemy_1_run | enemy_1_run |
| PlayerBody / Animation (fallback clips) | RifleClip | enemy_machinegun | enemy_machinegun |
| PlayerBody / Animation (fallback clips) | PistolClip | sprokka_shoot | sprokka_shoot |
| PlayerBody / Animation (fallback clips) | MeleeClip | enemy_1_attack | enemy_1_attack |
| PlayerBody / Animation (fallback clips) | RunClipSpeed | 4.5 | 4.5 |
| PlayerBody / Animation (fallback clips) | WalkStride | 1.25 | 1.25 |
| PlayerBody / Animation (fallback clips) | ThighSwing | 26 | 26 |
| PlayerBody / Animation (fallback clips) | KneeBend | 38 | 38 |
| PlayerBody / Animation (fallback clips) | ArmSwing | 16 | 16 |
| PlayerBody / Animation (fallback clips) | HipBob | 0.025 | 0.025 |
| PlayerBody / Animation (fallback clips) | RightHanded | true | true |
| PlayerBody / Camera | ThirdPersonDistance | 2.23 | 3.26 |
| PlayerBody / Camera | ThirdPersonCarDistance | 4.17 | 5.38 |
| PlayerBody / Camera | ThirdHeight | 0.25 | 0.25 |
| PlayerBody / Camera | ThirdShoulder | 0.45 | 0.45 |
| PlayerBody / Camera | ThirdCarHeight | 1.1 | 1.1 |
| PlayerBody / Camera | OrbitSpeed | 3 | 3 |
| PlayerBody / Camera | EnableMMB | false | false |
| PlayerBody / Camera | AimDownSightsTime | 0.2 | 0.2 |
| PlayerBody / Camera | PickAssistRadius | 0.06 | 0.06 |
| PlayerBody / Camera culling | OcclusionOpacity | 0.2 | 0.2 |
| PlayerBody / Camera culling | OcclusionRadius | 0.55 | 0.55 |
| PlayerBody / Car seat | FirstPersonDrivingX | 0 | 0 |
| PlayerBody / Car seat | FirstPersonDrivingY | 0 | 0 |
| PlayerBody / Car seat | FirstPersonDrivingZ | 0 | 0 |
| PlayerBody / Car seat | CarCameraForward | 0.12 | 0.12 |
| PlayerBody / Car seat | SeatDrop | 0.05 | 0.05 |
| PlayerBody / Climbing | MinimumHeight | 0.45 | 0.45 |
| PlayerBody / Climbing | MaximumHeight | 2.5 | 2.5 |
| PlayerBody / Climbing | Reach | 0.85 | 0.85 |
| PlayerBody / Climbing | WaistHeightLimit | 1.25 | 1.25 |
| PlayerBody / Climbing | AnimationSpeed | 1.5 | 1.5 |
| PlayerBody / Editors | Pose key: Select weapon / arm | Keypad0 | Keypad0 |
| PlayerBody / Editors | Pose key: Move / rotate | Keypad5 | Keypad5 |
| PlayerBody / Editors | Pose key: Up / pitch up | Keypad8 | Keypad8 |
| PlayerBody / Editors | Pose key: Down / pitch down | Keypad2 | Keypad2 |
| PlayerBody / Editors | Pose key: Right / yaw right | Keypad6 | Keypad6 |
| PlayerBody / Editors | Pose key: Left / yaw left | Keypad4 | Keypad4 |
| PlayerBody / Editors | Pose key: Forward / roll right | Keypad7 | Keypad7 |
| PlayerBody / Editors | Pose key: Back / roll left | Keypad1 | Keypad1 |
| PlayerBody / Editors | WeaponAdjustment | false | false |
| PlayerBody / First person | FirstPersonBodyX | 0 | 0 |
| PlayerBody / First person | FirstPersonBodyY | 0 | 0 |
| PlayerBody / First person | FirstPersonBodyZ | 0 | 0 |
| PlayerBody / First person | FirstPersonBodyBack | 0.08 | 0.08 |
| PlayerBody / First person | BodyBackDown | 0.08 | 0.08 |
| PlayerBody / First person | FirstPersonWeaponBack | 0.08 | 0.08 |
| PlayerBody / First person | FirstPersonChestLean | 20 | 20 |
| PlayerBody / ModAPI | VerboseModApi | false | false |
| PlayerBody / Model | Model | Models/Player_female.glb | Models/Player_female.glb |
| PlayerBody / Model | Texture | Models/Player_female.png | Models/Player_female.png |
| PlayerBody / Model | ArmsTexture | Models/Player2_female_arms.png | Models/Player2_female_arms.png |
| PlayerBody / Model | AnimationBundle | Models/apocaplayer_anims.bundle | Models/apocaplayer_anims.bundle |
| Quests / Advanced | MoneyVariable | (empty) | (not recorded) |
| Quests / Debug | FactionDumpKey | None | None |
| Quests / Debug | RecordNpcNames | false | false |
| Quests / Deliver | MinDistance | 400 | 400 |
| Quests / Deliver | MaxDistance | 3000 | 3000 |
| Quests / Deliver | GameHoursBase | 24 | 24 |
| Quests / Deliver | GameHoursPerKm | 4 | 4 |
| Quests / Deliver | CampRadius | 120 | 120 |
| Quests / Factions | HomeRadius | 120 | 120 |
| Quests / Factions | HumanFactions | Scrapyard,Coyotes | Scrapyard,Coyotes |
| Quests / Factions | PeopleNames | bodyguard,pistoleer,pup,merchant,bolter,lugger,scout,scraper,chieftain,elder | bodyguard,pistoleer,pup,merchant,bolter,lugger,scout,scraper,chieftain,elder |
| Quests / Goto | MinDistance | 150 | 150 |
| Quests / Goto | MaxDistance | 1800 | 1800 |
| Quests / Hire | Hirable | Bodyguard=5,Pup=1,Bolter=5,Pistoleer=5,Gunnar=15,Lugger=10 | Bodyguard=5,Pup=1,Bolter=5,Pistoleer=5,Gunnar=15,Lugger=10 |
| Quests / Hire | DurationGameHours | 24 | 24 |
| Quests / Hire | DeathPenalty | 1 | 1 |
| Quests / Hire | FollowDistance | 6 | 6 |
| Quests / Hire | CatchUpDistance | 70 | 70 |
| Quests / Hire | IgnoreCritters | Lizard | Lizard |
| Quests / Hire | LeashDistance | 28 | 28 |
| Quests / Hire | RealShooting | true | true |
| Quests / Hire | ShotAnimation | false | false |
| Quests / Hire | RecoilDegrees | 10 | 10 |
| Quests / Hire | MagazineOverride | (empty) | (not recorded) |
| Quests / Hire | ReloadOverride | (empty) | (not recorded) |
| Quests / Hire | ReloadSoundFrom | Bolter=crossbow | Bolter=crossbow |
| Quests / Hire | ShotDamage | 7 | 7 |
| Quests / Hire | ShotInterval | 0.9 | 0.9 |
| Quests / Hire | RangedHires | Bolter | Bolter |
| Quests / Hire | ArmPoseLike | Pistoleer=Scrud,Gunnar=Flexa,Lugger=Lugnut,Bolter=Boltjaw,Lugnut | Pistoleer=Scrud,Gunnar=Flexa,Lugger=Lugnut,Bolter=Boltjaw,Lugnut |
| Quests / Hire | RideRange | 35 | 35 |
| Quests / Items | HandInRadius | 10 | 10 |
| Quests / Items | MinLiquidFraction | 0.5 | 0.5 |
| Quests / Kills | BodyRadius | 35 | 35 |
| Quests / Kills | BodyBonus | 0.1 | 0.1 |
| Quests / Kills | KillRadius | 300 | 300 |
| Quests / Kills | QuestRange | 1000 | 1000 |
| Quests / Legacy | Apocasetter | true | true |
| Quests / Quests | GiverCooldownDays | 1 | 1 |
| Quests / Quests | ItemRange | 4000 | 4000 |
| Quests / Quests | MinimumHours | 24 | 24 |
| Quests / Quests | ExtraHoursPerKm | 4 | 4 |
| Quests / Quests | MaximumHours | 96 | 96 |
| Quests / Quests | NoRepeatDistance | 400 | 400 |
| Quests / Quests | NoRepeatGameHours | 12 | 12 |
| Quests / Quests | ChainOfferGameHours | 24 | 24 |
| Quests / Quests | GiverRespawnDistance | 500 | 500 |
| Quests / Reputation | StolenPenalty | 3 | 3 |
| Quests / Reputation | FailPenaltyMultiplier | 2 | 2 |
| Quests / Reputation | HostileAtRep | 0 | 0 |
| Quests / Reputation | RecoverGameHours | 24 | 24 |
| Quests / Reputation | PayPerRep | 0.005 | 0.005 |
| Quests / Reputation | MinimumPayFraction | 0.1 | 0.1 |
| Quests / Reputation | MaximumPayMultiplier | 0 | 0 |
| Quests / RepVendor | BuyPrice | 20 | 20 |
| Quests / RepVendor | SellPrice | 10 | 10 |
| Quests / RepVendor | MaxPerDeal | 100 | 100 |
| Quests / RepVendor | Npcs | merchant | merchant |
| Quests / Takeover | CampRadius | 120 | 120 |
| Quests / Takeover | MinDistance | 200 | 200 |
| Quests / Takeover | MaxDistance | 4000 | 4000 |
| Quests / Takeover | KillsNeeded | 4 | 4 |
| Quests / Takeover | WalkerSpawnDistance | 250 | 250 |
| Quests / Takeover | GroupSize | 5 | 5 |
| Quests / Takeover | WalkSeconds | 300 | 300 |
| Quests / Takeover | GarrisonSize | 4 | 4 |
| Quests / Takeover | GarrisonRange | 500 | 500 |
| Quests / Takeover | GarrisonCooldownGameHours | 12 | 12 |
| Quests / Takeover | ScoutRange | 250 | 250 |
| Quests / Takeover | PatrolAtCamp | true | true |
| Quests / Voice | Spatial | 0 | 0 |
| Survival / Debug | VerboseLog | false | false |
| Survival / Debug | AllowStewPotSpilling | false | false |
| Survival / Model | Scale | 0.2 | 0.2 |
| Survival / Stew Pot | Nutrition conversion multiplier | 2 | 2 |
| Survival / Stew Pot | Cooking rate | 0.25 | 0.25 |
| Survival / Stew Pot | Insanity boilaway rate | 0.25 | 0.25 |
| Survival / Stew Pot | Spoiling rate | 0.002604167 | 0.002604167 |

### main.cfg

| Section | Key | Current value | Recorded default |
| --- | --- | --- | --- |
| Debug | VerboseLog | true | (not recorded) |
| General | Apocasetter | true | true |
| General | Enable Debug | false | false |
| Guns | Enabled | true | (not recorded) |
| Guns | Projectiles | true | (not recorded) |
| Guns | PerGunMagazines | true | (not recorded) |
| Guns | Unloading | true | (not recorded) |
| Guns | BackpackOverflow | true | (not recorded) |
| Guns | ArmorOverflow | true | (not recorded) |
| Guns | BossHealth | true | (not recorded) |
| Guns | MonsterProjectilesHitWalls | true | (not recorded) |
| Guns | MeleeOnWheels | true | (not recorded) |
| Guns | NpcAim | true | (not recorded) |
| Guns | IronsightWheelZoom | true | (not recorded) |
| Guns | ToggleIronsights | false | (not recorded) |
| Guns | SmoothWeaponSwitch | true | (not recorded) |
| Guns / Ammo | HoldSeconds | 0.35 | (not recorded) |
| Guns / Bosses | AdjustHumanBossHP | 5 | (not recorded) |
| Guns / Projectiles | BulletSpeed | 250 | (not recorded) |
| Guns / Projectiles | BoltSpeed | 60 | (not recorded) |
| Guns / Projectiles | HeadshotMultiplier | 1.5 | (not recorded) |
| Guns / Projectiles | Tracers | true | (not recorded) |
| Guns / Projectiles | PlayerGunTracers | true | (not recorded) |
| Guns / Projectiles | VehicleDamage | true | (not recorded) |
| Guns / Projectiles | WheelPopOff | true | (not recorded) |
| Guns / Ranges | PistolRange | 60 | (not recorded) |
| Guns / Ranges | SmgRange | 70 | (not recorded) |
| Guns / Ranges | RifleRange | 120 | (not recorded) |
| Guns / Ranges | SniperRange | 250 | (not recorded) |
| Guns / Ranges | ShotgunRange | 35 | (not recorded) |
| Guns / Ranges | CrossbowRange | 90 | (not recorded) |
| HUD | Enabled | true | (not recorded) |
| HUD | Crosshair | 3 | (not recorded) |
| HUD | SelectionDot | true | (not recorded) |
| HUD | HitFeedback | true | (not recorded) |
| HUD | HitMarker | true | (not recorded) |
| HUD | FloatingDamage | 2 | (not recorded) |
| HUD | HeadshotSound | true | (not recorded) |
| HUD | HeadshotVolume | 100 | (not recorded) |
| Keys | In-Game Settings Button | F3 | F3 |
| Keys | Tool window | F7 | (not recorded) |
| Keys | Observe (third person) | LeftAlt | (not recorded) |
| Keys | Climb (alternative key) | None | (not recorded) |
| Keys | Cassette start/stop | Z | (not recorded) |
| Keys | Cassette volume down | Minus | (not recorded) |
| Keys | Cassette volume up | Equals | (not recorded) |
| Keys | NPC shout | Q | (not recorded) |
| Keys | NPC shout modifier | LeftAlt | (not recorded) |
| Keys | NPC test web | None | (not recorded) |
| Keys | Guns: unload fallback key | R | (not recorded) |
| Keys | Patrol editor | None | (not recorded) |
| Keys | Template recorder | None | (not recorded) |
| Keys | Bus passenger pose editor | None | (not recorded) |
| Keys | Merchant item editor | None | (not recorded) |
| NPC | Enabled | true | (not recorded) |
| NPC | EnableNpcDetection | true | (not recorded) |
| NPC | EnableNpcPathfinding | true | (not recorded) |
| NPC | NpcAim | true | (not recorded) |
| NPC | EnableIdleBehavior | true | (not recorded) |
| NPC | CoyotesIdle | false | (not recorded) |
| NPC | FriendsPassThrough | true | (not recorded) |
| NPC | Storms | true | (not recorded) |
| NPC | Burrow | true | (not recorded) |
| NPC | Ambush | true | (not recorded) |
| NPC | Pounce | true | (not recorded) |
| NPC | SpiderNests | true | (not recorded) |
| NPC | DogBite | true | (not recorded) |
| NPC | DogAnimations | true | (not recorded) |
| NPC | DogsLieDown | true | (not recorded) |
| NPC | DogJumpAttack | false | (not recorded) |
| NPC | DogCompanionCompat | true | (not recorded) |
| NPC / Detection | SightCone | 100 | (not recorded) |
| NPC / Detection | SightRange | 100 | (not recorded) |
| NPC / Detection | DarkSightRange | 5 | (not recorded) |
| NPC / Pathfinding | ScaleWithActors | false | (not recorded) |
| Patrols | Enabled | true | (not recorded) |
| Patrols | Automatic spawns | true | (not recorded) |
| Patrols | World traffic | true | (not recorded) |
| Patrols | Faction battles | true | (not recorded) |
| Patrols | Merchants | true | (not recorded) |
| Patrols | Human turrets | true | (not recorded) |
| Patrols | Ranged combat | true | (not recorded) |
| Patrols | Ram damage | true | (not recorded) |
| Patrols | Ram damage in car | false | (not recorded) |
| Patrols | Raider music | true | (not recorded) |
| Patrols | Spawn warning | true | (not recorded) |
| Patrols | Custom paintjobs | true | (not recorded) |
| Patrols | Always explode cars | false | (not recorded) |
| Patrols | Cleanup | true | (not recorded) |
| Patrols | Raiders overheat | false | (not recorded) |
| Patrols | Fury Road | true | (not recorded) |
| Patrols | Simple convoy bandits AI | true | (not recorded) |
| Patrols | Tactical driving | true | (not recorded) |
| Patrols | War Rig spawns | true | (not recorded) |
| Patrols | War Rig ground recovery | false | (not recorded) |
| Patrols / Balance | MinPartHealth | 80 | (not recorded) |
| Patrols / Balance | MaxPartHealth | 100 | (not recorded) |
| Patrols / Balance | ExplodedCarPartsPercent | 20 | (not recorded) |
| Patrols / Combat | NPC ram damage multiplier | 4 | (not recorded) |
| Patrols / Combat | ShootDistance | 40 | (not recorded) |
| Patrols / Combat | WitnessMeChance | 10 | (not recorded) |
| Patrols / Merchants | Max merchants | 3 | (not recorded) |
| Patrols / Merchants | Merchant spawn chance | 100 | (not recorded) |
| Patrols / Merchants | Merchant check interval (minutes) | 5 | (not recorded) |
| Patrols / Spawning | MinConvoyCooldown | 5 | (not recorded) |
| Patrols / Spawning | MaxConvoyCooldown | 60 | (not recorded) |
| Patrols / Spawning | PatrolSpawnChancePercent | 80 | (not recorded) |
| Patrols / Spawning | PatrolSizePercent | 100 | (not recorded) |
| Patrols / Spawning | Max patrols | 3 | (not recorded) |
| Patrols / Spawning | Max convoys | 1 | (not recorded) |
| Patrols / Spawning | Car spawn limit | true | (not recorded) |
| Patrols / Spawning | Max cars in event | 5 | (not recorded) |
| Patrols / Spawning | Max cars in double event | 6 | (not recorded) |
| Patrols / War Rig | WarRigPercent | 100 | (not recorded) |
| Patrols / World traffic | PatrolSpeedKmh | 55 | (not recorded) |
| Patrols / World traffic | ConvoySpeedKmh | 38 | (not recorded) |
| Patrols / World traffic | Camp traffic range | 2500 | (not recorded) |
| PlayerBody | Enabled | true | (not recorded) |
| PlayerBody | Character | Female | (not recorded) |
| PlayerBody | ThirdPerson | true | (not recorded) |
| PlayerBody | ToggleObserving | false | (not recorded) |
| PlayerBody | DynamicCrosshair | true | (not recorded) |
| PlayerBody | BodyFirstPerson | true | (not recorded) |
| PlayerBody | FirstPersonArms | true | (not recorded) |
| PlayerBody | FirstPersonArmSkin | true | (not recorded) |
| PlayerBody | ReplaceDriver | true | (not recorded) |
| PlayerBody | EquipmentOnBody | true | (not recorded) |
| PlayerBody | FemalePainSounds | true | (not recorded) |
| PlayerBody | SmoothWeaponSway | true | (not recorded) |
| PlayerBody | FirstPersonJumpLegs | true | (not recorded) |
| PlayerBody | SmoothAimDownSights | true | (not recorded) |
| PlayerBody | PickAssist | true | (not recorded) |
| PlayerBody / Camera culling | CameraCulling | true | (not recorded) |
| PlayerBody / Camera culling | CameraCullingInVehicles | false | (not recorded) |
| PlayerBody / Climbing | Climbing | true | (not recorded) |
| PlayerBody / Climbing | JumpTriggersClimbing | true | (not recorded) |
| PlayerBody / Climbing | AutomaticStepUp | false | (not recorded) |
| PlayerBody / Vehicles | VehicleStatusHint | true | (not recorded) |
| PlayerBody / Vehicles | VehicleHotkeyHint | true | (not recorded) |
| PlayerBody / Vehicles | IgnitionKey | true | (not recorded) |
| PlayerBody / Vehicles | RustlinerDoorways | true | (not recorded) |
| PlayerBody / Vehicles | LanceIgnoresOwnCar | true | (not recorded) |
| POI | Enabled | true | (not recorded) |
| POI / World | Spawn chance (%) | 2 | (not recorded) |
