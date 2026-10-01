# Castle art provenance

These original albedo textures were generated with the built-in image_gen tool for DOCX sections 2, 4 and 5. They are material maps used on real Unity geometry, not game screenshots. Selected PNG bytes were copied unchanged into Assets/LevelPresentation/Textures; originals remain in the tool output directory. Unity imports use a 1024-pixel maximum, mipmaps and repeat sampling. Generated images can contain imperfect edge continuity; repeated surfaces require rendered inspection.

## WeatheredLimestone.png

Path: `Assets/LevelPresentation/Textures/WeatheredLimestone.png`.

Prompt:

> Use case: stylized-concept. Asset type: production seamless albedo texture for the 3D stone walls of a dark Gothic castle game. Create ONE square 1024x1024 full-bleed tileable material swatch, orthographic flat front view. Subject: weathered dark grey limestone ashlar, fairly regular horizontal courses of hand-cut rectangular blocks, roughly 6 blocks across and 8 rows high, narrow recessed mortar joints, chipped uneven edges, fine pitting and subtle aged mineral variation, tiny sparse traces of dark moss in some joints. Broad neutral diffuse illumination, no cast shadows, no directional lighting, no vignette, no perspective, no highlights baked in. Mid-value desaturated grey stone that remains readable under cool moonlight and warm torch light; realistic texture detail with slightly painterly restraint. All four edges must repeat seamlessly. Avoid architecture silhouettes, borders, text, symbols, figures, doors, windows, props, watermarks. This is a flat texture image to apply to real 3D geometry, not an illustration of a castle.

## WornFlagstone.png

Path: `Assets/LevelPresentation/Textures/WornFlagstone.png`.

Prompt:

> Use case: stylized-concept. Asset type: production seamless albedo texture for 3D Gothic castle flagstone floors. Create ONE square full-bleed 1024x1024 tileable material swatch. Perfectly orthographic top-down flat material, no perspective. Worn grey limestone paving, irregular but fitted large rectangular slabs, approximately 5 slabs across and 6 rows, mixed lengths, fine shallow cracks, chipped edges, narrow dark dusty joints, subtle mineral variation and subdued traces of moss in occasional crevices. Weathered medieval craft with realistic small texture detail, slightly painterly restraint. Neutral diffuse lighting, mid-grey values, no directional light, cast shadows, vignette, baked highlights or wet mirror reflections. All four edges must repeat seamlessly. No walls, scenery, loose rubble, objects, blood, characters, text, writing, symbols, logos or watermarks. This is one flat surface texture for real game geometry, not a scene illustration.

## Meshes, materials and animation

`CastlePresentationBuilder` and `CastleActorMeshes` author original mesh geometry directly in Unity. The saved environment and actor assets are reproducible from those sources; their existing asset GUIDs are retained when regenerated. `CastleActorBuilder` supplies the four actor rigs and coloured URP materials, while `CastleActorPresentation` animates visual child transforms from actual movement, combat and health state. No external character, architecture or animation pack was added.

The environment uses the project's existing `Assets/FreeNightSky/Materials/nightsky1.mat` sky and its existing texture references through a separate `CastleNight.mat` copy with adjusted exposure/tint. Its original files remain unchanged. This note records reuse already present in the repository; it does not assert a new licence for that pre-existing pack.

The two generated source images are 1254×1254 PNGs, preserved unchanged despite the prompts requesting 1024×1024. Unity's import maximum limits runtime texture size. Source SHA-256 values:

- WeatheredLimestone: `0655a227dac30a4534985e9b87c61a6b54a502888715479b92c0c10e0095951c`
- WornFlagstone: `a6a24ef0e02fa92aee4822881fc20f2498f54848892d1ba53ade7837cbac967a`
