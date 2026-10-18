# Kick presentation — Testing, 2026-10-18

Denis clarified that the wrong texture is on his first-person female kick leg;
the slow animation is the third-person kick viewed by the other player.

The native first-person Player_leg uses Player2 and previously received the
older Player2_female_arms.png, containing bare thigh/older boot artwork. Current
Player_female.png has trousers and different boots. Arms.cs now gives only the
Player_leg mesh its own material. Model.cs extracts the embedded female_kick.png
to Models/Player2_female_kick.png only when missing, then uses its ordinary texture
loader. Existing arm pixels/files remain untouched. A custom ArmsTexture path
keeps its existing leg artwork. Male/native characters retain their prior path.

The observer's ModAPI CharPlan.Kick started the complete Kick clip at speed 1;
the local Body in Animation/Loco.cs:643-648 explicitly fits it into 0.55 seconds.
CharPlan now uses the same length/0.55 speed, duration and fade. Native Attack
damage/cooldown and network counters are unchanged. Source native references:
Sources/fsm/scene1_game/PlayerCameraHolder.md:5710 (Animation on waits 0.55 s),
:5730 (Attack fire sends Animation Activate and immediately spherecasts).
This aligns remote playback duration to the existing local presentation; it does
not introduce a delayed damage event or remove network interpolation latency.

Python texture rebaking was explicitly authorized in chat. Local authoring script:
bin/kick-texture/bake_leg.py, using the segment-projection method from the original
Apocaplayer/tools/bake.py. Inputs are the installed Player_female.glb/png and the
existing offline Assets/raw/Mesh/Player_leg__5298.obj plus its serialized bind
transforms. Native OBJ X mirroring is reversed, native UVs retained, right-leg
segments projected to the current model, nearest surface texture sampled and
UV borders padded 12 pixels. Only the leg material consumes this new atlas.
Asset inspection is not game appearance verification; Denis must check seams,
stretching, trousers/boots and lighting in the actual kick.

Three C# files: Animation/CharPlan.cs, Body/Arms.cs, Body/Model.cs (31 added,
7 removed lines), plus Embedded/Textures/female_kick.png (394360 bytes).
Windows Release/Deploy=true: zero warnings/errors. Output, installed DLL and
unique bin/Release/WildWastelandMP_kick_20261018_170306.dll share MD5
07546149419C3A6B7ABC19AECFF3A798. Includes preceding Name and punching fixes.
No new kick config, protocol/save changes, game execution, commit or push.

Denis's retest:

1. Restart both copies. Select Female on the client; first-person kick should
   show her current trousers and boots, without the old exposed upper-leg art.
2. Watch that client from the host while kicking singly and repeatedly: each
   visible kick should complete in the same 0.55 s window as the local kick.
   Repeat with host kicking while client observes, then try Male.
3. Check that kicks still damage at the existing native time and repeat rate;
   check punches, held weapon attacks, first-person arm textures and view changes.
4. Separately verify the preceding Name field persists with Enter/restart and
   that the chosen names appear in chat/save requests on the other copy.

With PlayerBody verbose logging enabled, loading the new atlas reports
`Texture Player2_female_kick.png (2048x2048)`. No new timing diagnostic was added.
