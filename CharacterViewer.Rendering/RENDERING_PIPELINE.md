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
   - [Parse cost & performance](#parse-cost--performance)
   - [Dismember partitions and shape filtering](#dismember-partitions-and-shape-filtering)
   - [Shader flag inventory](#shader-flag-inventory)
   - [Override channels (textures + meshes)](#override-channels-textures--meshes)
3. [Part 2 — Fragment shader pipeline](#part-2--fragment-shader-pipeline)
   - [Vertex shader (brief)](#vertex-shader-brief)
   - [Stage 1: base color & alpha test](#stage-1-base-color--alpha-test)
   - [Stage 1b: tint operations](#stage-1b-tint-operations)
   - [Stage 1c: skin saturation boost](#stage-1c-skin-saturation-boost)
   - [Stage 2: normal calculation](#stage-2-normal-calculation)
   - [Stage 3: dynamic lighting](#stage-3-dynamic-lighting)
   - [Stage 4: environment mapping](#stage-4-environment-mapping)
   - [Stage 5: emissive](#stage-5-emissive)
   - [Stage 6: tone-map, fresnel, vignette](#stage-6-tone-map-fresnel-vignette)
   - [Final: framebuffer alpha](#final-framebuffer-alpha)
4. [Part 3 — Editor overlay pass (markers, lines, regions)](#part-3--editor-overlay-pass-markers-lines-regions)
   - [Geometry accessors](#geometry-accessors)
   - [The debug shader + overlay GL state](#the-debug-shader--overlay-gl-state)
   - [Overlay channels](#overlay-channels)
   - [Region overlay (BodySlide classifier)](#region-overlay-bodyslide-classifier)
5. [Part 4 — Comparison with NifSkope and Outfit Studio](#part-4--comparison-with-nifskope-and-outfit-studio)
6. [Appendix A — Shader flag inventory table](#appendix-a--shader-flag-inventory-table)
7. [Appendix B — BSLightingShaderProperty field inventory](#appendix-b--bslightingshaderproperty-field-inventory)
8. [Appendix C — Texture slot inventory](#appendix-c--texture-slot-inventory)

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
   ├─ SSAO depth + normal prepass (Shaders/depth_only.*)
   ├─ SSAO pass (Shaders/ssao.frag, ssao_blur.frag)
   ├─ main pass (Shaders/basic.vert + basic.frag)
   ├─ wireframe pass (Shaders/wireframe.*)
   └─ editor overlay pass (Shaders/debug.*) — markers, measurement lines,
      region cap-loops / solid / wireframe; depth test OFF (always on top)
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
- `emissiveColor`, `emissiveMultiple` — emissive contribution. Multiplied by `baseColor.rgb` at the shader's emissive stage so the emissive is "absorbed" by the surface color (matching NifSkope's `albedo * (diffuse + emissive)` math); dark regions of the diffuse don't glow.
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

**TXST overrides apply to skin shapes only** (`BSLSP shaderType == 5` / `ST_SkinTint`). Body / Hands / Feet NIFs can ship non-skin shapes that share the same NIF — vanilla `FemaleBody_1.nif` carries a `FemaleUnderwear` clothing shape alongside the body, hands NIFs sometimes carry fingernail accessories, etc. Those shapes share the body-slot dismember partition (32 / 33 / 37) but use a default-shader path with their own NIF-baked diffuse and normal. The ARMA TXST diffuse for the body part is *skin* (`FemaleBody_1.dds`); applying it indiscriminately to every shape in the NIF clobbers that clothing shape with skin detail it shouldn't have. Gating on shader type 5 lets multi-shape skin NIFs (some body replacers split the torso into more than one skin shape) still receive the override on every skin shape, while leaving non-skin attachments alone. Applied in two places: the install-time fold into `effectiveTextures` ([VM_CharacterViewer.cs:2758](ViewModels/VM_CharacterViewer.cs#L2758)) and the post-load `ApplyTextureOverrides` target filter ([VM_CharacterViewer.cs:3334](ViewModels/VM_CharacterViewer.cs#L3334), via `GlMesh.IsSkinShape`).

### NiAlphaProperty

Per shape that has an `AlphaPropertyRef` ([NifMeshBuilder.cs:962-991](Nif/NifMeshBuilder.cs#L962)):

- `flags & 0x0200` → `HasAlphaTest` — gates the `discard` in the fragment shader.
- `flags & 0x0001` → `HasAlphaBlend` — host enables `GL_BLEND` and disables depth write for the shape.
- `threshold / 255.0f` → `AlphaThreshold` — drives the `discard` cutoff.

`SrcBlend` (bits 1-4) and `DstBlend` (bits 5-8) enums in the alpha-property flags are honored per-mesh. NifMeshBuilder extracts the indices, BuiltMesh / GlMesh carry them through to the renderer, and Pass 2 calls `glBlendFunc` per-mesh via a Bethesda-enum → OpenTK `BlendingFactor` mapping in [GlRenderer.cs](Gl/GlRenderer.cs). The vast majority of actor alpha-blended shapes use `SRC_ALPHA / INV_SRC_ALPHA` (standard "over" transparency); the notable exception is the UBE-style wet-eye outer cornea, which ships `SRC_ALPHA / ONE` (additive) so a near-black cornea adds nothing to the iris underneath while bright catchlight pixels add brightness. Honoring the per-mesh factors is what removes the need for any special-case shader logic to render the cornea correctly.

#### `HasAlphaTest` is suppressed for `ShaderType == 4` face shapes

`VM_CharacterViewer.ApplyTexturesToGlMesh` overrides `glMesh.UseAlphaTest = false` when `built.ShaderType == 4 && built.HasAlphaTest`. The NIF's `HasAlphaBlend` and `AlphaThreshold` are preserved unchanged; only the GL-side `discard` gate flips off.

**Observed:** Vanilla `MaleHeadKhajiit` (and likely other beast-race face NIFs) carries `NiAlphaProperty.AlphaTest=True, threshold=73` together with diffuse alpha < 1 across the entire face surface and `SLSF1_Vertex_Alpha` with per-vertex alpha varying down to ~0.325 in a ring around the head/body seam (133 of 1356 vertices on Ri'saad's head). The shader's vertex-alpha-into-texture-alpha multiply produces face fragments with α ≈ 0.27–0.28 along that seam — below the 0.286 threshold — and the GL `discard` drops them. At ~750 px portrait resolution each dropped fragment is a pixel-sized hole that lets the cleared background bleed through; the visible result is the head reading as dimmer or smaller than the body. Vanilla in-game Skyrim does **not** produce this discard pattern.

**Inferred but not verified:** The engine-side mechanism that makes vanilla Khajiit faces render solid. Plausible candidates we haven't traced: the engine ignoring the alpha-test bit on `BSLSP_FACE`, an alpha-to-coverage path running on a differently-configured framebuffer (note that this renderer's Pass-1 deliberately disables `SAMPLE_ALPHA_TO_COVERAGE` per the comment in `GlRenderer.cs`), `BSLSP_FACE` being routed through a separate engine draw call that doesn't read `NiAlphaProperty`, or the actual in-game vertex-alpha values differing from what niflysharp reads back from the NIF. No engine source or Community Shaders' replacement-shader review has been done for this specific path.

Suppressing the discard at `ShaderType == 4` produces output that empirically matches vanilla in-game appearance even though the engine-side mechanism for the match remains open. The face's alpha data still reaches `FragColor.a` (the shader writes it honestly); the off-screen pipeline's alpha-strip at readback handles the downstream PNG consequence (see [Part 2 framebuffer alpha](#final-framebuffer-alpha)). Vertex_alpha-driven head/body seam fade could be reintroduced via a soft-blend path later if a use case appears; the seam is invisible at typical portrait viewing angles.

### Skinning

CPU-side, in [TryApplyCpuSkinning](Nif/NifMeshBuilder.cs#L1380). For each bone the shape weights to:

1. Read the bone name list (`nif.GetShapeBoneList`).
2. Get the inverse bind-pose transform from the **shape NIF** (`nif.GetShapeBoneTransform(shape, i, ...)`).
3. Get the bone's world transform from the **skeleton NIF** (`skeletonNif.GetNodeTransformToGlobal(boneName)`).
4. Compose `boneWorld * inverseBind` → cached `CachedSkinTransform` per bone.

Per vertex: read up to 4 bone-weight pairs from `nif.GetShapeBoneWeights(...)`, accumulate the weighted bone transform on the vertex position and (with the rotation portion only) on the normal. The result becomes the renderer's `Positions` array; the original NIF positions are kept as `BindPosePositions` for BodySlide morphing.

Why CPU-side skinning instead of a GPU vertex shader doing it: BodySlide morphing happens after skinning and needs to operate on the deformed positions. Doing skinning in-shader would require re-running the morph at every redraw, multiplying CPU work for no rendering gain.

### Parse cost & performance

Parsing the per-NPC NIFs is the dominant CPU cost of generating a mugshot, so the pipeline works hard to hide it. Two mechanisms:

- **Parse cache** ([NifMeshBuilder.cs](Nif/NifMeshBuilder.cs) `BuildFromFile`): keyed on `(nifPath, nifMTime, skeletonPath, skelMTime, bipedBodyPart)`, holds cloned `BuiltMesh` snapshots. Shared body parts (`Body`/`Hands`/`Feet`/`Hair`/`Tail`) are LRU-**protected**; the per-NPC FaceGen head and null-part attire/outfit meshes are evicted first (they're one-shot). Measured hit rate ≈ 81% on a ~94-NPC mod.
- **Prewarm offload** ([GameWindowOffscreenRenderer.cs](Offscreen/GameWindowOffscreenRenderer.cs) `PrewarmAsync` → `CharacterPreviewCache.PrewarmNpc`): the GL-free parse + DDS decode run on a thread-pool worker ahead of the single GL render thread, so by the time a tile renders it hits warm caches and the render thread pays only GL upload + draw + readback. After a full prewarm the render thread's own `parseMs` is ≈ 0; the parse cost lives on the workers.

**Where the parse cost goes** (measured, prewarm-worker, ~94-NPC mod; per cache-missed part):

| Phase | Share of parse | What it is |
|---|---|---|
| `NifFile.Load` | ~10% | native file parse — at the libnifly floor |
| **Geometry marshaling** | **~51%** | the per-element `vectorVector3.getitem` + `.x/.y/.z` reads over verts/normals/UVs/tangents/bitangents/colors/triangles |
| Skinning | ~36% | per-vertex bone-weight reads (same per-element marshaling) + per-bone transform setup + the C# skinning math |
| rest | ~3% | shader/texture/alpha reads |

The geometry slice is expensive because niflysharp (a SWIG binding) surfaces each `std::vector<Vector3>` as a wrapper whose indexer does **one managed→native crossing per element**, returning a finalizable `Vector3` whose `.x/.y/.z` are three more crossings — so reading one vertex costs ~4 crossings + one GC-tracked allocation. A high-poly head runs to ~190k crossings + ~48k finalizable objects. There is **no bulk-copy fast path** in the stock binding (`ToArray`/`CopyTo` are per-element), and niflysharp 2.0.x does not add one (and is net10-only, so unusable from this net8 library).

The only lever that removes this is a native bulk-copy helper (`memcpy` from the contiguous `std::vector<Vector3>` into a managed `float[]`), which must live inside the niflyswig binding — a fork + native rebuild. It would roughly halve parse cost; **deferred** as of 2026-06 given the build/maintenance overhead.

**Instrumentation** (gated, production-safe — zero cost unless the trigger file is present): drop `LogNifCacheDiag.txt` next to the exe → `RenderLogs/NifCacheDiag.log` prints a `parse split` line every 25 parses (load/build %, then geom/skin shares of build). Drop `LogRenderTimings.txt` → `RenderLogs/RenderTimings.csv` gets per-NPC `loadMs`/`buildShapesMs` columns (captured on the prewarm worker). Counters: `_threadLoad/Build/Geom/SkinTicks` in [NifMeshBuilder.cs](Nif/NifMeshBuilder.cs).

**In-RAM cache byte budgets** ([SystemMemoryBudget.cs](SystemMemoryBudget.cs)). Three decode caches are byte-budgeted rather than entry-capped (a 4K texture is ~16× a 1K one, so a count cap is meaningless): the **pixel** cache (decoded BGRA32, dominant), the **mesh** parse cache (built geometry), and the **cubemap** cache (envmaps). They hold a fixed **75 : 9 : 1** ratio — fractions `0.75 / 0.09 / 0.01` of free RAM summing to `0.85` (`BaselineFreeRamFraction`). The ratio was calibrated from a 50-NPC prewarm measurement: pixel demand dominated (~2 GB resident), mesh was tiny (~57 MB, and in practice bounded by its 96-entry count cap, not bytes), cubemap negligible (~0.1 MB). The mesh share is set higher than its measured demand (0.09 vs a demand-matched ~0.04) as deliberate headroom for high-poly mesh replacers, where 96 cached entries *can* exceed the byte budget and would otherwise evict below the count cap. Sizing is governed by `ICharacterViewerSettings` and re-polled every N inserts (so a host change lands within a few renders), across three modes ([RenderCacheMode](RenderCacheMode.cs)):

- **`PercentFreeRam`** (default) — the caches may collectively use `FreeRamCachePercent`% (default **85**, reproducing the historical fractions) of *reclaimable-free* RAM (OS-free + what the cache already holds, minus a `max(2 GB, 20%)` headroom reserve), split by the ratio. **The same percent applied to total physical RAM is the upper ceiling** — one knob is the single source of truth for both target and cap; there is no separate per-cache ceiling. Raising it caches more aggressively; lowering it frees RAM for the rest of the app.
- **`FixedRam`** — the ratio is applied to a user-set fixed pool (`FixedCacheBudgetBytes`) instead of live free RAM, capped at each cache's ratio share of total physical RAM so an oversized pool can't exceed the machine.
- **`Disabled`** — budget 0; caches retain nothing (a render still holds the pixels it fetched, so this is safe, just non-reusing).

SynthEBD surfaces all three via General Settings (`CacheMode` / `CacheFreeRamPercent` / `CacheFixedBudgetGB`, bridged by `SynthEbdSettingsAdapter`). The 75 : 9 : 1 ratio was validated against NPC2's `CacheRatioMeasurementDiagnostic` (50 diverse NPCs, prewarm path); revisit if the mesh count cap or texture resolutions change materially.

### Dismember partitions and shape filtering

NIF shapes that ship with a `BSDismemberSkinInstance` carry a list of dismember-partition IDs — Bethesda's `BIPED_OBJECT` enum (32 = body / torso, 33 = hands, 37 = feet, 30 / 130 / 230 = head, 31 = hair, 40 = tail). NifSkope labels them via the shared body-part enum on each partition entry. The renderer makes two decisions during NIF parse based on these IDs.

**Primary-head election** ([NifMeshBuilder.cs FindAccessoryOffsetAndPrimaryHead](Nif/NifMeshBuilder.cs)). Inside FaceGen NIFs only, the tallest shape (by local Z extent) whose partitions intersect `{30, 130, 230, 1}` is tagged `IsPrimaryHeadShape = true`. Three downstream consumers depend on the flag:

- **MeshAware camera framing** — its `PrimaryHead` selector matches this shape, and `AboveLowerYOfPrimaryHead` filter clips accessory bbox contributions to verts above this shape's chin.
- **FaceTint application** — `ApplyTexturesToGlMesh` blends the per-NPC FaceTint mask onto this shape's slot-0 diffuse.
- **Accessory positioning** — for unskinned shapes whose vertices are authored near the origin, this shape's global transform is used to position the accessory.

The election is gated on the presence of a NIF block named `BSFaceGenNiNodeSkinned` in the file's block table. Vanilla SSE FaceGen NIFs always carry one; body / hands / feet NIFs do not. The scope matters because Bethesda's vanilla child meshes (`ChildFeet.nif`) bundle a placeholder `ChildHead` shape with the same `partitions = [1, 0]` as the FaceGen face mesh. Without the scope, that placeholder would win the election in the body NIF, and the FaceTint-blend branch would then composite the per-NPC FaceTint onto the body-slot TXST diffuse, producing a dark-face artifact.

The partition set is `{30, 130, 230, 1}`. The first three are SSE-modern; partition 1 is the legacy Oblivion-era `BP_HEAD` value. Vanilla child face meshes (`MaleHeadChild`, `ChildHead`) ship with `[1, 0]` and never got migrated to the modern numbering — NifSkope still labels partition 1 as `BP_HEAD` via the body-part enum, so this is the authoritative signal rather than a name heuristic.

**Biped-slot shape filter** ([NifMeshBuilder.cs BuildAllShapes](Nif/NifMeshBuilder.cs)). When a NIF is loaded under one of three body parts:

| Body part label | Required partition |
|---|---|
| `Body` | 32 |
| `Hands` | 33 |
| `Feet` | 37 |

shapes whose dismember partitions don't include that slot are skipped before `BuildShape` runs. Treats each ARMA's WorldModel NIF as the source for one biped slot only. For Dorthe-style child meshes the filter drops `ChildHead`, `EyesChild`, `MouthChild`, `BODY`, `Wrists` from the scene when `ChildFeet.nif` is loaded as Feet — leaving only the actual `Feet` shape (partition 37).

Head / Hair / Tail body parts are intentionally exempt. The reasoning is empirical and discussed under [Survey findings](#survey-findings-shape-filtering) below: dismember partitions are not a reliable indicator of shape role for those NIFs.

**Engine-side mechanism is undocumented.** Skyrim's engine clearly skips these placeholder shapes in-game (no one sees Dorthe with duplicate eyes through her boots), but neither the CK wiki nor any source we have access to describes the precise rule. The most plausible candidate — "engine renders shapes whose partition includes the ARMA's biped slot" — is consistent with the data and with Bethesda's authoring habits, but has not been confirmed against engine source. Treat the biped-slot filter as a working approximation of the engine's behavior, validated empirically (see [Survey findings](#survey-findings-shape-filtering)) but not as a documented contract.

#### Survey findings (shape filtering)

A diagnostic batch run (`MeshSurveyRunner` in NPC Plugin Chooser 2's host code) walked one NPC per enabled appearance mod with a non-empty mod folder — 250 NPCs / 2,943 shapes total — and emitted per-shape metadata to CSV. Notable observations:

| Body part | Distinct partition lists observed |
|---|---|
| `Body` | `[32]` (252×), `[38;32;34]` (100×), `[32;38;34]` (99×), `[32;32]` (15×), `[32;34;38]` (3×), `[32;53]` (2×), `[38;32]` (1×), `[32;34]` (1×) |
| `Hands` | `[33]` (233×), `[33;33]` (16×) |
| `Feet` | `[37]` (249×) |

Every Body shape carries partition 32. Every Hands shape carries 33. Every Feet shape carries 37. **The biped-slot filter dropped zero shapes across the corpus** — no false positives in adult NPCs.

For Head shapes the partition picture is much messier:

- **58 face accessories carry partition `[32]`** (the body slot) despite being mouth, brows, eyes, lashes, or eyeshadow inside the FaceGen NIF — `FemaleMouthHumanoidDefault`, `KWA_FemaleBrows`, `KWA_FemaleEyesHuman`, `0EyeShadow`, etc. Either an authoring copy-paste or a Bethesda-tools default; either way, partition values do not predict shape role here.
- 5 Hair NIF shapes carry `[32]`. 2 Tail NIF shapes carry `[32]`. 1 Tail shape carries `[37]`.

These are the empirical reason Head / Hair / Tail are exempt from the biped-slot filter — adding them would cull legitimate face accessories.

For primary-head election, all 252 parseable FaceGen NIFs elected exactly one primary head, and none elected outside a `BSFaceGenNiNodeSkinned`-bearing NIF. Four primary-head shapes have names that do not contain "head":

- `DK_Thogra_Face` (Orc follower)
- `0FoamimiHPHMaleHumanCore` (Glenmoril overhaul)
- `MiraiHPHFace`
- `_000SamathaF1Face`

All four carry partition 230 and were elected via the partition-based path. A name-based detection ("shape name contains 'head'") would have missed all four — confirming partition-based detection is more robust than naming heuristics.

**No shape in the corpus has `flags & 1u` (NiAVObject AppCulled / Hidden) set.** All 2,943 flags values end in `…E` (bits 1/2/3 set, bit 0 clear). Whatever mechanism the engine uses to skip placeholder shapes inside child body NIFs, it is not the AppCulled flag — that's why we can't simply respect it the way [NifSkope's `Node::isHidden`](#NifSkope-comparison-shape-filtering) does and rely on Bethesda's NIFs to carry it.

#### NifSkope comparison (shape filtering)

NifSkope's `Node::isHidden()` only checks `flags.node.hidden` (the AppCulled bit) and walks the parent chain. `Mesh::isHidden()` extends that with "no `TexturingProperty` and no `BSShaderLightingProperty`." There is **zero** partition-based or biped-slot-based filtering in NifSkope's GL pipeline — the only references to `BSDismemberSkinInstance` in NifSkope source are in `spells/skeleton.cpp` for skinning operations, not rendering.

Practical consequence: opening `ChildFeet.nif` directly in NifSkope renders the `ChildHead` / `EyesChild` / `MouthChild` placeholders along with the Feet shape, all visible. NifSkope is a single-file editor with no concept of biped slots or ARMA composition — it has no information that would let it decide a shape "doesn't belong" in a given context. This renderer needs the same information NifSkope lacks (the body-part label the host loaded the NIF under), threaded through `BuildFromFile`'s `bipedBodyPart` parameter.

### Shader flag inventory

What [Appendix A](#appendix-a--shader-flag-inventory-table) catalogs by bit. Summary:

- **Flags we honor directly** (the shader/host branches on them): `Specular`, `Greyscale_To_PaletteColor`, `Environment_Mapping`, `Facegen_Detail_Map`, `Model_Space_Normals`, `Eye_Environment_Mapping`, `Hair_Soft_Lighting`, `Own_Emit`, `Double_Sided`, `Vertex_Colors`, `Soft_Lighting`, `Rim_Lighting`.
- **Flags we honor *indirectly* via a different signal that's reliably paired with the flag in actor NIFs**: `Skinned` (we check niflysharp's bone-list directly), `ZBuffer_Write` (we drive `GL_DEPTH_MASK` from `NiAlphaProperty.HasAlphaBlend`).
- **Flags we ignore** because the data we render reliably doesn't depend on them, the feature isn't implemented, or the flag is non-actor-specific. See Appendix A for the per-flag rationale.

The "ignore" bucket is large because most SLSF1/SLSF2 bits exist for engine paths we don't share — landscape rendering, LOD fadeout, parallax/decal/refraction shaders, vehicle texture remapping, fire/water effects, weapon-blood splatter. These never appear on actor body/face/hair shapes and adding code paths for them would be dead weight. The ones worth flagging as "could matter and we don't do them" are `Parallax`, `Anisotropic_Lighting` (hair specular), `Back_Lighting` (skin transmission), and `Glow_Map` (slot 2 emissive modulation) — see Appendix A notes for each.

### Override channels (textures + meshes)

The host can mutate a loaded scene through two **neutral, replace-on-reapply** channels on `VM_CharacterViewer`. Both queue while a load/rebuild is in flight and drain in `ProcessPendingScene` once the scene commits (so a host can fire them right after `LoadAsync` returns without awaiting the GL install).

**`ApplyTextureOverrides(IEnumerable<TextureOverride>)`** — retargets texture slots (0/1/2/7) on existing skin shapes. Each `TextureOverride(bodyPart, slot, gameRelativePath)` routes to `Renderer.Meshes.Where(m => m.BodyPart == bodyPart && m.IsSkinShape)` (Head routes only to the primary head shape). The host maps its own override representation to this via `ParseBodyPart` / `ParseTextureSlot`.

**`ApplyMeshOverrides(IEnumerable<MeshOverride>)`** — *synthesizes* extra renderable shapes from `.nif` files the base NPC doesn't carry. The first consumer is an auxiliary armature on a non-base biped slot (e.g. slot 52, `(BipedObjectFlag)4194304`) that some mods add to the actor **at runtime by script** — it is never in the static `WornArmor` the resolver walks, so its mesh lives only in the selected asset-pack subgroup's `WorldModel.<sex>.File`. The same channel later serves NPC Plugin Chooser 2's "Include Default Outfit" / "Include headgear".

Per override (`MeshOverride`: `Key`, `MeshPath`, `BipedSlots`, `HidesSlots`, `Kind`, `Textures`) the channel:

1. resolves `MeshPath` via `GameAssetResolver` (an auxiliary armature's NIFs are typically dependency-mod assets under `Data\meshes\…`, resolved like any other mesh — not under the config prefix);
2. CPU-skins it to the **current scene's skeleton** (`_cachedMeshPaths.SkeletonPath`) using the same `NifMeshBuilder.BuildFromFile` path as the base meshes — skinning and skin-tint are already generic (bone weights from the NIF, tint from the NIF shader type), so no per-slot logic is needed;
3. applies the bundled `Textures` (else the NIF's own `BSShaderTextureSet`) — a SynthEBD auxiliary subgroup ships mesh + its slot's `SkinTexture.*` together, so the texture set travels *with* the mesh rather than through the texture-only channel;
4. tints by `Kind` default — `Skin` lets the shader decide (an auxiliary skin mesh tints with the body QNAM like any slot-32 skin shape); `Armor`/`Headgear` never take the skin QNAM tint;
5. weight-morphs it — the override NIF is the `_1` (weight-100) variant, so its `_0` companion is loaded and `Positions`/normals are lerped at `t = NpcWeight/100` (the same `BlendWeightMorph` the base body gets). Without this the auxiliary mesh, authored to fit a weight-100 body, floats low/forward on a lower-weight NPC;
6. registers the shape under `Key` (also used as `GlMesh.BodyPart`, so a slot's `SkinTexture.*` routed by `ApplyTextureOverrides` lands on it) with its biped slots.

**Armor skin inheritance.** An armor NIF commonly bakes its own bare-skin shapes — the exposed shoulders/arms/midriff of a sleeveless cuirass, the wrists of gauntlets — as `ShaderType 5` (`ST_SkinTint`) geometry that ships a **placeholder** body diffuse (typically `MaleBody_1.dds`). When applying an `Armor`/`Headgear` override, the channel replaces that placeholder on those skin shapes with the **actor's race skin** — the same TXST the base body already resolved, reused from `_cachedMeshPaths.TxstTextures` (the `Body` set for body/forearm/calf/feet skin, the `Hands` set for hand-slot pieces). The `ShaderType == 5` gate keeps this off the armor's metal/leather shapes; a `SkinTexture` the ArmorAddon supplies itself (`MeshOverride.Textures`, from `NAM0`) takes precedence per slot. Without this, a race with a distinct body texture (e.g. Snow Elf → `MaleBodySnowElf.dds`) shows default-tan arms under armor while its face and bare body stay pale. Implemented in `VM_CharacterViewer.ApplyMeshOverrides`, mirroring the existing per-shape rule that already preserves the QNAM **skin tint** for these same baked-in armor skin shapes (revealing armors like the light Hide cuirass) — this extends that handling from tint to **texture**.

> **Caveat — observed, not documented.** This rule (*an armor's skin partition is textured with the actor's race skin unless the ArmorAddon overrides it via `NAM0`*) is the **observed** in-engine result, not behavior taken from authoritative Bethesda / Creation Kit documentation. It was confirmed empirically with **Knight-Paladin Gelebor** in the Ancient Falmer cuirass: the cuirass ArmorAddon's `NAM0` skin texture is empty and the cuirass NIF references `MaleBody_1.dds`, yet in-game his arms render as pale as his face — so the only possible source for that texture is the `SnowElfRace` skin. This **contradicts a fair amount of online modding "lore"** which states that armor uses its own baked skin texture. There may be tertiary / advanced engine behavior — per-armor skin-texture swap lists (`NAM2`/`NAM3`), skin-tone interactions, race-specific armatures — that we do **not** model here, for lack of concrete documentation. If a future asset contradicts the observed rule, this is the place to revisit.

**Numeric slot routing.** `ParseBodyPart` understands the raw-cast `(BipedObjectFlag)N` form (not just `BipedObjectFlag.Body/Hands/Feet`), mapping each single-bit flag to a routing key — named base parts keep their labels, everything else becomes a generic `"Slot{n}"` (slot 52 → `"Slot52"`). `BipedFlagToBodyPart(int)` is the shared converter so a host builds a `MeshOverride.Key` that matches what the parser routes textures to. No semantic per-slot concept exists anywhere — the slot number is the only key.

**Slot occupancy / hiding.** Every shape carries the biped slots it occupies (`GlMesh.BipedSlots`; base shapes get theirs from their body-part label, override shapes from `MeshOverride.BipedSlots`) plus a `SlotDrawPriority` (0 skin/base, 1 armor, 2 headgear). After each `ApplyMeshOverrides`, `ResolveSlotVisibility` hides any shape a strictly-higher-priority shape's `HidesSlots` covers — body armor (slot 32) hides the base body, headgear (slots 30/31) hides hair. The flag is `GlMesh.HiddenBySlotOccupancy`, kept separate from the missing-texture cull (`IsRendering`); the render passes gate on `ShouldRender = IsRendering && !HiddenBySlotOccupancy`. An auxiliary mesh on a free slot (e.g. slot 52) collides with nothing, so it is never hidden and never hides — but the machinery is in place for NPC2's clothing/headgear features.

**Bone resolution → skip, or warn-but-render.** CPU-skinning resolves each weighted bone's world transform from the skeleton NIF or, failing that, the **mesh's own NIF** — actor body/armor NIFs embed copies of the bones they're weighted to, which is how the base body renders auxiliary-armature weights even on a skeleton that lacks those bones (in the field: a test body skinned 26 bones, 24 from the skeleton + 2 from the body NIF, when the skeleton mod that adds the auxiliary bones was absent; the auxiliary armature's own NIF likewise embeds the full bone chain it is weighted to). `TryApplyCpuSkinning` classifies each weighted bone and surfaces two signals:

- **`BuiltMesh.UnresolvedSkinBones`** — bone present in *neither* source; it keeps a zero transform and would collapse its vertices to the origin. `ApplyMeshOverrides` **skips** any shape that has one — no crash, no bind-pose collapse.
- **`BuiltMesh.BonesAbsentFromSkeleton`** — bone present in the mesh NIF but absent from the skeleton, so it rendered via the mesh-NIF fallback. The shape still renders, but on a frame the base meshes (which DO get those bones from the skeleton) don't share, so it can be **misaligned**. This is exactly how a missing/incompatible skeleton mod manifests (an auxiliary mesh that sits slightly off the body). `ApplyMeshOverrides` renders it but adds a skeleton-compatibility warning — **except for SMP/HDT physics bones**: when the NIF links a physics XML (`NiStringExtraData "HDT Skinned Mesh Physics Object"`, surfaced as `BuiltMesh.PhysicsXmlPaths`), bones that config references (checked against the resolved XML's attribute values) legitimately exist *only* in the mesh NIF — no skeleton ships them, the physics engine animates them at runtime, and the bind-pose fallback render is exactly the authored rest pose — so they are excluded from the warning (`VM_CharacterViewer.FilterPhysicsDrivenBones`). Without this, every SMP-skirted outfit tripped a false "install XPMSSE" warning that NPC2 persisted into the mugshot's missing-asset metadata, re-staling the tile every session. Bones the physics config does not name (e.g. genuine XPMSSE-only bones on a vanilla skeleton) still warn; an unresolvable/unreadable XML leaves all bones warned (conservative).

Both, plus unresolved override NIFs, are surfaced on `VM_CharacterViewer.MeshOverrideWarnings`, which SynthEBD shows as a render-preview warning line (analogous to NPC2's mugshot missing-asset icon) — e.g. "the loaded skeleton is missing bone(s) […]; install the skeleton these meshes require (XPMSSE / XP32 Maximum Skeleton)." The normal load path ignores both signals — its behavior is unchanged.

**Offscreen parity.** The same channel is available to the headless mugshot path: `OffscreenRenderRequest.MeshOverrides` (an `IEnumerable<MeshOverride>?`) is applied after texture overrides and before the framing/render, via the same `VM_CharacterViewer.ApplyMeshOverrides` on the per-render VM — so a saved PNG shows the auxiliary/outfit/headgear shapes (and the slot-hiding) identically to the live preview. The post-apply `MeshOverrideWarnings` are copied into the optional `OffscreenRenderRequest.MeshOverrideWarningsOut` list (parallel to `MissingMeshPathsOut` / `MissingTexturePathsOut`) so the host can flag an incomplete offscreen render the same way it flags one in the preview. Null `MeshOverrides` leaves the offscreen path byte-identical to before.

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

##### Skin-tint operator selector (debug)

The `tint_color` blend has a runtime-selectable operator behind it (`u_skinTintOperator`, surfaced in the render panel). 0 is the production multiply; the rest exist to triangulate which operator the engine actually uses for face-vs-body skin-tone consistency, since multiply produces a visible seam on some replacer NPCs:

| Op | Formula | Notes |
|---|---|---|
| 0 | `albedo * tint` | Production default. |
| 1 | `overlay(albedo, tint)` | Photoshop-style. Brightens 2× where albedo < 0.5 (most skin), darkens above. |
| 2 | `pow(pow(albedo, 2.2) * pow(tint, 2.2), 1/2.2)` | Linear-space multiply — model the tint as happening in linear lighting space. |
| 3 | `pow(albedo, 1/tint)` | Gamma-aware. Reduces dark-region darkening for low tint values. |
| 4 | `mix(albedo, albedo*tint, u_skinTintLerpStrength)` | User-controlled lerp. |
| 5 | `mix(albedo, albedo*tint, skin_tint_alpha)` | Lerp weighted by NIF's per-shape `BSLSP.skinTintAlpha`. Always 0 in vanilla / replacer NIFs we've sampled — effectively reproduces "no tint." Useful as a control point. |
| 6 | `pegtop(albedo, tint) * (1.012, 0.996, 1.012)` | Engine-faithful per Community Shaders' `GetFacegenRGBTintBaseColor`. Pegtop soft-light is `b² + 2·t·b·(1-b)`; the trailing constant is the engine's small per-channel color-shift. |

The Pegtop operator (op 6) is the engine-correct one; the others remain selectable for cross-checking against alternate hypotheses. `u_skinTintApplyToFace` additionally extends the operator to ShaderType==4 face shapes (production: only ShaderType==5 body shapes participate); the `is_face_shape` per-mesh uniform gates the branch so the toggle flips without scene reload. `u_vertexColorMode` is an unrelated debug override on the Stage-1 vertex-color multiply (auto / force-on / force-off) that's tucked into the same render-panel row because it's used during the same diagnostic work.

**Hair-tint isolation.** Hair-tint shapes (BSLSP_HAIRTINT, ShaderType 6) without the greyscale-to-palette flag also flow through the `has_tint_color` branch — the diffuse is multiplied by the hair color from the NPC record. They must NOT participate in the SkinTint operator experiments, though: the body Pegtop path's `(1.012, 0.996, 1.012)` color-shift constant has no business being on hair, and the soft-light / gamma / lerp ops would shift hair color away from the simple engine RGB multiply that's the engine-correct hair behavior. The per-mesh `is_hair_tint` uniform forces `op = 0` (multiply) regardless of the host's selection, isolating the operator experiments to body / face skin tinting.

##### Engine-source cross-check (verified against CS)

Op 6, FaceTint mode 4, and the engine-style detail-map blend are verified byte-for-byte against the Community Shaders source. Three checkpoints, all matching:

- **Tint formula** — `Lighting.hlsl: GetFacegenRGBTintBaseColor` is `K · (b² + 2tb(1-b))` with `K = (1.01171875, 0.99609375, 1.01171875)`. Op 6 implements this identically.

- **Tint source** — `Actor::UpdateSkinColor` reads `npc->bodyTintColor` (the QNAM byte triple at offset 246 on TESNPC), divides each byte by 255, and calls `UpdateBodyTint(NiColor)`, which traverses the scene graph and writes the float color straight into `BSLightingShaderMaterialFacegenTint::tintColor` — the source of the HLSL `TintColor` uniform. The host's QNAM passthrough at `NpcMeshResolver.cs:349-356` does the same byte/255 conversion. Source matches engine byte-for-byte.

- **Color space** — `Color::Diffuse` (in CS's `Color.hlsli`) is identity in non-PBR mode and `linear → gamma` in PBR mode. The PBR branch existing only to deliver gamma values to the rest of the pipeline means the vanilla (non-PBR) pipeline operates on gamma-encoded values throughout — texture samplers do not auto-decode, lighting math runs in gamma space, ACES tonemap + framebuffer-srgb encoding at the end. The renderer matches: `PixelInternalFormat.Rgba8` upload (no auto-decode) with `GL_FRAMEBUFFER_SRGB` at output. Confirmed by inverting this experimentally — uploading diffuses as `PixelInternalFormat.Srgb8Alpha8` for sampler-side auto-decode produced uniform red-shift across all NPCs because the downstream lighting + tonemap stack was tuned for gamma-space inputs. Reverted; gamma-space upload is engine-correct.

#### Detail map (face)

[basic.frag:271-274](Shaders/basic.frag#L271). When SLSF1_Facegen_Detail_Map is set and slot 3 has a texture, `overlayBlend(baseColor, detailSample)` adds detail (skin pores, stubble) on top of the diffuse before the FaceTint pass.

#### FaceTint (face)

[basic.frag:280-289](Shaders/basic.frag#L280). The interesting one. Two blend modes are gated on `is_model_space` (the SLSF1_Model_Space_Normals flag):

- **MSN faces → overlay blend.** Matches NifSkope's `sk_msn.frag`. Vanilla FaceGen always bakes MSN, and so do common replacers (UNP, CBBE, 3BA, BHUNP, HIMBO).
- **Non-MSN faces → multiply blend.** Matches the apparent in-game behavior for tangent-space face replacers like UBE. NifSkope's `sk_default.frag` (the non-MSN counterpart) has no FaceTint code at all; multiply is the closest practical fit that still preserves per-pixel features (freckles, lipstick) which a "no-op" path would lose.

This is the only place the renderer makes a decision based on MSN beyond the normal-map sampling itself. See the [FaceTint commit history](#) for the full empirical derivation; the short version is that overlay's `2·base·tint` branch (taken for `base < 0.5`, which describes most skin diffuses) brightens the face by 2× when the FaceTint texture's mean RGB > 0.5 — which is essentially every FaceTint. For MSN faces this matches the engine; for non-MSN faces the engine doesn't apply FaceTint at all, leaving overlay 2× off.

`FACE_TINT_MODE` const at the top of [basic.frag](Shaders/basic.frag#L70) forces overlay (`1`) or multiply (`2`) for cross-checking.

##### FaceTint runtime mode + Pegtop + multiply-on-empty

The shader-side `FACE_TINT_MODE` const has been promoted to a `u_faceTintMode` uniform so the host can flip operators at runtime to triangulate the engine's actual FaceTint blend without scene reload. Five modes are now available:

| Mode | Behavior |
|---|---|
| 0 | Auto (MSN → overlay, non-MSN → multiply) |
| 1 | Always overlay |
| 2 | Always multiply |
| 3 | Always skip (use for vanilla children whose multiply renders too brown) |
| 4 | Pegtop soft-light (engine-faithful per Community Shaders' `GetFacegenBaseColor`) |

The Pegtop helper (added in the [skin-tint operator selector](#skin-tint-operator-selector-debug) commit) is reused here. Mode 4 is the engine-correct one; the others remain selectable for cross-checking.

**Multiply-on-empty-slot-3 override.** A second toggle (`u_faceTintMultiplyOnEmptyDetail`) forces multiply for face shapes (`ShaderType==4`) whose NIF has `SLSF1_Facegen_Detail_Map` set but whose `BSShaderTextureSet` slot 3 is empty. Empirically resolves the seam on modder faces that omit slot 3 (Brynjolf, Aia Arria, Angeline Morrard from Ordinary People) without affecting NPCs whose slot 3 is populated. The per-mesh `is_face_empty_detail` uniform is set in `ApplyTexturesToGlMesh` based on the NIF flag + slot inspection; the shader-side toggle decides whether to act on it. Useful as a stopgap pending the engine-style detail-map blend, which addresses the same shapes from the detail-map side.

##### Engine-style detail-map blend

The legacy detail-map path (described under [Detail map (face)](#detail-map-face) above) ran *before* the FaceTint blend as a Photoshop overlay. The Skyrim engine's actual handling, per Community Shaders' `Lighting.hlsl` replacement shader (`GetFacegenBaseColor`), is different on two fronts:

- **Order**: detail map is applied AFTER the FaceTint blend, not before.
- **Blend**: it's a multiply against a transformed sample, not an overlay against the raw sample.

The transform is:

```
detailColor = 3.984375 * (vec3(1/255, 0, 1/255) + sampledDetail)
postFaceTint *= detailColor
```

The constant `3.984375` is roughly `4 - 1/64`; the `(1/255, 0, 1/255)` offset prevents the green channel from being lifted off zero (so a black-green-zero detail texel multiplies to mid-gray instead of nuking the surface to black). At a "neutral" detail sample of `(0.25, 0.5, 0.25)` the result is approximately `(1.0, 2.0, 1.0)` — a 2× green boost that the engine then absorbs through subsequent grading. With Bethesda's BlankDetailmap.dds (mid-gray), the multiply lands close to identity; with a populated slot 3 (freckles, dirt, complexion), it brightens.

The `u_detailMapEngineStyle` uniform selects between the legacy overlay path and the engine-style multiply path; both are gated on the same `has_detail_map && u_enableDetail` test, so toggling doesn't change which shapes participate, only how they composite. Pairs with `FaceTintMode==4` (Pegtop) and `SkinTintOperator==6` (Pegtop with engine constant) to reproduce the engine's full face composition pipeline.

**Defaults: Pegtop face/body + engine-style detail.** As of the same commit that flipped these defaults, new VMs ship with `SkinTintOperator = 6`, `FaceTintMode = 4`, and `UseEngineStyleDetailMap = true` — the engine-faithful trio. Validated against vanilla Addvar, Nordic Faces, Aia, Angeline, Bjartur, and UBE Lydia (all match in-game / NifSkope) plus Frodnar (matches the engine's own minor seam). Brynjolf still requires `SkinTintApplyToFace` to be enabled — open question, tracked under follow-up investigation. Hosts that want the legacy multiply behavior can flip the operators back from the render-panel.

##### BlankDetailmap fallback (debug)

A separate experimental host-side toggle (`UseBlankDetailFallback`) substitutes `textures\actors\character\male\BlankDetailmap.dds` when slot 3 is empty on a face shape. The substitution happens in `ApplyTexturesToGlMesh` at scene-build time — the toggle therefore fires `ReloadRequested` rather than just pushing a uniform. Tests whether the engine substitutes a similar default at runtime for slot-3-empty faces (Brynjolf, Aia, Angeline). Mostly redundant with the Pegtop + engine-style detail combination: for those shapes the multiply with BlankDetailmap (mid-gray) lands close to identity, which is also what skipping the detail step gets you. Kept as a control point because it isolates "did the engine substitute a texture?" from "what blend math?" for future investigation.

### Stage 1c: skin saturation boost

[basic.frag:456-467](Shaders/basic.frag#L456). Optional, host-tunable chroma adjustment applied AFTER all Stage-1b tint stages (skin tint, detail, FaceTint) and BEFORE Stage 2 normal calculation. Gated on the per-mesh `is_skin` uniform (true for shader types 4 and 5 — face plus body, hands, feet) and a non-1.0 `u_skinSaturationBoost`:

```glsl
if (is_skin && u_skinSaturationBoost != 1.0) {
    float lum = dot(baseColor.rgb, vec3(0.2126, 0.7152, 0.0722));
    baseColor.rgb = max(mix(vec3(lum), baseColor.rgb, u_skinSaturationBoost), 0.0);
}
```

Same luminance-preserving formulation as `Color::Saturation` in CS's `Color.hlsli`, with a non-negative clamp. Default 1.0 is no-op. Values > 1 boost chroma along the original hue, restoring race-distinguishing skin character that the downstream pipeline tends to compress toward neutral (Imperials look pale, Redguards Mediterranean, Orcs olive at high SSS settings). Values < 1 desaturate skin while leaving hair/eyes untouched.

Applied to the diffuse albedo before lighting, so specular highlights stay achromatic — wet/oily skin highlights consume `specularColor`, not `baseColor`. Hair (BSLSP_HAIRTINT, type 6), eyes (BSLSP_EYE, type 16), and any other non-skin shapes pass through unchanged.

Practical context: at high SSS strength (the host's old default of 2.0, since changed to 0.0), the warm-flesh injection desaturates skin broadly. A boost in the 1.25–1.75 range typically recovers correct appearance for all races simultaneously. After lowering the SSS default the boost remains useful as a per-NPC dial when scene lighting biases skin toward neutral.

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

[basic.frag:607-654](Shaders/basic.frag#L607). Two terms:

- **Forward scatter (wrap-extension delta)**: `max(wrap - lambert, 0)` where `wrap = (NdotL + R) / (1 + R)`, `lambert = max(NdotL, 0)`, and R = `subsurfaceRolloff` (BSLSP's `lightingEffect1` per the Bethesda spec). At R=0 the wrap collapses onto Lambert and the delta is zero everywhere. At R>0 the delta is zero on the fully-lit hemisphere (`wrap == lambert`), positive only at/past the terminator, and peaks at NdotL = 0 with magnitude `R/(1+R)`. This is the warm bleed past the standard terminator that physical SSS produces.
- **Back scatter / transmission**: `pow(max(-NdotL, 0), 3)` — bright where the light is *behind* the surface relative to the viewer. Tightened by the `pow 3` so only thin backlit edges glow (ears, nostrils, lip rims).

The SSS color is `mix(vec3(1.0, 0.35, 0.25), baseColor.rgb, 0.4)` — 60% warm flesh tint, 40% the surface color. The 40% bias toward `baseColor` is so dark-skinned NPCs don't get unrealistically bright-red SSS while still reading as warm.

**Why the forward term is a delta, not the full wrap.** An earlier formulation used `forward = sss_color * wrap` directly, which is large (≈1) on the fully-lit side. That added a fixed-hue warm contribution on top of the standard diffuse, visibly desaturating skin away from its surface tone — Imperials read pale, Redguards Mediterranean, green Orcs olive/yellow. The delta formulation removes that artifact: lit pixels get zero SSS contribution and keep their surface hue exactly, while the terminator and shadow-side contributions (where physical SSS actually does warm light) are preserved. Conceptually this is what pre-integrated SSS via a curvature LUT ([Penner 2011](https://advances.realtimerendering.com/s2011/Penner%20-%20Pre-Integrated%20Skin%20Rendering%20%28Siggraph%202011%20Advances%29.pptx)) produces on average — only the terminator transition gets warmth.

The mask comes from slot 2 (`_sk.dds` for vanilla; the `.r` channel). Vanilla bodies ship with a 4×4 black `_sk.dds` (`mean RGB = (0,0,0)` per the diagnostic dumper), which effectively disables SSS — the engine's same observation made via the empty-mask convention. UBE follows the same convention.

`u_subsurfaceStrength` is a host-side global multiplier (0 disables, 1 is honest source-value SSS, >1 boosts). With the delta formulation the lit side is hue-stable at any strength, so the multiplier can sit at 1.0 by default without skin-tone drift; higher values just deepen the terminator bleed.

**SSS is added separately, not multiplied by `baseColor`** — `sss_color` already mixes in the surface color, so a final multiply would double-tint and look muddy.

#### Eye catchlight

When `is_eye` (BSLSP_EYE shader type, ID 16) and the light is the key light: a tight Blinn-Phong spot at glossiness 256 added *on top of* `baseColor`-multiplied lighting. The "wet eye" sparkle we expect in portraits.

**Why on top of baseColor**: a real eye catchlight is the studio key reflecting off the wet cornea, not tinted by the iris pigment. Multiplying by iris color (`baseColor`) would make the catchlight green on green eyes, etc. We want it to stay white-on-iris.

### Stage 4: environment mapping

[basic.frag:668-697](Shaders/basic.frag#L668). When SLSF1_Environment_Mapping or SLSF1_Eye_Environment_Mapping is set and slot 4 has a texture:

```glsl
viewDirWorld = normalize(u_cameraPos - v_worldPos);
reflectWorld = reflect(-viewDirWorld, normalize(v_worldNormal));

if (is_env_map_2d) {
    // Legacy spherical-2D fallback for mod-shipped panoramic envmaps
    float m = 2.0 * sqrt(rx*rx + ry*ry + (rz+1)*(rz+1));
    envColor = texture(texture_envmap_2d, vec2(rx/m + 0.5, ry/m + 0.5)).rgb;
} else {
    // Engine-faithful cubemap path (vanilla DDS cubemaps)
    envColor = texture(texture_envmap, reflectWorld).rgb;
}

envMask = has_env_mask ? texture(envmask, TexCoords).r : 1.0;
finalColor += envColor * envMask * scale;
```

`scale` is `eyeCubemapScale` for eye shapes, `envMapScale` otherwise.

**Cubemap loading.** Vanilla Skyrim envmaps in slot 4 are DDS cubemaps (`Caps2 & 0xFE00 == 0xFE00` → all six face flags set). Pfim 0.11.4 reads only the first face of a multi-face DDS, so [`CharacterPreviewCache.DecodeDdsCubemap`](Assets/CharacterPreviewCache.cs) parses the 124-byte DDS_HEADER itself, slices the payload into six equal face buffers (DDS spec stores faces in `+X, -X, +Y, -Y, +Z, -Z` order, sequentially after the header), and feeds each face through Pfim as a synthesized "single-face" DDS stream — same header with the cubemap bits cleared from `Caps2`, prefixed to that face's bytes. This decouples the cubemap-detection step from Pfim's lack of multi-face support without requiring a different DDS library.

[`GlTextureManager.LoadEnvMap()`](Gl/GlTextureManager.cs) returns `(handle, isCube)`. If the file is a complete cubemap, `UploadCubemap` issues six `glTexImage2D` calls onto `GL_TEXTURE_CUBE_MAP_POSITIVE_X..NEGATIVE_Z` with `GL_CLAMP_TO_EDGE` on all three axes (necessary for cubemap seam continuity; `GL_REPEAT` would produce visible joins). If the file isn't a cubemap (mod-shipped panoramic 2D), we fall back to the legacy 2D path and the host sets `glMesh.IsEnvMap2D = true` so the shader takes the spherical-UV branch.

**Reflection vector coordinate space.** Computed in **world space** using `u_cameraPos` (per-frame, from `OrbitCamera.GetEyePosition()`) and `v_worldNormal` (vertex/geometry normal, not the per-pixel bumped normal). This matches Community Shaders' `Lighting.hlsl` envmap path — bumped reflections would require transforming the per-pixel `normal_viewSpace` back to world via `inverse(u_view)` and aren't part of the engine-faithful pipeline.

**Why the geometry normal, not the bumped one.** Cubemap reflections track macro shape; the eye is a sphere, the body is a body, regardless of fine surface detail in the normal map. Sampling the cubemap with the bumped normal would make per-texel reflections that the eye reads as noisy texture rather than reflective surface. CS, NifSkope, and Outfit Studio all use the geometry normal for envmap.

**Driver completeness.** Texture unit 6 is `samplerCube`; unit 10 is the 2D fallback `sampler2D`. `GlRenderer` always binds *something* to both units per draw call: a real cubemap or the 1×1 black `_defaultBlackCubemap` to unit 6, and either the 2D envmap or texture 0 to unit 10. This avoids "incomplete texture target" warnings on strict drivers when `has_environment_map` is false. `EnableCap.TextureCubeMapSeamless` is enabled at renderer init for consistent edge filtering across vendors.

### Stage 5: emissive

When SLSF1_Own_Emit is set:

```glsl
finalColor += emissiveColor * emissiveMultiple * baseColor.rgb;
```

The multiply by `baseColor.rgb` matches NifSkope's `color = albedo * (diffuse + emissive) + spec` — emissive is bounded by the surface color, so dark regions of the diffuse (e.g., lash hairs that share a mesh with a glowing iris) don't emit. An earlier version of this stage was pure-additive (`finalColor += emissiveColor * emissiveMultiple`), which produced a saturated-yellow stomp across the entire eye-and-lash mesh on shapes like BB's Serana Replacer that ship with strong vampire-eye emissive values like `(0.89, 0.65, 0) × 1.42`.

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

### Final: framebuffer alpha

The fragment shader writes `FragColor = vec4(finalColor, baseColor.a)` — output alpha is just the surface's own alpha, no special-casing.

Earlier versions of this shader had a luma-driven workaround (`outAlpha = min(baseColor.a, lit_luma)` for any ST_EYE shape) to make UBE's separate wet-eye outer cornea fade where dim, since hard-coded `GL_SRC_ALPHA / GL_ONE_MINUS_SRC_ALPHA` blending would otherwise paint a solid black void over the iris underneath. That workaround was replaced once the renderer started honoring per-mesh `SrcBlend / DstBlend` from `NiAlphaProperty` (see [Part 1 NiAlphaProperty](#nialphaproperty)) — UBE's wet-eye is authored as additive blend (`SRC_ALPHA / ONE`), which produces the engine-correct "black cornea adds nothing, catchlight adds brightness" behaviour natively. No shader logic needed.

#### Off-screen readback forces output alpha to 1.0

`GameWindowOffscreenRenderer.RenderInternalCore` stamps `alpha = 255` over every pixel of the resolved byte buffer between `ReadPixelsRgba` and PNG encode. The shader still writes its honest per-fragment alpha — this is a CPU-side overwrite of the readback bytes before they reach the PNG encoder.

**Observed:** the shader's per-fragment alpha is `texture_alpha × vertex_alpha` (after the multiply in Stage 1). For most shapes that's 1.0. But for shapes where:

- the diffuse texture has `alpha < 1` somewhere (vanilla Khajiit faces stamp alpha < 1 across the entire face; alpha-tested hair cards have alpha < 1 at strand silhouettes; any cutout geometry), or
- the `SLSF1_Vertex_Alpha` flag is set on the BSLSP and vertex colors carry alpha < 1 (Khajiit faces have a low-alpha ring around the head/body seam, alpha dropping as low as 0.325),

…the framebuffer ends up storing partial alpha in those pixels. `glClear` sets alpha=1.0 across the FBO initially, then shader writes overwrite alpha wherever a fragment is drawn.

**Live preview empirically displays opaque** for the same scene. The exact mechanism producing the opaque display hasn't been traced — candidates include GLWpfControl explicitly normalizing alpha during its present step, WPF's `D3DImage` compositor ignoring source alpha at composition time, or the underlying D3D9 surface format not surfacing a usable alpha channel to WPF. The verified empirical outcome is: per-fragment α values from the shader reach the off-screen pipeline's readback bytes but never surface in the on-screen pathway.

**Off-screen mugshot** ends in `glReadPixels` + PNG encode. PNG keeps the alpha channel. PNG viewers — including the WPF Image controls NPC2's gallery uses to render mugshot tiles — honor PNG alpha and composite the image over their panel background. Khajiit faces with α ≈ 0.5 alpha-blend with NPC2's gray gallery panel at 50% mix, producing the "head darker than body" / "skin swallows the illumination" effect that looks like dimmed shading but is actually alpha bleed-through to the UI panel underneath. This was caught by opening the PNG in IrfanView with transparency-aware display: the whole Khajiit face read as a transparency checkerboard, and human reference NPCs showed transparency around the hair silhouettes (a long-known artifact users had been compensating for with a black background in the gallery preview panel).

The fix is a one-loop CPU pass forcing alpha=255 on every pixel. It works because:

- Mugshots are rendered against an opaque background (`glClear` with the requested background color, alpha=1.0). Output alpha carries no useful information in the PNG.
- Anti-aliasing at hair / cutout silhouettes already lives in the RGB channel — the MSAA resolve blends edge-fragment samples that passed alpha-test with samples that didn't (which stay at the cleared background color), producing the correct soft RGB blend without needing the PNG alpha channel for edge softness. Stripping alpha doesn't introduce jaggy silhouettes.
- The pre-stamp alpha bytes are display-pipeline noise downstream of a pipeline whose actual color information lives entirely in RGB.

If transparent-background mugshot exports are ever wanted (e.g. portrait stickers for external use), this should become conditional via a request flag rather than unconditional. Currently it's always-on because no caller wants transparent tiles.

##### Related dead ends

Two earlier attempts went down the gamma rabbit hole because the symptom looked like a brightness asymmetry between the off-screen tile and the live preview. Both were reverted once the alpha-channel write-through was understood:

1. **`Srgb8Alpha8` resolve FBO + `GL_FRAMEBUFFER_SRGB` around the blit.** Produced washed-out mugshots across all races. The gamma encoding was brightening pixels on top of the underlying alpha-blend symptom but not addressing the actual transparency issue.
2. **CPU-side linear → sRGB IEC 61966-2-1 LUT after `glReadPixels`, gated on `EnableToneMapping`.** Same problem at a different layer — the math was correct in isolation, but stacked on top of the alpha-blend display problem produced "skin lifted to washed-out highlights, then alpha-blended against panel gray, producing pale-but-muddy tones."

In both cases the PNG's RGB was always correct; the dimming was happening at display time via UI panel show-through. No gamma adjustment was needed.

#### History: where this bug was hiding

For a long time the only visible symptom was transparency around hair-card silhouettes — fine cutout edges with alpha < 1 from MSAA sample coverage. The NPC2 gallery worked around it with a black background panel that masked the hair transparency. The fact that **all** Khajiit faces stored partial alpha across the *entire* face surface was hidden behind a separate symptom: `MaleHeadKhajiit`'s `NiAlphaProperty.AlphaTest=True, threshold=73` caused the shader's `discard` to drop partial-alpha fragments at the head/body seam (where vertex alpha was low enough to drag `tex_α × vert_α` below 0.286), leaving a salt-and-pepper of *fully opaque cleared-background grey* pixels through the face. The face looked "head darker than body" but had alpha=1.0 in the PNG — gallery panels didn't blend through.

Suppressing the alpha-test discard on `ShaderType==4` face shapes (see [Part 1 NiAlphaProperty](#nialphaproperty), specifically the face-shape suppression rationale) made all face fragments render. Their `tex_α × vert_α` then became the per-pixel framebuffer alpha and surfaced in the PNG. That's when the alpha-strip step was needed — the alpha-test suppression and the alpha-strip are two halves of the same fix.

---

## Part 3 — Editor overlay pass (markers, lines, regions)

Everything in Parts 1–2 renders the *body*. The overlay pass renders the BodyTypeProfile editor's authoring gizmos **on top of** the body: key-vertex marker spheres, measurement lines, and the RegionVolume region visualizations (cap-loop contour, solid surface, wireframe). It runs last in `Render()` ([GlRenderer.cs](Gl/GlRenderer.cs)), after the wireframe overlay, with **depth test disabled** so the gizmos are always visible through the mesh from any angle — that "always on top" property is load-bearing for the region tools (you can see the cut contour even when it's behind the bust).

This pass is consumed by SynthEBD's BodySlide classifier but lives entirely in the rendering tier with **neutral types** (`Vector3` lists, interleaved `float` triangle buffers) — the viewer never references a SynthEBD type. The editor pushes geometry into renderer collections via `VM_CharacterViewer` setter methods; the renderer just draws whatever's in the collections.

### Geometry accessors

The classifier reads deformed mesh geometry back out of the viewer through three accessors on `VM_CharacterViewer`, all returning data in the same **pre-ModelScale local space** as `GlMesh.CpuPositions`:

| Method | Returns | Used for |
|---|---|---|
| `GetShapePositions(shape)` | `Vector3[]` of the current (deformed) vertex positions, freshly allocated | BoundingBox key-vertex resolution; evaluating a baked region against the live preset |
| `GetShapeIndices(shape)` | `int[]` triangle index buffer (flat triplets, shape-local), **not** cloned | walking a shape's triangles to clip a region box |
| `GetZeroedShapePositions(shape, weight)` | `Vector3[]` of the **sliders-0 (undeformed)** body at the given weight | resolving a region's box against the topology-stable reference (see [REGION_VOLUME.md](../SynthEBD/REGION_VOLUME.md)) |

`GetZeroedShapePositions` re-runs the same base-pose lerp + skinning pipeline as `ApplyMorphSet` but applies **no slider deltas** and is **non-destructive** — it does not touch `CpuPositions`, re-upload to the GPU, or fire `BodySlideApplied`, so it's safe to call mid-authoring without flicker or re-entrancy.

### The debug shader + overlay GL state

Most overlay geometry is drawn through one shared VAO/VBO (`_debugVao` / `_debugVbo`, vertex layout = `position(3) + normal(3)` = 6 floats) and one shader pair ([Shaders/debug.vert](Shaders/debug.vert) + [debug.frag](Shaders/debug.frag)). The fragment shader is trivial: a single `u_color` uniform with a `u_shaded` switch — `u_shaded=0` outputs the flat color (lines), `u_shaded=1` applies a two-sided fake-sun lambert so filled/3D geometry (marker spheres, the region solid) reads as shaded shape even at one color.

The one exception is the **slider-morph heatmap** channel, which needs a *per-vertex* color (a `u_color` uniform can't express a gradient), so it has its own VAO/VBO (`_heatmapVao` / `_heatmapVbo`, layout = `position(3) + normal(3) + color(3)` = 9 floats) and shader pair ([Shaders/heatmap.vert](Shaders/heatmap.vert) + [heatmap.frag](Shaders/heatmap.frag)). `heatmap.frag` is **unlit** — it outputs the interpolated per-vertex color directly, because that color *is* the datum (morph magnitude); applying the debug pass's fake-sun shade would falsify the magnitudes. The normal is still carried in the layout for parity (and so a future lit variant needs no re-plumbing) but is unused by the fragment shader.

Shared state for the whole pass: **depth test off**; markers + solid additionally enable **back-face cull** and `u_shaded=1`; lines use `u_shaded=0` and a per-channel `GL.LineWidth`. Buffers are uploaded per-draw with `BufferUsageHint.DynamicDraw` (the overlay changes every frame the user edits, so there's no value in static buffers).

> **Same shader, two looks.** The "solid object visible through the body" effect needed for region Solid mode is *not* transparency — it's the lit debug geometry drawn with depth test off, exactly like the marker spheres. A flat single color reads as a 3D solid because `u_shaded=1` varies brightness with the face normal. This is why the region Solid view required no main-shader changes and no alpha/sort-order work.

### Overlay channels

Each channel is a public collection on `GlRenderer`, cleared+repopulated by a `VM_CharacterViewer` setter. Drawn in this order (later = on top):

| Channel | Type | Color | Drawn by | Pushed by |
|---|---|---|---|---|
| `KeyVertexMarkers` (+ `BoxResolved`, `PreviewKeyVertex`, `PreviewPick`) | `List<Vector3>` spheres | orange / yellow / green / purple | `DrawKeyVertexMarkers` (marker pass) | `SetBoxResolvedMarkers`, `SetPreviewPickMarkers`, … |
| `RegionSolidTriangles` | `List<float>` interleaved pos+normal | magenta (`RegionSolidColor`) | `DrawRegionSolid` (marker pass, lit) | `SetRegionSolid` |
| `MeasurementLines` | `List<MeasurementLineSegment>` | per-segment | `DrawMeasurementLines` @ 4.5px | `SetMeasurementLines` |
| `RegionOverlayLines` | same | cyan | `DrawMeasurementLines` @ 4.5px | `SetRegionOverlay` |
| `RegionWireLines` | same | cyan | `DrawMeasurementLines` @ `RegionWireWidth` (1.25px) | `SetRegionSolid` (wire arg) |
| `RegionCapMarkers` | `List<Vector3>` spheres | cyan | `DrawKeyVertexMarkers` | `SetRegionOverlay` (verts arg) |
| `SliderHeatmapTriangles` | `List<float>` interleaved pos+normal+**color** | per-vertex ramp | `DrawSliderHeatmap` (marker pass, **unlit**, own VAO/shader) | `HighlightSliderMorph` |

Marker spheres are a pre-built 2×-subdivided octahedron (128 tris) scaled by `KeyVertexMarkerRadius × ModelScale` and re-centered per marker. Because each sphere re-uploads ~3 KB, dotting a marker at *every* vertex of a dense cut loop produces an overlapping "beaded cord" and tanks the framerate — which is why the region overlay draws **edges as thin lines**, not a marker per vertex.

### Region overlay (BodySlide classifier)

A RegionVolume region's selected-state visualization has two modes (editor `RegionViewMode`), both built from the baked `ResolvedRegion` evaluated against the current deformed positions (so they track the previewed preset):

- **End-Cap** (default): the cyan cut-loop contour only, pushed to `RegionOverlayLines` (thick) — a clean curve, no per-vertex markers.
- **Solid**: the magenta region surface (patch + caps) as a lit solid in `RegionSolidTriangles`, plus a thin cyan triangle-edge **wireframe** in `RegionWireLines`. Faces = solid, vertices/edges = wireframe.

The geometry is generated by `RegionVolumeEvaluator.BuildSolidSurface` (interleaved pos+normal triangles, flat per-tri normals) and `BuildSolidWireframe` (deduped patch edges). The pending-box wireframe (the AABB you draw/edit) is a separate WPF `Canvas` overlay in [UC_CharacterViewer.xaml.cs](../SynthEBD/Classes_Aux/Views/UC_CharacterViewer.xaml.cs) `UpdateBoxWireframe`, not part of this GL pass. The measurement algorithm behind all of this — zeroed-space authoring, cross-preset tracking, cap modes, rotation — is documented separately in [REGION_VOLUME.md](../SynthEBD/REGION_VOLUME.md).

### Slider-morph heatmap (Label by Sliders annotator)

The OBody **Label by Sliders** rule editor can highlight, on its embedded preview, exactly which body vertices a designated BodySlide slider moves — so a rule author can see what `BellyMuscle` or `Waist` actually controls before writing a threshold against it. It is the one overlay that scales to **thousands** of vertices, so it deliberately reuses the *batched* upload model (one `BufferData` + one `DrawArrays`, like `DrawRegionSolid`) rather than the per-element marker/line paths, which the note above warns tank the framerate at high counts.

- **Data.** A slider's per-vertex morph deltas are already a sparse `Dictionary<ushort, Vector3>` (vertexIndex → offset) inside the loaded `.tri`/OSD context. `BodySlideDeformer.TryGetSliderDeltas(slider, shape, tri, osd)` returns it, reusing the same `.tri`-over-OSD preference and OSD longest-common-prefix name-stripping as `ApplyMorphSet` — **no file re-parse**. The set is direction-agnostic: `Small`/`Big` scale this one geometric morph, so the moved-vertex set is identical at any weight.
- **Geometry + color.** `SliderHeatmapBuilder.Build` (pure, unit-tested) walks the shape's `CpuIndices` against its current deformed `CpuPositions`, keeps vertices whose `|delta|` exceeds `DefaultMinDelta` (~1e-4 mesh units, dropping near-zero authored noise), and emits every triangle with **any** affected corner as interleaved `pos+normal+color`. Color grades `|delta|` against the slider's own max through a jet-style ramp (`Ramp`, blue→cyan→green→yellow→red); unaffected corners of an emitted triangle take the cold end, giving a soft falloff at the patch boundary. `VM_CharacterViewer.HighlightSliderMorph` fans this over every cached body shape into `SliderHeatmapTriangles`; `ClearSliderHighlight` empties the channel.
- **Draw.** `DrawSliderHeatmap` runs at the end of the marker pass (depth still off, back-face cull still on) with its own unlit shader/VAO, so the patch reads as an always-on-top magnitude map. It is rebuilt on every preset/weight/NPC change because `VM_SliderAnnotatorPreviewPanel` re-applies it from the viewer's `BodySlideApplied` event (which also covers queued-morph replays), tracking the current pose.

### Region vertex-edit pick mode

Beyond drawing a box, a region's selection can be hand-curated vertex by vertex (the Option-B edit layer in [REGION_VOLUME.md](../SynthEBD/REGION_VOLUME.md)). This adds **no new GL channel** — it reuses the existing pick plumbing and the **preview-marker channel**:

- **Mode + direction.** `VM_CharacterViewer.IsRegionVertexEditMode` (a third pick mode alongside `IsKeyVertexPickMode` / `IsBoundingBoxPickMode`) and `RegionVertexEditAdditive` (Add vs Remove) are bound to two toolbar checkboxes. While the mode is on, the view's mouse handlers suppress orbit.
- **Pick.** A left-click ray-picks the nearest vertex (`HitTestKeyVertex`); a left-drag rubber-bands the shared `BoxSelectionRect` and bulk-selects every enclosed vertex by re-projecting the mesh (`BuildRegionVertexEditFromScreenRect`, the same projection as `ComputeBoxFromScreenRect`). Either way the affected vertices' **zeroed** positions are looked up (`GetZeroedShapePositions`, so the stored edit is display-space-independent) and fanned out as a `RegionVertexEditPick` through `RegionVertexEdited` / `AnyRegionVertexEdited` to the editor VM.
- **Live selection overlay (recolored wireframe).** Rather than dropping marker spheres, the editor recolors the region's **wireframe** to show the edits. In Solid view mode it pushes per-segment-colored line segments through `SetRegionWireColored` (which fills the `RegionWireLines` channel — already per-segment colored): untouched patch edges stay cyan, the half of each edge nearest a curated **added** vertex is green (a whole edge between two added vertices is fully green), and **removed** vertices — which have no edges, having been carved out of the patch — plus any *isolated* added vertex (no triangle formed yet) get small **node crosses** (red / green, three short axis segments) sized to the local mean edge length. The half-edge tagging comes from `RegionVolumeEvaluator.BuildTaggedWireframe`, which flags each patch-edge endpoint whose welded vertex is a whole-triangle corner at an added mesh index. In End-Cap mode the clean cyan contour is kept and only the node crosses are drawn. The induced patch's contour / Solid surface re-resolve through the normal region-overlay channels on every edit. (The old `PreviewPickMarkers` sphere path is explicitly cleared, so no spheres linger.)

---

## Part 4 — Comparison with NifSkope and Outfit Studio

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

- **Subsurface scattering for skin.** Neither reference applies SSS. Without it, faces look like plastic mannequins under directional lighting. Our wrap term `(NdotL + R) / (1 + R)` is engine-faithful — `R` is the BSLSP `subsurfaceRolloff` field, which the Bethesda docs document as the wrap-lighting parameter and `lightingEffect1` in the binary schema. The forward-scatter contribution we *add* is the **delta over Lambert** (`max(wrap - lambert, 0)`), not the wrap directly, so the lit hemisphere gets zero SSS and only the terminator/shadow side is warm-tinted. The rest of the SSS formulation is our own and was empirically tuned, not derived from observed engine behavior:

  - The warm-flesh `vec3(1.0, 0.35, 0.25)` and the 40% blend toward `baseColor` are hand-picked values that "look right" — Bethesda's engine, per Community Shaders' analytic forward path ([Lighting.hlsl `GetSoftLightMultiplier`](https://github.com/doodlum/skyrim-community-shaders)), reads the per-pixel rim/soft tint from slot 2 (`rimSoftLightingTexture`) rather than a hard-coded constant. Vanilla skin ships an empty 4×4 `_sk.dds`, so on stock content the engine's analytic SSS is effectively zero and CS additionally applies a *screen-space* Burley/SeparableSSS pass (which we can't replicate in a forward renderer without a deferred G-buffer + multi-pass post). For portrait-acceptable skin in our forward pipeline, the analytic warm-tint mix is the practical substitute.
  - The transmission term `pow(max(-NdotL, 0), 3) × sss_color × 0.6` is our own approximation; the engine's transmission likely uses a screen-space gaussian blur of the back-lit translucent contribution rather than a pure analytic falloff.
  - The slot-2 `_sk.dds` mask is read as an SSS strength multiplier (`.r` channel), which matches how the engine documentation describes the texture role, but the resulting contribution we add is hand-tuned, not engine-equivalent.

  Net: visually we land in a portrait-acceptable place (skin doesn't read as plastic, ear/nostril edges glow when backlit) without the lit-side hue shift the original full-wrap formulation produced. It's not a faithful reproduction of the engine's screen-space SSS. If we ever need a closer engine match — e.g., to match an in-game screenshot pixel-for-pixel — replacing the analytic forward+transmission terms with a pre-integrated LUT or a Burley screen-space pass is the next step.

- **Eye catchlight.** Neither NifSkope nor Outfit Studio renders a dedicated catchlight. We do, because portrait photographs read as "alive vs. dead" largely on the wet-eye specular dot. Our env-map path is spherical-2D rather than cubemap (see "Where we differ accidentally" below), so the env-cubemap-driven specular highlight that gives in-game eyes their sparkle isn't faithful here; the catchlight pass compensates.

- **Shadows + SSAO + tone-map.** Portrait-quality finishing. NifSkope and Outfit Studio are utility renderers; we're a portrait viewer. SSAO uses a depth + normal prepass ([Shaders/depth_only.vert](Shaders/depth_only.vert), [Shaders/depth_only.frag](Shaders/depth_only.frag)) that writes view-space normals to an RGB8 G-buffer alongside depth; [ssao.frag](Shaders/ssao.frag) samples those normals directly rather than reconstructing them via `cross(dFdx, dFdy)`. Reconstructed normals are constant per triangle (position varies linearly in screen space inside a triangle, so its derivatives are constant), which makes the hemisphere orient off the *flat geometric face normal* and the triangulation pop on smooth surfaces — collarbone, neck, cheek — at any non-trivial radius. The 4×4 box blur in [ssao_blur.frag](Shaders/ssao_blur.frag) cannot hide that because polygon edges are far larger than its footprint and `pow(occlusion, u_intensity)` amplifies the per-triangle contrast before the blur runs. Sampling the interpolated smooth normal from the G-buffer eliminates the artifact at the source.

### Where we differ accidentally / by omission

- **Albedo doubling on MSN face shapes.** NifSkope's `sk_msn.frag` does `albedo += albedo` (multiply by 2) after the detail+tint overlays. We don't do this — and the absence of a face/body seam (with tonemap on *or* off) is empirical evidence the engine doesn't either. The doubling is gated on `hasDetailMask = isST(ST_FaceTint)`, which is false for body shapes; if the engine ran the same code, MSN faces would render 2× brighter than bodies in-game, which they don't. NifSkope likely inherits this from older fixed-function-era vertex lighting where the per-vertex `diffuse + emissive` varying was pre-scaled to a smaller range and the doubling compensated. NifSkope's single-mesh preview hides the asymmetry — they never composite an MSN face against a body in the same frame, so the doubling never produces a visible seam in their tool. Treat this NifSkope code as a NifSkope-specific quirk, not a missing feature on our side.

- **Wetness, parallax, refraction.** Bethesda's actual face/body shaders support all of these via dedicated BSLSP fields and shader flags. They're rare on actor meshes — almost no skin shapes set the parallax flag — so the omission is practical, not a bug. If this renderer ever needs to handle armor or weapons more accurately, parallax becomes important.

- **Glow maps (slot 2 emissive modulation).** Some emissive shapes use slot 2 as an emissive mask. We use slot 2 only for SSS on skin shapes; for non-skin emissive shapes we apply `Own_Emit` without modulation. Visually incorrect for, e.g., the Daedric armor enchanted glow, irrelevant for actor preview.

- **Skin desaturates at high SSS strength.** With `SubsurfaceStrength` at 0 (the current default), skin tones render correctly across all races — Imperials warm, Redguards distinctly dark, Orcs saturated green. Raising it toward the prior default of 2.0 globally desaturates skin (Imperials pale, Redguards Mediterranean, Orcs olive), washing race-distinguishing character toward neutral. The tint pipeline itself is engine-faithful at all SSS strengths — Pegtop + color-shift, QNAM passthrough, and gamma-space rendering all match the CS source byte-for-byte (per the [engine-source cross-check](#engine-source-cross-check-verified-against-cs)) — so the desaturation almost certainly originates in the SSS shader stage or a lighting-interaction issue rather than the tint pipeline. The [skin saturation boost](#stage-1c-skin-saturation-boost) is the user-facing compensation dial; SSS shader audit is a separate follow-up. Causes eliminated during the investigation that traced this back to SSS: NIF `skinTintColor` (always (1,1,1) per render logs), Pegtop math, the color-shift constant, QNAM source/passthrough, color-space mismatch (sRGB-vs-gamma explicitly tested and falsified).

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
