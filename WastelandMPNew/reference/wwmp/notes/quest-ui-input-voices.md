# Quest conversation input and temporary voice suppression

2026-10-19 — Testing. Requested for both WWMP and singleplayer WW, including commits and DLL builds.

The native conversation acquires EditorSession and blocks button actions, but its existing hooks do not cover MouseLook. Multiplayer intentionally keeps time running during the modal. Native MouseLook.DoMouseLook reads Input.GetAxis directly and updates its cached yaw/pitch, without consulting cursor visibility, button hooks or time scale. Player's Look FSM uses those actions for PlayerCameraHolder and then copies yaw to Direction (Sources/fsm/scene1_game/Player.md:467; Sources/code/Assembly-CSharp/HutongGames.PlayMaker.Actions/MouseLook.cs).

PluginRuntime adds one Quests-owned prefix on DoMouseLook: while the native conversation is open, skip only actions owned by the player. This catches both OnUpdate and non-repeating OnEnter calls before rotation/angle accumulation; existing PlayerBody orbiting OnUpdate hook remains separate. Closing, scene reset and module shutdown clear NativeOpen through the existing lifecycle. CleanHUD and fallback conversation use the same guard; PlayerBody may be off. No scene scan, new job, Core change or time-scale change.

Voice.Say has a temporary static readonly false gate before directory discovery/loading/playback. Keep decoding, recordings and existing saved voice settings for later restoration. This suppresses authored quest offer/accept/remind/done lines, including those invoked through the optional Talk quest adapter; ordinary NPC/player sounds and Talk's own dialogue remain as before. No config, protocol or save-format changes. Two source files per repo, seven added/one replaced lines, plus patch inventory and handover documentation.

Denis's tests:

1. On each MP peer, approach a friendly NPC and press F (or rebound interaction). Move the mouse widely; the view must stay fixed while hovering, scrolling and selecting quest/hire choices. Close via CLOSE and Escape, then verify normal looking resumes without a stored mouse turn. Repeat with CleanHUD off and PlayerBody off, and after loading a world again.
2. Ask for work, accept, reopen/remind and turn in a quest that has installed recordings. Text, choices, progress and rewards must work silently. Other game sounds remain audible.
3. Repeat in singleplayer using its staged DLL when appropriate. Existing singleplayer repo-only/no-deployment policy is retained.

Startup evidence: `[Quests] Conversation mouse-look guard installed.` confirms Harmony accepted the patch; `[Quests] Quest voicelines temporarily disabled (recording set incomplete).` confirms the voice policy initialized. Neither proves in-game behavior. No game launch, harness, push or release.

Final MP Windows Release/Deploy=true build passed with zero warnings/errors. Built, installed and unique stage MD5 `B1CC8BCA3C452A4B89CB71DE3C4358D9`; stage `bin/Release/WildWastelandMP_quest-ui-voices_20261019_011745.dll`. Games were closed and disabled WW manifests absent. Concurrent Core sleeper edits appeared during the first shared-tree compile, so the final build uses an isolated archive of HEAD `50dac88` plus only the two Quests source changes; handshake remains 16. The earlier shared-tree DLL `EEA4F6901846D389523798A884F2161A` is superseded and is not the final installed build. Other session edits were retained uncommitted.

Base Windows Release/Deploy=false build passed with zero warnings/errors; unique stage `../WildWasteland/bin/Release/WildWasteland_quest-ui-voices_20261019_011657.dll`, MD5 `8BFFC327B7E225E820A605A41BFCFF22`. Not deployed under its standing repo-only policy.
