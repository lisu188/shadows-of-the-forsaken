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

The environment uses the project's existing `Assets/FreeNightSky/Materials/nightsky1.mat` sky and its existing texture references through a separate `CastleNight.mat` copy with adjusted exposure/tint. Its original files remain unchanged. The package identification and remaining licence evidence are recorded below.

The two generated source images are 1254×1254 PNGs, preserved unchanged despite the prompts requesting 1024×1024. Unity's import maximum limits runtime texture size. Source SHA-256 values:

- WeatheredLimestone: `0655a227dac30a4534985e9b87c61a6b54a502888715479b92c0c10e0095951c`
- WornFlagstone: `a6a24ef0e02fa92aee4822881fc20f2498f54848892d1ba53ade7837cbac967a`

## Existing Free Night Sky package — issue #20

Checked on **2026-10-01**. The two material and twelve texture `.meta` files consistently identify the package below; see [nightsky1 material metadata](../Assets/FreeNightSky/Materials/nightsky1.mat.meta) and [front texture metadata](../Assets/FreeNightSky/Textures/nightsky1_front.png.meta). These files entered this repository in initial commit `657ebd4`; that Git date and the importer `timeCreated` values do not identify when the owner acquired a licence.

| Field | Recorded evidence |
| --- | --- |
| Package | **Free Night Sky** |
| Imported version | **1.0**, from `AssetOrigin.packageVersion` |
| Asset Store product | **79066**, from `AssetOrigin.productId` |
| Imported upload | **153041**, from `AssetOrigin.uploadId` |
| Importer licence label | `licenseType: Store`; this is metadata, not a licence grant |
| Publisher | **qianyuez**, publisher **22883**, identified by the [official product page](https://assetstore.unity.com/packages/2d/textures-materials/sky/free-night-sky-79066) and its embedded product/publisher metadata |

The current official product record has `customLicense: false` and empty `licenseText`. Unity's [Asset Store terms](https://unity.com/legal/as-terms), dated **December 4, 2024**, identify the standard Asset Store EULA in Appendix 1, while allowing separately supplied provider terms (§1.2). Appendix 1 §2.2.1 describes incorporation into a product containing substantial original content and distribution of the embedded asset; §2.2.2 addresses restricted assets. These are the currently published terms, not a recovered copy of the owner's original agreement. Unity also retains a [January 1, 2023 legacy version](https://unity.com/legal/as-terms-legacy), explicitly replaced in December 2024.

Unity's [commercial-use guidance](https://support.unity.com/hc/en-us/articles/205623589-Can-I-use-assets-from-the-Asset-Store-in-my-commercial-game) distinguishes use of non-restricted free assets in games from distribution of assets as standalone items. The package name's word “Free” does not establish a public-domain or open-source licence, and this provenance entry does not grant permission to redistribute the original PNGs/materials separately.

**Still missing for the project's licence record:** an owner-held acquisition record identifying this package and the applicable licence; the original downloaded package or accompanying terms for version 1.0/upload 153041; and evidence addressing distribution of the source assets in the repository. No licence/readme or acquisition record was found among this pack's tracked files. The current listing identifies the publisher and a standard-terms reference, but does not establish the owner's original acquisition or any separate permissions. Issue #20's provenance/usage acceptance therefore remains open; no conclusion about the legality of the existing use is made here. Acquisition evidence can be retained privately with a redacted reference rather than committing account details or credentials.
