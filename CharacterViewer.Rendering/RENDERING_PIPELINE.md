# CharacterViewer Rendering Pipeline

A reference for how `CharacterViewer.Rendering` parses NIF meshes and renders them, with side-by-side notes on how NifSkope and Outfit Studio handle the same problems.

---

## Table of contents

1. [Pipeline overview](#pipeline-overview)
2. [Part 1 — NIF parsing](#part-1--nif-parsing)
   - [Geometry](#geometry)
   - [BSLightingShaderProperty fields](#bslightingshaderproperty-fields)
   - [BSShaderTextureSet slots](#bsshadertextureset-slots)
   - [NiAlphaProperty](#nialphaproperty)
   - [Skinning](#skinning)
   - [Shader flag inventory](#shader-flag-inventory)
3. [Part 2 — Fragment shader pipeline](#part-2--fragment-shader-pipeline)
   - [Vertex shader (brief)](#vertex-shader-brief)
   - [Stage 1: base color & alpha test](#stage-1-base-color--alpha-test)
   - [Stage 1b: tint operations](#stage-1b-tint-operations)
   - [Stage 2: normal calculation](#stage-2-normal-calculation)
   - [Stage 3: dynamic lighting](#stage-3-dynamic-lighting)
   - [Stage 4: environment mapping](#stage-4-environment-mapping)
   - [Stage 5: emissive](#stage-5-emissive)
   - [Stage 6: tone-map, fresnel, vignette](#stage-6-tone-map-fresnel-vignette)
   - [Final: eye alpha & framebuffer](#final-eye-alpha--framebuffer)
4. [Part 3 — Comparison with NifSkope and Outfit Studio](#part-3--comparison-with-nifskope-and-outfit-studio)
5. [Appendix A — Shader flag inventory table](#appendix-a--shader-flag-inventory-table)
6. [Appendix B — BSLightingShaderProperty field inventory](#appendix-b--bslightingshaderproperty-field-inventory)
7. [Appendix C — Texture slot inventory](#appendix-c--texture-slot-inventory)

---

## Pipeline overview

```
.nif file on disk
      │
      ▼
[niflysharp] NifFile.Load(path)
      │
      ▼
[Nif/NifMeshBuilder.cs] BuildFromFile / BuildAllShapes / BuildShape
   ├─ extract per-shape geometry (verts, normals, tangents, UVs, tris)
   ├─ extract BSLightingShaderProperty fields
   ├─ extract BSShaderTextureSet slot paths
   ├─ extract NiAlphaProperty flags + threshold
   ├─ resolve bones via skeleton NIF, do CPU-side skinning
   └─ produce a list<BuiltMesh> POCOs
      │
      ▼
[ViewModels/VM_CharacterViewer.cs] ApplyTexturesToGlMesh
   ├─ apply ARMA TXST overrides on top of NIF-baked texture paths
   ├─ load each texture via TextureManager (Pfim → BGRA32 → GL)
   └─ set per-shape uniforms on a GlMesh (HasFaceTintMap, TintColor, …)
      │
      ▼
[Gl/GlRenderer.cs] DrawScene
   ├─ shadow pass (Shaders/shadow_depth.*)
   ├─ SSAO pass (Shaders/ssao.frag, ssao_blur.frag)
   ├─ main pass (Shaders/basic.vert + basic.frag)
   └─ wireframe pass (Shaders/wireframe.*)
```

The data path is intentionally one-way: niflysharp → POCO → GlMesh → uniforms → GL. There's no round-trip back to the NIF, no in-memory edit of NIF data; the renderer is read-only with respect to the file.

---

## Part 1 — NIF parsing

All NIF reads go through [Nif/NifMeshBuilder.cs](Nif/NifMeshBuilder.cs), which takes a path or an open `NifFile` and returns a list of `BuiltMesh` POCOs. The POCO is a snapshot — once it exists the `NifFile` can be disposed without affecting the renderer.

### Geometry

Per shape ([NifMeshBuilder.cs:589 BuildShape](Nif/NifMeshBuilder.cs#L589)):

| Data | Source | Notes |
|---|---|---|
| Vertex positions | `nif.GetVertsForShape(shape)` | Vector3 list |
| Normals | `nif.GetNormalsForShape(shape)` | If absent or all-zero, regenerated from triangle face normals |
| UVs | `nif.GetUvsForShape(shape)` | Vector2 list |
| Triangles | `shape.GetTriangles(out vec)` | Indices into the vertex list |
| Tangents | `nif.GetTangentsForShape(shape)` | When absent, MikkTSpace-style derivation from positions+UVs |
| Bitangents | `nif.GetBitangentsForShape(shape)` | Same fallback |
| Vertex colors | per-vertex via the shape's geometry data | Only when SLSF2_Vertex_Colors is set on the BSLSP **and** the shape actually carries vertex-color data |

Coordinate space conversion: the renderer uses Y-up world space with the character facing −Z. NIFs are Z-up, so positions go through `(x, z, −y)` and normals/tangents through the same swizzle plus a re-normalization in [BuildShape](Nif/NifMeshBuilder.cs#L589).

### BSLightingShaderProperty fields

Read at [NifMeshBuilder.cs:809-902](Nif/NifMeshBuilder.cs#L809) when the shape's `ShaderPropertyRef` resolves to a `BSLightingShaderProperty`. Each field is read in a `try/catch` so version-specific fields not present on older NIFs simply default rather than throwing. See [Appendix B](#appendix-b--bslightingshaderproperty-field-inventory) for the full read/ignore table.

The fields actually consumed downstream are:

- `shaderFlags1`, `shaderFlags2` — propagated to `BuiltMesh.ShaderFlags1/2` and reinterpreted at the GlMesh-binding stage.
- `bslspShaderType` — propagated to `BuiltMesh.ShaderType`. The renderer branches on `4` (BSLSP_FACE), `5` (BSLSP_SKINTINT), `6` (BSLSP_HAIRTINT), and `16` (BSLSP_EYE).
- `glossiness`, `specularStrength`, `specularColor` — Blinn-Phong specular params.
- `subsurfaceRolloff` — drives the SSS wrap term.
- `rimlightPower` — exponent for the silhouette falloff used by hair backlight and skin rim.
- `grayscaleToPaletteScale` — multiplier for the hair-tint shader path.
- `emissiveColor`, `emissiveMultiple` — additive emission.
- `uvScale`, `uvOffset` — per-shape UV transform applied in the vertex shader.
- `environmentMapScale`, `eyeCubemapScale` — env-map intensity (different fields for normal vs eye shader).
- `hairTintColor` — only when `bslspShaderType == BSLSP_HAIRTINT`.

Fields explicitly ignored (read but not used, or not read at all) include `softlighting`, `backlightPower`, `refractionStrength`, all wetness/parallax/subsurface-color fields, and `skinTintColor`/`skinTintAlpha`. The skin tint stored in the NIF is always `(1, 1, 1)` with `alpha=0` for the bodies we've inspected — the engine doesn't pull body tint from there at runtime; it pulls from NPC record `TextureLighting` (QNAM), which is loaded by the host and passed in via `ResolvedNpcMeshPaths.TextureLightingColor`.

### BSShaderTextureSet slots

Read via `nif.GetTexturePathByIndex(shape, slot)` for slots 0–8 ([NifMeshBuilder.cs:782-787](Nif/NifMeshBuilder.cs#L782)).

| Slot | Conventional meaning | Used by renderer |
|---|---|---|
| 0 | Diffuse / albedo | Always |
| 1 | Normal map (TSN or MSN) | Always |
| 2 | Glow / SkinTint / detail palette | For skin shapes only (BSLSP_FACE, BSLSP_SKINTINT) — drives SSS mask. For other shader types, slot 2 has different meaning (glow, detail palette) and is intentionally not bound to avoid spurious red SSS. |
| 3 | Height / Parallax / FacegenDetail | For face shapes when SLSF1_Facegen_Detail_Map is set |
| 4 | Environment cubemap | When SLSF1_Environment_Mapping or SLSF1_Eye_Environment_Mapping is set |
| 5 | Environment mask | Paired with slot 4 |
| 6 | Tint / Inner (FaceTint) | For the primary head shape when the NPC has a FaceTint .dds (slot 6 is unused on body shapes) |
| 7 | Backlight / Specular | When present, used as a specular mask (`.r` channel) |
| 8 | (unused) | Never bound |

ARMA TXST records can override slots at render time via `ResolvedNpcMeshPaths.TxstTextures[bodyPart][slot]` — applied in `VM_CharacterViewer.ApplyTexturesToGlMesh` on top of the NIF-baked path before the texture is loaded.

### NiAlphaProperty

Per shape that has an `AlphaPropertyRef` ([NifMeshBuilder.cs:962-991](Nif/NifMeshBuilder.cs#L962)):

- `flags & 0x0200` → `HasAlphaTest` — gates the `discard` in the fragment shader.
- `flags & 0x0001` → `HasAlphaBlend` — host enables `GL_BLEND` and disables depth write for the shape.
- `threshold / 255.0f` → `AlphaThreshold` — drives the `discard` cutoff.

`SrcBlend` / `DstBlend` enums in the alpha-property flags are not honored — the renderer assumes standard `GL_SRC_ALPHA / GL_ONE_MINUS_SRC_ALPHA` for any alpha-blended shape. In practice this matches the engine for hair, lashes, brows, and the wet-eye outer cornea; non-default blend equations are extremely rare in actor NIFs.

### Skinning

CPU-side, in [TryApplyCpuSkinning](Nif/NifMeshBuilder.cs#L1380). For each bone the shape weights to:

1. Read the bone name list (`nif.GetShapeBoneList`).
2. Get the inverse bind-pose transform from the **shape NIF** (`nif.GetShapeBoneTransform(shape, i, ...)`).
3. Get the bone's world transform from the **skeleton NIF** (`skeletonNif.GetNodeTransformToGlobal(boneName)`).
4. Compose `boneWorld * inverseBind` → cached `CachedSkinTransform` per bone.

Per vertex: read up to 4 bone-weight pairs from `nif.GetShapeBoneWeights(...)`, accumulate the weighted bone transform on the vertex position and (with the rotation portion only) on the normal. The result becomes the renderer's `Positions` array; the original NIF positions are kept as `BindPosePositions` for BodySlide morphing.

Why CPU-side skinning instead of a GPU vertex shader doing it: BodySlide morphing happens after skinning and needs to operate on the deformed positions. Doing skinning in-shader would require re-running the morph at every redraw, multiplying CPU work for no rendering gain.

### Shader flag inventory

What [Appendix A](#appendix-a--shader-flag-inventory-table) catalogs by bit. Summary:

- **Flags we honor directly** (the shader/host branches on them): `Specular`, `Greyscale_To_PaletteColor`, `Environment_Mapping`, `Facegen_Detail_Map`, `Model_Space_Normals`, `Eye_Environment_Mapping`, `Hair_Soft_Lighting`, `Own_Emit`, `Double_Sided`, `Vertex_Colors`, `Soft_Lighting`, `Rim_Lighting`.
- **Flags we honor *indirectly* via a different signal that's reliably paired with the flag in actor NIFs**: `Skinned` (we check niflysharp's bone-list directly), `ZBuffer_Write` (we drive `GL_DEPTH_MASK` from `NiAlphaProperty.HasAlphaBlend`).
- **Flags we ignore** because the data we render reliably doesn't depend on them, the feature isn't implemented, or the flag is non-actor-specific. See Appendix A for the per-flag rationale.

The "ignore" bucket is large because most SLSF1/SLSF2 bits exist for engine paths we don't share — landscape rendering, LOD fadeout, parallax/decal/refraction shaders, vehicle texture remapping, fire/water effects, weapon-blood splatter. These never appear on actor body/face/hair shapes and adding code paths for them would be dead weight. The ones worth flagging as "could matter and we don't do them" are `Parallax`, `Anisotropic_Lighting` (hair specular), `Back_Lighting` (skin transmission), and `Glow_Map` (slot 2 emissive modulation) — see Appendix A notes for each.

---

## Part 2 — Fragment shader pipeline

The main fragment shader is [Shaders/basic.frag](Shaders/basic.frag). The vertex shader [Shaders/basic.vert](Shaders/basic.vert) does little beyond standard MVP transforms and TBN construction; everything interesting happens per-fragment.

### Vertex shader (brief)

[basic.vert](Shaders/basic.vert) outputs:

- `gl_Position` (clip space) and `v_viewSpacePos` (for view-space lighting math).
- `TexCoords = aTexCoords * u_uvScale + u_uvOffset` — the BSLSP `uvScale`/`uvOffset` get folded in here, not in the fragment shader, so per-fragment tex sampling is just a direct `texture(...)` call.
- `vertexColor` (passed through; only used when `has_vertex_colors`).
- `v_tangentToViewMatrix` — view-space TBN built from per-vertex tangent/bitangent/normal. Used by the tangent-space normal-map path.
- `v_modelToViewNormalMatrix` — used by the model-space normal-map path.
- `v_worldPos` — for shadow-map lookup in the fragment shader.
- `v_worldNormal` — for the `DEBUG_VIZ_WORLD_NORMAL` debug branch.

The `u_model` matrix is identity for the static character preview (the character is at the origin, the camera orbits around it). That assumption is leveraged by `DEBUG_VIZ_MSN_NORMAL` which treats model-space and world-space as interchangeable.

### Stage 1: base color & alpha test

[basic.frag:246-261](Shaders/basic.frag#L246).

```
baseColor = texture(diffuse, TexCoords);
if has_vertex_colors: baseColor.rgb *= vertexColor.rgb; baseColor.a *= vertexColor.a;
if use_alpha_test && baseColor.a < alpha_threshold: discard;
```

**Why vertex colors here, not later**: vertex color in Skyrim NIFs is part of the diffuse data path — it's premultiplied into the albedo before any other shading. NifSkope's `sk_default.frag` does the same (`albedo = baseMap.rgb * C.rgb`). Outfit Studio also does the same.

**Alternative considered**: gate vertex-color application on a debug toggle. Rejected because vertex colors *are* the surface color when present (typical case: brows, where vertex color modulates a generic brow texture).

### Stage 1b: tint operations

[basic.frag:263-289](Shaders/basic.frag#L263). Three mutually-exclusive paths plus a fourth that runs in addition:

```
if has_greyscale_to_palette:     // hair, BSLSP_HAIRTINT shader type
    baseColor.rgb = baseColor.rrr * tint_color * greyscaleToPaletteScale;
else if has_tint_color:           // body, BSLSP_SKINTINT shader type
    baseColor.rgb *= tint_color;

if has_detail_map:                // face shapes with slot 3 populated
    baseColor.rgb = overlayBlend(baseColor.rgb, detailSample);

if has_face_tint_map:             // primary head shape with slot 6
    if MSN: baseColor.rgb = overlayBlend(baseColor.rgb, tintSample);
    else:   baseColor.rgb *= tintSample;
```

#### Greyscale-to-palette (hair tint)

Only on shapes with `bslspShaderType == BSLSP_HAIRTINT`. The diffuse texture is monochrome — only the red channel is meaningful — and gets multiplied by `tint_color × greyscaleToPaletteScale`. The host populates `tint_color` from the NIF's `BSLSP.hairTintColor` for this path (overridden by the NPC record's `HairColor` FormLink when present).

**Alternative**: simple RGB multiply (`baseColor.rgb *= tint_color`). Used when SLSF1_Greyscale_To_PaletteColor is **not** set but the shape is still a hair-tint shader. The choice happens host-side in [VM_CharacterViewer.cs:2902-2929](ViewModels/VM_CharacterViewer.cs#L2902).

#### Skin tint (body)

`baseColor.rgb *= tint_color`. The `tint_color` uniform is set host-side from the NPC's QNAM `TextureLighting` color when `built.ShaderType == 5`. This is what gives a vanilla generic male body its per-NPC warm-tan instead of looking the same on every character.

The NIF's `BSLSP.skinTintColor` is **not** read for this — it's reliably `(1,1,1)` on every body NIF in the wild. The engine doesn't use the NIF field; it uses the NPC record. We follow the engine.

#### Detail map (face)

[basic.frag:271-274](Shaders/basic.frag#L271). When SLSF1_Facegen_Detail_Map is set and slot 3 has a texture, `overlayBlend(baseColor, detailSample)` adds detail (skin pores, stubble) on top of the diffuse before the FaceTint pass.

#### FaceTint (face)

[basic.frag:280-289](Shaders/basic.frag#L280). The interesting one. Two blend modes are gated on `is_model_space` (the SLSF1_Model_Space_Normals flag):

- **MSN faces → overlay blend.** Matches NifSkope's `sk_msn.frag`. Vanilla FaceGen always bakes MSN, and so do common replacers (UNP, CBBE, 3BA, BHUNP, HIMBO).
- **Non-MSN faces → multiply blend.** Matches the apparent in-game behavior for tangent-space face replacers like UBE. NifSkope's `sk_default.frag` (the non-MSN counterpart) has no FaceTint code at all; multiply is the closest practical fit that still preserves per-pixel features (freckles, lipstick) which a "no-op" path would lose.

This is the only place the renderer makes a decision based on MSN beyond the normal-map sampling itself. See the [FaceTint commit history](#) for the full empirical derivation; the short version is that overlay's `2·base·tint` branch (taken for `base < 0.5`, which describes most skin diffuses) brightens the face by 2× when the FaceTint texture's mean RGB > 0.5 — which is essentially every FaceTint. For MSN faces this matches the engine; for non-MSN faces the engine doesn't apply FaceTint at all, leaving overlay 2× off.

`FACE_TINT_MODE` const at the top of [basic.frag](Shaders/basic.frag#L70) forces overlay (`1`) or multiply (`2`) for cross-checking.

### Stage 2: normal calculation

[basic.frag:291-323](Shaders/basic.frag#L291). Three paths:

```
if has_normal_map:
    if is_model_space:
        // MSN: sample as model-space, flip Z (NIF Z-up → renderer Y-up), transform to view space
    else if tbnIsValid:
        // TSN: sample, G-flip (DirectX convention), TBN transform to view space
    else:
        // No tangents available, fall back to vertex normal in TBN[2]
else:
    // No normal map, use vertex normal
```

**MSN path**: Bethesda's MSN textures are stored in Y-up local model space (character faces +Z). Our renderer is Y-up world space (character faces −Z), so the only swizzle needed is a Z flip. There is **no** DirectX G-flip on the MSN path — that's a tangent-space convention only.

**TSN path**: standard tangent-space normal mapping with the DirectX G-flip (`normal_tangentSpace.g *= -1.0`). The TBN matrix is constructed in the vertex shader from the per-vertex tangent/bitangent.

**Alternatives considered**: deriving normals via screen-space derivatives of position (`dFdx/dFdy` cross product). NifSkope's `sk_msn.frag` has commented-out code for this. Rejected because vertex normals are correct on Bethesda meshes (we've verified this via the `DEBUG_VIZ_WORLD_NORMAL` debug branch); face normals would lose smoothing.

### Stage 3: dynamic lighting

[basic.frag:325-472](Shaders/basic.frag#L325). The largest stage. Up to `MAX_LIGHTS = 5` lights are accumulated in a single loop:

```
for each light:
    if type == 1 (ambient):  finalColor += lightColor * baseColor.rgb * ao;
    if type == 2 (directional):
        compute diffuse, specular, backlight, rimlight, subsurface, eye-catchlight;
        accumulate into finalColor
```

Each directional light contributes:

#### Diffuse

Lambert (`max(NdotL, 0)`), upgraded to a "wrap" lighting term (`NdotL * 0.5 + 0.5`) when SLSF2_Soft_Lighting is set. Wrap lighting is the cheapest skin SSS approximation — it lets light bleed slightly past the terminator into the shadow side, removing the harsh terminator that pure Lambert produces on faces.

**Alternative**: half-lambert (`(NdotL * 0.5 + 0.5)^2`). Produces a similar effect but with a sharper midtone falloff. Rejected because Bethesda's actual `lightingEffect1` value (read into our `subsurfaceRolloff` uniform) drives a parameterized wrap, applied separately in the SSS path; doing half-lambert here would double up.

#### Shadows

`sampleShadowPCF` ([basic.frag:175-210](Shaders/basic.frag#L175)). 3×3 PCF kernel on top of GPU `sampler2DShadow` bilinear PCF (= 36 effective samples). Slope-scale bias to avoid acne at grazing angles. **Only the key light (index 1)** casts shadows — fill and rim lights are arranged to bounce or wrap, so shadowing them produces double-darkening in occluded regions.

**Alternative**: cascaded shadow maps (CSM). Rejected because the character is at a known fixed distance from the camera (orbit), so a single ortho shadow map covering ~2× character bounds is plenty. CSM is overkill for a portrait viewer.

#### Specular

Blinn-Phong with the BSLSP's `specularColor`, `specularStrength`, `glossiness`. Modulated by slot 7's `.r` channel when present (the specular mask), or 1.0 (full strength) when absent.

`shadow` factor is applied so specular highlights vanish in shadow correctly.

**Alternative**: GGX microfacet BRDF. Rejected for portraits — Blinn-Phong with reasonable `glossiness` (30–80 for skin, 200+ for eyes) gives a clean specular highlight that matches Bethesda's reference look. GGX would add roughness-aware shoulders that aren't authored into the data.

#### Hair backlight

When SLSF1_Hair_Soft_Lighting is set: `pow(1 - VdotN, rimlightPower) * u_backlightColor * lightColor * NdotL * baseColor`. This is the classic hair "translucent edge" effect — visible only at silhouette angles, gated on the lit side via `NdotL`.

`u_backlightColor` is a host-side uniform, default warm-amber, dialed by the user in the lighting preset.

#### Rim lighting (skin)

When SLSF2_Rim_Lighting is set: `pow(1 - VdotN, rimlightPower) * lightColor * baseColor`. Different from hair backlight in that it doesn't gate on `NdotL` — rim shows on silhouette regardless of lit side. Used for skin to suggest the slight backscatter at ear/nose silhouettes.

#### Subsurface scattering

[basic.frag:413-445](Shaders/basic.frag#L413). Two terms:

- **Forward scatter**: a wrap-lighting variant `(NdotL + R) / (1 + R)` where R = `subsurfaceRolloff` (BSLSP's `lightingEffect1` per the Bethesda spec). At R=0 this is pure Lambert; at R=1 it's half-lambert.
- **Back scatter / transmission**: `pow(max(-NdotL, 0), 3)` — bright where the light is *behind* the surface relative to the viewer. Tightened by the `pow 3` so only thin backlit edges glow.

The SSS color is `mix(vec3(1.0, 0.35, 0.25), baseColor.rgb, 0.4)` — 60% warm flesh tint, 40% the surface color. The 40% bias toward `baseColor` is so dark-skinned NPCs don't get unrealistically bright-red SSS while still reading as warm.

The mask comes from slot 2 (`_sk.dds` for vanilla; the `.r` channel). Vanilla bodies ship with a 4×4 black `_sk.dds` (`mean RGB = (0,0,0)` per the diagnostic dumper), which effectively disables SSS — the engine's same observation made via the empty-mask convention. UBE follows the same convention.

`u_subsurfaceStrength` is a host-side global multiplier (0 disables, 1 is honest source-value SSS, >1 boosts).

**SSS is added separately, not multiplied by `baseColor`** — `sss_color` already mixes in the surface color, so a final multiply would double-tint and look muddy.

#### Eye catchlight

When `is_eye` (BSLSP_EYE shader type, ID 16) and the light is the key light: a tight Blinn-Phong spot at glossiness 256 added *on top of* `baseColor`-multiplied lighting. The "wet eye" sparkle we expect in portraits.

**Why on top of baseColor**: a real eye catchlight is the studio key reflecting off the wet cornea, not tinted by the iris pigment. Multiplying by iris color (`baseColor`) would make the catchlight green on green eyes, etc. We want it to stay white-on-iris.

### Stage 4: environment mapping

[basic.frag:474-488](Shaders/basic.frag#L474). When SLSF1_Environment_Mapping or SLSF1_Eye_Environment_Mapping is set and slot 4 has a texture:

```
reflectDir = reflect(-viewDir, normal_viewSpace);
// Spherical 2D mapping (NOT a cubemap)
uv.x = reflectDir.x / m + 0.5;
uv.y = reflectDir.y / m + 0.5;  // m = 2 * sqrt(rx² + ry² + (rz+1)²)
envColor = texture(envmap, uv).rgb;
envMask = has_env_mask ? texture(envmask, TexCoords).r : 1.0;
finalColor += envColor * envMask * scale;
```

`scale` is `eyeCubemapScale` for eye shapes, `envMapScale` otherwise.

**Why spherical 2D and not a cube map**: this is technically wrong, just visually invisible. Bethesda's `EyeCubeMap.dds` ships as 128×32 BC7_UNORM — a 4:1 aspect ratio that's almost certainly a strip or cross cubemap layout, not an equirectangular spherical projection. NifSkope and Outfit Studio both load it as a real `samplerCube` and sample with `texture(cubemap, reflectionVector)`. Our [GlTextureManager.LoadCubemap()](Gl/GlTextureManager.cs#L176) uploads it as a `TextureTarget.Texture2D` and the shader samples it with sphere-map UV math — the math is incorrect for the actual texture layout. The mismatch is hidden by content: the file's mean RGB is `(5.4, 5.5, 5.4)` (≈2% intensity), so the env-map contribution is dim enough that the dominant eye highlight comes from the dedicated catchlight pass (which *is* correct), not from the env reflection. If we ever bump the env intensity or use a brighter cubemap, the artifact would surface as a wrong rotation of the reflection vs the head pose. Logged as future-work; the fix is to call `glTexImage2D(GL_TEXTURE_CUBE_MAP_POSITIVE_X + face, ...)` six times after splitting the strip layout, and switch the shader sampler to `samplerCube`.

### Stage 5: emissive

[basic.frag:490-493](Shaders/basic.frag#L490). When SLSF1_Own_Emit is set: `finalColor += emissiveColor * emissiveMultiple`. Additive, no light interaction — emissive surfaces glow regardless of incident light.

We do **not** support glow maps (slot 2 modulating emission) currently. This affects very few actor shapes; `Own_Emit` without a glow map is the common case.

### Stage 6: tone-map, fresnel, vignette

[basic.frag:495-543](Shaders/basic.frag#L495). All folded under `u_enableToneMapping` so the legacy linear pipeline stays bit-for-bit reproducible when the toggle is off.

#### Fresnel contour darkening

```
fresnel = pow(1 - max(NdotV, 0), 4);
finalColor *= mix(1.0, 0.85, fresnel);
```

~15% darkening at silhouette edges. Real photography has this from grazing-angle microfacet shadowing; without it the silhouette reads as flat-shaded. Folded under the tone-map toggle since both are "finishing touches" that ship together.

#### ACES filmic tonemap

```
c = finalColor * 0.6;
c = (c * (2.51c + 0.03)) / (c * (2.43c + 0.59) + 0.14);  // Narkowicz 2015 ACES approx.
c = mix(luminance(c), c, 1.10);  // mild saturation boost
```

ACES filmic compresses HDR highlights, adds a soft toe in shadows, and produces the warm shoulder that makes skin read as portrait rather than flat linear. Pairs with `FRAMEBUFFER_SRGB` on the host so the framebuffer gamma-encodes on write.

**Alternative**: Reinhard, Hable/Uncharted 2 (NifSkope and Outfit Studio both use this). Reinhard crushes highlights too aggressively for portraits. Uncharted 2 is reasonable but doesn't have ACES's warm shoulder; we use ACES for the "cinematic portrait" feel users expect from a character viewer.

#### Vignette

`smoothstep(u_vignetteRadius, sqrt(2), distFromCenter)` — radial darkening from screen center toward corners. Reads less as a "vignette effect" and more as the natural lens falloff every photographic portrait has. Tunable from 0 (off) to 1 (corners to black) via `u_vignetteIntensity`.

### Final: eye alpha & framebuffer

[basic.frag:545-560](Shaders/basic.frag#L545). The wet-eye outer cornea (BSLSP_EYE + alpha-blend + env-mapping) ships with a near-black diffuse and alpha=1. In the engine, the eye-cubemap reflection writes over that black so the shape reads as a transparent glassy overlay. We don't have a per-shape eye cubemap path in slot 4 for this shape, so the env block is skipped — leaving a solid alpha=1 black void without intervention.

Workaround: for eye shapes only, modulate alpha by the lit luminance so dark cornea pixels become transparent (iris shows through) while specular catchlights stay opaque (wet-eye sparkle preserved). For non-eye shapes baseColor.a is unchanged.

---

## Part 3 — Comparison with NifSkope and Outfit Studio

Both reference applications ship shader source under `res/shaders/`. NifSkope splits Skyrim shading across `sk_default.frag` (179 lines) and `sk_msn.frag` (194 lines); Outfit Studio uses a single `default.frag` (320 lines).

### High-level comparison

| Feature | This renderer | NifSkope | Outfit Studio |
|---|---|---|---|
| Multi-mesh scene | ✓ | ✗ (one shape at a time) | ✓ |
| MSN vs TSN normal maps | ✓ both | ✓ both (separate shaders) | ✓ both (one shader) |
| FaceTint (slot 6) blend | overlay (MSN) / multiply (non-MSN) | overlay+double for MSN; not applied for non-MSN | not applied |
| Detail map (slot 3) | overlay | overlay (MSN only, then doubles albedo) | not applied |
| Tint color (slot N/A, host uniform) | ✓ for skin/hair | ✓ for skin/hair | ✗ |
| SLSF2_Soft_Lighting wrap | NdotL · 0.5 + 0.5 | wrap formula via `lightingEffect1` mask | wrap formula via `softlighting` mask |
| Hair backlight (SLSF1_Hair_Soft_Lighting) | rim·diffuse·baseColor | rim only (no NdotL gate) | not applied |
| Rim light (SLSF2_Rim_Lighting) | rim·baseColor | rim·smoothstep(L·V) | code present but commented out |
| Subsurface scattering | wrap + back-scatter, slot 2 mask | not applied | not applied |
| Specular | Blinn-Phong, slot 7 mask | Blinn-Phong, normalMap.a or slot 7 | Blinn-Phong, normalMap.a or slot 7 |
| Environment mapping | spherical 2D (slot 4) | cubemap (slot 4) | cubemap (slot 4) |
| Eye catchlight | dedicated tight Blinn-Phong spot | not applied | not applied |
| Shadows | PCF directional (key light) | not applied | not applied |
| SSAO | screen-space, separate pass | not applied | not applied |
| Tone-map | ACES filmic (Narkowicz) | Hable/Uncharted 2 | Hable/Uncharted 2 |
| Fresnel silhouette darkening | ✓ | ✗ | ✗ |
| Vignette | ✓ | ✗ | ✗ |

The shorter both reference shaders are reflects their narrower scope: NifSkope previews one mesh at a time without portrait-style finishing; Outfit Studio is an editor where the renderer's job is to show the silhouette of garments while you slide BodySlide weights, not to produce a final-render-quality image.

### Where we differ deliberately

- **FaceTint blend on non-MSN faces.** NifSkope's `sk_default.frag` has zero FaceTint code; Outfit Studio doesn't apply FaceTint at all. Neither tool has the multi-mesh seam problem because (a) NifSkope only renders one shape so there's no neck to compare, (b) Outfit Studio doesn't tint anything. We need *something* on non-MSN faces to preserve per-pixel features (freckles, lipstick) and not produce a brightness skew vs the body. Multiply is the closest practical fit. See [`FACE_TINT_MODE` comment in basic.frag](Shaders/basic.frag#L49).

- **Subsurface scattering for skin.** Neither reference applies SSS. Without it, faces look like plastic mannequins under directional lighting. Our forward-wrap term `(NdotL + R) / (1 + R)` is engine-faithful — `R` is the BSLSP `subsurfaceRolloff` field, which the Bethesda docs document as the wrap-lighting parameter and `lightingEffect1` in the binary schema. The rest of the SSS formulation is our own and was empirically tuned, not derived from observed engine behavior:

  - The warm-flesh `vec3(1.0, 0.35, 0.25)` and the 40% blend toward `baseColor` are hand-picked values that "look right" — Bethesda's engine likely uses pre-integrated SSS via a 2D LUT keyed on `(NdotL, curvature)` ([Penner 2011](https://advances.realtimerendering.com/s2011/Penner%20-%20Pre-Integrated%20Skin%20Rendering%20%28Siggraph%202011%20Advances%29.pptx)) rather than the warm-tint mix we do.
  - The transmission term `pow(max(-NdotL, 0), 3) × sss_color × 0.6` is our own approximation; the engine's transmission likely uses a screen-space gaussian blur of the back-lit translucent contribution rather than a pure analytic falloff.
  - The slot-2 `_sk.dds` mask is read as an SSS strength multiplier (`.r` channel), which matches how the engine documentation describes the texture role, but the resulting contribution we add is hand-tuned, not engine-equivalent.

  Net: visually we land in a portrait-acceptable place (skin doesn't read as plastic, ear/nostril edges glow when backlit), but it's not a faithful reproduction of the engine's SSS. If we ever need a closer engine match — e.g., to match an in-game screenshot pixel-for-pixel — replacing the analytic forward+transmission terms with a pre-integrated LUT is the next step.

- **Eye catchlight.** Bethesda ships a wet-eye outer cornea NIF shape that relies on the engine's eye-cubemap reflection to fake transparency. Our env-map path is spherical-2D rather than cubemap and the per-NPC eye cubemap isn't in slot 4 for this shape, so without the catchlight + alpha-luminance trick the cornea would render as solid black over the iris.

- **Shadows + SSAO + tone-map.** Portrait-quality finishing. NifSkope and Outfit Studio are utility renderers; we're a portrait viewer.

### Where we differ accidentally / by omission

- **Albedo doubling on MSN face shapes.** NifSkope's `sk_msn.frag` does `albedo += albedo` (multiply by 2) after the detail+tint overlays. We don't do this — and the absence of a face/body seam (with tonemap on *or* off) is empirical evidence the engine doesn't either. The doubling is gated on `hasDetailMask = isST(ST_FaceTint)`, which is false for body shapes; if the engine ran the same code, MSN faces would render 2× brighter than bodies in-game, which they don't. NifSkope likely inherits this from older fixed-function-era vertex lighting where the per-vertex `diffuse + emissive` varying was pre-scaled to a smaller range and the doubling compensated. NifSkope's single-mesh preview hides the asymmetry — they never composite an MSN face against a body in the same frame, so the doubling never produces a visible seam in their tool. Treat this NifSkope code as a NifSkope-specific quirk, not a missing feature on our side.

- **Wetness, parallax, refraction.** Bethesda's actual face/body shaders support all of these via dedicated BSLSP fields and shader flags. They're rare on actor meshes — almost no skin shapes set the parallax flag — so the omission is practical, not a bug. If this renderer ever needs to handle armor or weapons more accurately, parallax becomes important.

- **Glow maps (slot 2 emissive modulation).** Some emissive shapes use slot 2 as an emissive mask. We use slot 2 only for SSS on skin shapes; for non-skin emissive shapes we apply `Own_Emit` without modulation. Visually incorrect for, e.g., the Daedric armor enchanted glow, irrelevant for actor preview.

- **NiAlphaProperty src/dst blend equations.** We hard-code `GL_SRC_ALPHA / GL_ONE_MINUS_SRC_ALPHA` for all alpha-blended shapes. NifSkope honors the per-shape blend equations. In the actor shapes we render this distinction never matters; for effect shaders (BSEffectShaderProperty) it would.

---

## Appendix A — Shader flag inventory table

Bit layout per [NifSkope `glproperty.h` lines 450–517](https://github.com/niftools/nifskope/blob/develop/src/gl/glproperty.h#L450).

**Legend for the "Used" column:**
- ✓ — The shader or host code branches on this bit.
- ~ — We don't read this bit, but achieve the same runtime behavior via a different signal that's reliably paired with the flag in actor NIFs.
- ✗ — We don't honor this bit. The "Why we don't use this" column explains whether honoring it would change the output for actor meshes, and what feature would be needed to support it.

### SLSF1 (shaderFlags1)

| Bit | Name | Used | Why we don't use this (or how we honor it indirectly) |
|---|---|---|---|
| 0 | Specular | ✓ | Gates the Blinn-Phong specular pass. |
| 1 | Skinned | ~ | We use a stronger signal: niflysharp's `GetShapeBoneList(shape, ...)` returns the actual bone count from the SkinInstance/SkinData blocks. The flag exists to indicate the *presence* of skinning data; checking the data directly is one extra niflysharp call but correct even on malformed NIFs where the flag is out of sync. |
| 2 | Temp_Refraction | ✗ | Engine-internal flag for water/glass refraction shapes. We don't render refraction. Never set on actor BSLSP meshes. |
| 3 | Vertex_Alpha | ✗ | Tells the engine to consume the vertex color's alpha channel. We use SLSF2_Vertex_Colors as the gate and always multiply `baseColor.a *= vertexColor.a` when it's set. The Vertex_Alpha-without-Vertex_Colors edge case (RGB-only vertex coloring) is rare and not seen on actor shapes. |
| 4 | Greyscale_To_PaletteColor | ✓ | Switches the hair-tint code path to `baseColor.rrr * tint_color * scale`. |
| 5 | Greyscale_To_PaletteAlpha | ✗ | Variant of palette tinting where the texture's alpha (not red) channel drives the lookup. Not seen on actor hair/skin meshes; supporting it would mean a third hair-tint shader branch. |
| 6 | Use_Falloff | ✗ | `BSEffectShaderProperty`-specific; never set on `BSLightingShaderProperty` actor shapes. |
| 7 | Environment_Mapping | ✓ | Enables slot 4 spherical env-map sampling. |
| 8 | Recieve_Shadows | ✗ | We unconditionally sample the shadow map in the lighting loop when `EnableShadows` is true. Honoring this per-shape would require a uniform and a branch but never visibly improves portrait output — actor meshes universally set this flag. |
| 9 | Cast_Shadows | ✗ | We don't read this flag. The shadow pre-pass uses heuristic culls instead ([GlRenderer.cs:888-897](Gl/GlRenderer.cs#L888)): skip pure alpha-blend shapes (their shadow would be a soft amorphous blob), skip eyes (always self-shadowing). The flag is universally set on actor shapes; the heuristic is a stricter filter that produces cleaner shadows than honoring the flag literally would. |
| 10 | Facegen_Detail_Map | ✓ | Enables slot 3 overlay (detail map on faces). |
| 11 | Parallax | ✗ | We don't sample slot 3 as a height map for view-dependent UV displacement. Adding parallax requires a per-pixel raymarch in the fragment shader; non-trivial and rarely set on actor skin/face meshes. Visible regression in theory for parallax-mapped armor; not for actor body/face. |
| 12 | Model_Space_Normals | ✓ | Selects MSN normal-map path *and* FaceTint blend mode (overlay vs multiply). |
| 13 | Non_Projective_Shadows | ✗ | Specialty flag for shapes that should produce undistorted shadows on stairs/etc. Actor-irrelevant. |
| 14 | Landscape | ✗ | Tells the engine the shape is part of the landscape mesh (different shader path entirely). Never set on actors. |
| 15 | Refraction | ✗ | Glass/water refraction effect. Requires a backbuffer copy + UV displacement that we don't have a pipeline stage for. Never set on actors. |
| 16 | Fire_Refraction | ✗ | Same as Refraction, fire-effect-specific. Never set on actors. |
| 17 | Eye_Environment_Mapping | ✓ | Enables slot 4 env-map for BSLSP_EYE shader type. |
| 18 | Hair_Soft_Lighting | ✓ | Enables hair backlight pass. |
| 19 | Screendoor_Alpha_Fade | ✗ | Drives dithered alpha fadeout for LOD distance transitions. We don't do LOD or distance fade in a portrait viewer (the camera orbits at a roughly fixed distance). |
| 20 | Localmap_Hide_Secret | ✗ | Local-map UI flag — controls whether the shape appears on the in-game world map. No rendering implication. |
| 21 | FaceGen_RGB_Tint | ✗ | We don't branch on this bit; we apply `tint_color` unconditionally when `built.ShaderType == 5` (BSLSP_SKINTINT). In actor NIFs the bit is set whenever shader type is 5, so the type acts as a proxy. A hypothetical shape with shader type 5 and this flag *unset* would get over-tinted by us — engine would render it with the BSLSP-stored `(1,1,1)` skinTintColor instead. Never seen in actor data. |
| 22 | Own_Emit | ✓ | Enables emissive pass. |
| 23 | Projected_UV | ✗ | World-projected decal UVs (used by terrain decals, not actor materials). |
| 24 | Multiple_Textures | ✗ | Engine flag for shapes that swap their texture set based on game state. Actor BSLSP doesn't use this. |
| 25 | Remappable_Textures | ✗ | Vehicle/object slot remapping at runtime (e.g., paint job swapping). Not used by actor BSLSP. |
| 26 | Decal | ✗ | Decal-projection mesh. We don't render decals. |
| 27 | Dynamic_Decal | ✗ | Dynamically-updated decal (e.g., bullet hits). We don't render decals. |
| 28 | Parallax_Occlusion | ✗ | Heavy-cost parallax variant with self-occlusion sampling. Not implemented; rarely set on actors. |
| 29 | External_Emittance | ✗ | Tells the engine the emissive value comes from an external source (world lighting, scripts) rather than the BSLSP-stored field. We always use the BSLSP-stored value. |
| 30 | Soft_Effect | ✗ | `BSEffectShaderProperty`-specific. Never set on `BSLightingShaderProperty` actor shapes. |
| 31 | ZBuffer_Test | ✗ | We have `GL_DEPTH_TEST` enabled for the entire main pass. Honoring this per-shape would require disabling depth test for the rare shape that wants to render without depth comparison (sky overlays, screen-space UI). Never appears on actor meshes. |

### SLSF2 (shaderFlags2)

| Bit | Name | Used | Why we don't use this (or how we honor it indirectly) |
|---|---|---|---|
| 0 | ZBuffer_Write | ~ | We honor this functionally via `NiAlphaProperty.HasAlphaBlend`: alpha-blended shapes get `GL.DepthMask(false)` for their pass ([GlRenderer.cs:548-560](Gl/GlRenderer.cs#L548)), opaque shapes get `GL.DepthMask(true)`. Bethesda pairs the two signals 1:1 — every alpha-blended actor shape has this flag *unset* in the BSLSP and the alpha-blend bit *set* in the AlphaProperty. We use the AlphaProperty signal because it's also what tells us to enable `GL_BLEND`, so the two decisions are co-located. |
| 1 | LOD_Landscape | ✗ | LOD landscape shape; never set on actors. |
| 2 | LOD_Objects | ✗ | LOD object shape; never set on actor body parts. |
| 3 | No_Fade | ✗ | Disables LOD distance fade. We don't do LOD fade in the portrait viewer (camera distance is ~fixed). |
| 4 | Double_Sided | ✓ | Disables `GL_CULL_FACE` host-side for the shape. |
| 5 | Vertex_Colors | ✓ | Enables `baseColor *= vertexColor`. |
| 6 | Glow_Map | ✗ | Tells the engine to modulate the emissive output by slot 2 (`_g.dds` for non-skin meshes). We use slot 2 for SSS on skin shapes; for non-skin emissive shapes we apply `Own_Emit` without modulating by a glow texture. Affects enchantment glows and similar effects on armor/weapons; irrelevant for actor skin. |
| 7 | Assume_Shadowmask | ✗ | Tells the engine to interpret the diffuse alpha channel as a shadow mask. Specialty flag, not set on actor shapes. |
| 8 | Packed_Tangent | ✗ | Indicates tangents are stored packed in vertex data with a Bethesda-specific encoding. niflysharp unpacks them transparently — we receive standard `vec3` tangents from `nif.GetTangentsForShape(shape)` regardless of this bit. |
| 9 | Multi_Index_Snow | ✗ | Snow-coverage variant for outdoor objects. Actor-irrelevant. |
| 10 | Vertex_Lighting | ✗ | Tells the engine to use baked vertex lighting and skip dynamic lights. We always do dynamic lighting; honoring this would skip the directional + shadow + specular contributions, which would look broken in a portrait. Actor meshes don't ship with baked vertex lighting anyway. |
| 11 | Uniform_Scale | ✗ | Optimization hint indicating the shape has uniform world-space scale (so the normal matrix doesn't need the inverse-transpose). We compute the inverse-transpose unconditionally; the saving wouldn't be measurable on a portrait scene. |
| 12 | Fit_Slope | ✗ | Landscape-specific terrain conformance. Actor-irrelevant. |
| 13 | Billboard | ✗ | Tree/grass billboard rotation around the world-up axis. Actor-irrelevant. |
| 14 | No_LOD_Land_Blend | ✗ | LOD landscape blending. Actor-irrelevant. |
| 15 | EnvMap_Light_Fade | ✗ | Fades the environment-map contribution by `NdotL`. Subtle effect on highly-reflective actor parts (eyes); our env-map sampling has bigger correctness issues than this anyway (see Stage 4 note about treating cubemaps as 2D textures), so this fadeoff is not currently the limiting factor. |
| 16 | Wireframe | ✗ | Engine wireframe rendering mode. We have a dedicated wireframe shader ([Shaders/wireframe.frag](Shaders/wireframe.frag)) used as a viewport mode toggle, so this BSLSP bit is not consulted. |
| 17 | Weapon_Blood | ✗ | Blood splat overlay on weapons. Actor-irrelevant. |
| 18 | Hide_On_Local_Map | ✗ | Local-map UI flag. No rendering implication. |
| 19 | Premult_Alpha | ✗ | Diffuse uses premultiplied alpha. We assume straight alpha across the board; misapplying this on an alpha-blended shape would over-darken the fringes. Not seen on actor skin/hair/lashes. |
| 20 | Cloud_LOD | ✗ | Cloud rendering. Actor-irrelevant. |
| 21 | Anisotropic_Lighting | ✗ | Hair-strand-direction anisotropic specular. We use isotropic Blinn-Phong for hair. Honoring this would mean implementing a Kajiya-Kay or Marschner BRDF and reading hair-flow tangents. The absence is most visible on long, dramatically-lit hair; in our portrait setup with subtle backlight + isotropic spec, it reads acceptably. |
| 22 | No_Transparency_Multisampling | ✗ | MSAA tuning flag. We don't use MSAA in the portrait viewer (we rely on shader AA + sRGB framebuffer). |
| 23 | Unused01 | ✗ | Reserved bit, no engine semantics. |
| 24 | Multi_Layer_Parallax | ✗ | Heavy parallax variant. Not implemented. |
| 25 | Soft_Lighting | ✓ | Switches diffuse from Lambert to wrap (`NdotL * 0.5 + 0.5`). |
| 26 | Rim_Lighting | ✓ | Enables skin rim pass. |
| 27 | Back_Lighting | ✗ | Per-pixel back-lighting using a backlight texture. We have a hair-soft-lighting term but no separate skin back-lighting path. Visible on thin-skinned regions (ears, finger webs) lit from behind; in our default lighting setup these regions don't get strong back-light, so the absence isn't striking. Adding it would mean reading slot 7 as a backlight texture (currently used for spec masks) under a different shader-flag check. |
| 28 | Unused02 | ✗ | Reserved. |
| 29 | Tree_Anim | ✗ | Tree-animation flag. Actor-irrelevant. |
| 30 | Effect_Lighting | ✗ | `BSEffectShaderProperty`-specific. Never set on `BSLightingShaderProperty`. |
| 31 | HD_LOD_Objects | ✗ | HD LOD shape variant. Actor-irrelevant. |

---

## Appendix B — BSLightingShaderProperty field inventory

What we read from the shape's BSLSP, where it goes, and what we ignore.

| Field | Read | Used as | Notes |
|---|---|---|---|
| `shaderFlags1` | ✓ | Bit-tested per Appendix A | |
| `shaderFlags2` | ✓ | Bit-tested per Appendix A | |
| `bslspShaderType` | ✓ | Branches in host code | Selects FACE/SKINTINT/HAIRTINT/EYE rendering paths |
| `glossiness` | ✓ | `materialGlossiness` uniform | Blinn-Phong exponent |
| `specularStrength` | ✓ | `materialSpecularStrength` uniform | Spec multiplier |
| `specularColor` | ✓ | `specularColor` uniform | RGB |
| `softlighting` | ✗ | (bypassed) | We use `subsurfaceRolloff` for the wrap term instead |
| `rimlightPower` | ✓ | `rimlightPower` uniform | Exponent for hair backlight + skin rim |
| `subsurfaceRolloff` | ✓ | `subsurfaceRolloff` uniform | Controls SSS wrap |
| `backlightPower` | ✗ | (unused) | We don't do back-lighting separately |
| `grayscaleToPaletteScale` | ✓ | `greyscaleToPaletteScale` uniform | Hair-tint multiplier |
| `fresnelPower` | ✗ | (unused) | We have a fixed `pow(..., 4)` in the tone-map fresnel |
| `emissiveColor` | ✓ | `emissiveColor` uniform | RGB |
| `emissiveMultiple` | ✓ | `emissiveMultiple` uniform | Scalar |
| `uvScale` | ✓ | Vertex shader `u_uvScale` | Folded into TexCoords at vertex stage |
| `uvOffset` | ✓ | Vertex shader `u_uvOffset` | Same |
| `environmentMapScale` | ✓ | `envMapScale` uniform | |
| `eyeCubemapScale` | ✓ | `eyeCubemapScale` uniform | Used when `is_eye` |
| `refractionStrength` | ✗ | (unused) | We don't do refraction |
| `skinTintColor` | ✗ | (unused) | NIF stores `(1,1,1)` — the engine doesn't use this for body tinting at runtime; it pulls QNAM from the NPC record. We follow the engine. |
| `skinTintAlpha` | ✗ | (unused) | Same |
| `hairTintColor` | ✓ | `tint_color` uniform (when ShaderType==HAIRTINT) | Overridden by `INpcGetter.HairColor` resolution when present |
| `parallaxInnerLayerThickness` | ✗ | (unused) | Parallax not supported |
| `parallaxRefractionScale` | ✗ | (unused) | |
| `parallaxInnerLayerTextureScale` | ✗ | (unused) | |
| `parallaxEnvmapStrength` | ✗ | (unused) | |
| `wetnessSpecScale` … `wetnessMetalness` | ✗ | (unused) | Wetness not supported |
| `lumEmittance` … `finalExposureMax` | ✗ | (unused) | Newer-version exposure controls; only meaningful in FO76-era files |
| `subsurfaceColor` | ✗ | (unused) | Hardcoded warm-flesh `(1.0, 0.35, 0.25)` in the SSS shader |
| `transmissiveScale` | ✗ | (unused) | |
| `sparkleParameters` | ✗ | (unused) | |

---

## Appendix C — Texture slot inventory

Per the BSShaderTextureSet convention, slot meanings vary by shader type. The renderer's interpretation:

| Slot | Skin shaders (FACE, SKINTINT) | Hair (HAIRTINT) | Eye (EYE) | Default |
|---|---|---|---|---|
| 0 | Diffuse | Diffuse (greyscale palette) | Iris diffuse | Diffuse |
| 1 | Normal (TSN or MSN) | Normal | Normal | Normal |
| 2 | SSS mask (`.r` channel) | (unused) | (unused) | (unused — could be glow map but we don't use it) |
| 3 | Detail map (when SLSF1_Facegen_Detail_Map) | (unused) | (unused) | (unused — could be parallax height) |
| 4 | Env cubemap (spherical 2D) | (unused) | Eye cubemap | Env cubemap |
| 5 | Env mask | (unused) | Env mask | Env mask |
| 6 | FaceTint (face shapes only, primary head) | (unused) | (unused) | (unused) |
| 7 | Specular mask (`.r`) | (unused) | (unused) | Specular mask |
| 8 | (unused) | (unused) | (unused) | (unused) |

The "primary head shape" gating for FaceTint happens host-side: among all shapes in a FaceGen NIF, only the one with the largest height bounding box gets FaceTint applied. Accessory shapes (mouth, eyes, brows, lashes) share the FaceGen NIF but each have their own diffuse and don't need the per-NPC tint baked at character creation.
