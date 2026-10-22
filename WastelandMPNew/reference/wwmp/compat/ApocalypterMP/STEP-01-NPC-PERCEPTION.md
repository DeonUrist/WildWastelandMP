# Step 1 — independent NPC perception of multiplayer players

Status: source checkpoint compiled; paused after this step at Denis's request. No gameplay verification, deployment, commit or push. The overall MP patch remains incomplete.

## Cause and change

The prior draft still shared a notice timer across candidates and simple foot-patrol AI considered only a current or globally nearest player. That could transfer player 1's detection progress to player 2, or let a hidden nearest player prevent acquisition of a farther visible one.

Each NPC now keeps notice progress, acquisition, last visibility and fought-memory deadline separately for each actual player. Every visible player advances their own timer; selection chooses the closest independently acquired/remembered player passing visibility. A closer player still being noticed cannot hide a farther eligible target. Switching players does not transfer notice time or memory deadlines.

Previously acquired players retain outside-cone tracking individually. Range, storm/night visibility and head/body obstruction checks still apply; losing sight respects the existing five-second minimum. Contact state is pruned for unavailable/disconnected players and cleared on world reset. Both host and guest downed players are excluded. NPC scheduling distance considers remote players rather than only the host.

Normal on-foot NPCs, simple foot-patrol combat, spider acquisition and insanity-target selection use these identity-aware rules. Firing LOS for simple foot-patrol AI recognizes the specific target's vehicle rather than treating a remote player's own car as unrelated cover. Sight exemption for that target's car was present in the preceding draft; acquisition/tracking now uses it per player.

Changed in this step: Core/Multiplayer.cs (cached native local-down state); NPC/Detection/Senses.Players.cs (contact logic and pruning), Senses.cs (selection, memory and lifecycle), Senses.Spiders.cs (player selection), NPC/Brain/SimpleBandits.cs (eligible closest selection and firing LOS). No new config, saved key, class/prefab rename or network packet format change in this step.

## Validation and artifact

Windows PowerShell: `dotnet build WildWasteland.csproj -c Release -p:Deploy=false -v:minimal` — succeeded, **0 warnings / 0 errors**, 2026-10-18. No harnesses or game/server launches.

- Built DLL: `C:\Users\chura\.codex\worktrees\ww-mp-rc33\WildWasteland\bin\Release\WildWasteland.dll`.
- MD5: `7E9F4536341747E0E4BFDE333D7F0464`.
- SHA256: `EFD1EBE3CC805EC76AC33B771A292D9CF01C371524920C4870F4013A754958CC`.
- Non-live checkpoint copy: `V:\Repos\Apocalypter\_staging\ww-mp-20261018\WildWasteland_0.1.36_mp_step1_7E9F4536.dll`. This includes the unfinished earlier compatibility source and is NOT a final install/test handover.

Compile success does not verify runtime reflection bindings, networking or game behavior. Seated NPC targeting, patrol-driver pursuit, per-target dismount handover and the other compatibility gaps in RESUME.md remain to be completed before the planned headless test.

## Manual scenarios after the complete patch is ready

1. Player A stands visible to a hostile foot patrol; nearer player B stays behind a solid wall or outside its initial cone. The patrol acquires A after A's notice delay and never targets B from A's detection progress.
2. B enters clear sight. B must complete its own notice delay; once both are detected the closest is selected. Swap distances and check switching in both directions.
3. An acquired player circles behind the NPC on foot or while seated in a vehicle. Turning past the cone alone must not drop tracking. A solid wall or leaving range still invokes the existing loss/memory rules for that specific player.
4. Down/revive or disconnect/rejoin B while A remains active. A's timer/memory must remain independent and B must not inherit the old avatar's acquisition.
5. Repeat against a simple foot-patrol gunner, with the guest entering/exiting a car. WW NPC verbose sight/engagement logs should refer to the actual chosen player rather than an unrelated host target. In-game verification belongs to Denis.

## Next important step

Give seated gunners and patrol drivers valid WW per-target perception/knowledge, select the closest known eligible player, and carry only actual known identities across dismount. Current TrafficFactions/TrafficAgent and CrewReturn still have generic nearest-player or host-only awareness paths; these were deliberately left for the next checkpoint. After that step, build, save the report and pause again as requested.
