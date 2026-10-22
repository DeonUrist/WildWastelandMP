# Local character bodies and portrait selector — 2026-10-18, Testing

Final checkpoint: Denis supplied the finished eight RGBA portraits, removed the duplicate Mechanic/Pistoleer choices, requested eight adjacent pictures without names with females together, and authorized commit/build. Windows Release/Deploy=true passed with zero warnings/errors. Built/installed/unique stage MD5 `747D3E92FA5400531E4D9CD36B73EDA4`; stage `bin/Release/WildWastelandMP_portraits-checkpoint_20261018_233935.dll`. Both games closed; client plugins shares the install. No game launch/harness, push/release or base/external DLL changes. Earlier ten-placeholder/no-build statements are superseded by the final section and tests below; gameplay/visual/performance checks remain pending.

Denis reports that additional MP bodies are visible to the other player but disappear in the owner's first-person body and local third-person view. His reproduction emits unreadable vertices/normals/UV/indices for `merchant.002`, followed by six bone-weight/vertex-count errors. He also requested Gungirl and Shoota and ten vertical portrait choices with editable placeholders, with no rendered character artwork.

## Cause and source change

`PlayerBody/Body/MultiplayerModels.cs` previously read `original.vertices`, `normals`, `uv` and `triangles` from unreadable native meshes. Unity returns empty geometry while native bone weights remain available; all six calls to `Model.Make` then receive incompatible arrays. The other player's native renderer does not perform this local rebuild, explaining the different result.

`NativeGeometry` now uses the existing Unity `SkinnedMeshRenderer.BakeMesh` mechanism for unreadable meshes. A private mesh copy receives temporary bind poses that cancel the current bone transforms, so the result contains original mesh-space geometry rather than an already animated body. Original skin weights/bind poses feed the same six variants and filters. The renderer's original shared mesh is restored in `finally`; both temporary meshes are destroyed. Readable inputs retain direct reads; invalid geometry is rejected before building variants. No imported game mesh is modified. This is a source correction awaiting compilation and Denis's visual test.

API references: [Unity BakeMesh](https://docs.unity3d.com/ScriptReference/SkinnedMeshRenderer.BakeMesh.html), [Unity 2020.3 bind poses](https://docs.unity3d.com/2020.3/Documentation/ScriptReference/Mesh-bindposes.html), [Unity 2020.3 renderer bindings](https://github.com/Unity-Technologies/UnityCsReference/blob/2020.3/Runtime/Export/Graphics/GraphicsRenderers.bindings.cs). WW already uses BakeMesh for skinned crossbow intersections in `Guns/Tracers/BoltAttachment.cs`; that file is unchanged.

Gungirl and Shoota append enum values 8 and 9; previous values stay stable. They reuse Flexa/Sprokka rigs and the existing Women of Wasteland model/texture settings through `Services.CharacterAssets`. This provider exposes configured asset paths from config binding, independently of NPC simulation or appearance component lifetime. PlayerBody owns its mesh/texture caches, so switching NPC off does not destroy player skins. Existing files/custom models win; missing default assets use the existing NPC installer. Local bodies and remote avatars apply the same skin before animation. Existing first-person weapon arm/leg atlases, TAB inventory pictures and voice selection are unchanged.

Character packets 80/81 and motion 82 accept all ten choices. Handshake revision 14 rejects older peers that cannot display the new choices. Packet shapes, native MP DLL, config keys and save formats are unchanged; `Character` accepts two additional enum names. Base WildWasteland is untouched.

## Photoshop files

Later request: Denis explicitly asked for eight actual enemy-body renders, excluding Male/Female, standing without guns on black. Separate 1024x2048 offline Blender source pictures, contact sheet and ZIP are at `V:/Repos/Apocalypter/_tools/portrait-editing/enemy-bodies-20261018_230957/`. Original native meshes/albedos and installed Women models/textures are used; the main body renderer matches WW's stripped-prop appearance. Merchant/Mechanic and Bodyguard/Pistoleer share the same unadorned mesh/albedo. These are Photoshop inputs, not replacements for the placeholders below, game screenshots or runtime verification. No game launch/build/deploy or live asset/config/save changes.

F3 > PLAYER uses one row of eight vertical pictures without captions. Order: Male, Merchant, Bodyguard, Sprokka, OrganicMechanic, Female, Gungirl, Shoota. The three female choices are adjacent. The supplied final artwork is 320 × 640 RGBA PNGs. No placeholder art remains in the source portrait directory.

Editable source defaults: `src/Modules/PlayerBody/Embedded/CharacterPortraits/`.

On opening PLAYER, missing defaults extract to `BepInEx/plugins/WildWastelandMP/PlayerBody/Portraits/`. Existing PNGs are never overwritten; changed PNGs reload when PLAYER settings reopen. File names:

- `Male.png`, `Merchant.png`, `Bodyguard.png`, `Sprokka.png`, `OrganicMechanic.png`
- `Female.png`, `Gungirl.png`, `Shoota.png`

Keep filenames; PNG transparency is supported. The 1:2 aspect ratio fits the portrait area. Source defaults are embedded in the DLL; installed files still override and reload on opening settings. The old Models/player_character_2_UI*.png files remain on disk but no longer select TAB artwork. Native TAB RawImages and the CleanHUD mirror use the same selected portrait as PLAYER; native texture/UV restore on shutdown/scene change. TAB replacement retains the existing ReplaceDriver toggle. Portrait updates run before 3D body creation so a missing body cannot block the picture. Legacy Mechanic/Pistoleer enum IDs and names remain stable for config/protocol compatibility; old configs and live changes canonicalize to Merchant/Bodyguard. Both the normal portrait selector and Developer body chooser omit the duplicate choices.

## Verification and manual test

Final Windows build/deploy passed zero warnings/errors as recorded above. The portrait follow-up touches six source files, including the shared existing Core selector and PlayerBody inventory/model-choice paths, and embeds the eight user-supplied PNGs. No new config keys in this follow-up. Integrated DLL also contains earlier pending shared-checkout fixes. No game launch, harness or push/release. Visual fit and performance remain pending.

Both peers need this same DLL:

1. Open F3 > PLAYER. Expect exactly eight tall pictures in one row, no names: Male, Merchant, Bodyguard, Sprokka, Organic Mechanic, Female, Gungirl, Shoota. Verify the three female pictures are adjacent and the selected border follows clicks. No Mechanic/Pistoleer choice should appear in either normal or Developer selection.
2. For Merchant first, then every native choice, look down in first person; switch to third person, walk/crouch and draw/holster a weapon. Expect visible body geometry, ordinary animation and no unreadable-mesh or bone-weight bounds errors.
3. Select Gungirl and Shoota on each peer; verify the local body and other player's view use the matching female mesh/texture while standing, aiming, reloading, climbing and seated. Retest offline and with NPC disabled; no NPC spawns or AI are needed for selection.
4. Recheck Male/Female, passenger/driver views, normal car and motorcycle hands. Native first-person weapon arms remain their existing atlas implementation.
5. Replace an installed portrait PNG in Photoshop and reopen PLAYER. Expect the replacement at the same name/aspect, selected border intact. Reopen again; the image must remain untouched.
6. With global VerboseLog on, expect `[PlayerBody] Body Merchant: <N> vertices; first-person meshes ready (native mesh baked)` and corresponding lines for other native choices. Character changes retain `[PlayerBody] Character: <name>`. Gungirl/Shoota should report nonzero vertices; their readable replacement mesh does not need the baked suffix.
7. Select each of the eight models and open TAB. Expect that exact PNG in the survivor portrait, with the same transparency and full frame. Test both native TAB and CleanHUD TAB, restart/load and runtime PlayerBody off/on. With ReplaceDriver off, expect the original native portrait; turning it on restores the selected picture. Old Mechanic/Pistoleer configurations should resolve to Merchant/Bodyguard without shifting other enum IDs. A live edited portrait should refresh in both PLAYER and TAB after reopening settings.
