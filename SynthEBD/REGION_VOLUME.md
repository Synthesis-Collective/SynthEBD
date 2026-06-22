# RegionVolume measurement

A reference for the **RegionVolume** body measurement in the BodySlide classifier: what it measures, why it's built the way it is, and how the pieces fit together. Companion to the BodySlide-classifier measurement system in [BodyTypeProfile.cs](Settings/Settings_OBody/BodyTypeProfile.cs); the rendering of its authoring overlays is in [CharacterViewer.Rendering/RENDERING_PIPELINE.md](../CharacterViewer.Rendering/RENDERING_PIPELINE.md) (Part 3).

---

## Table of contents

1. [Why it exists](#why-it-exists)
2. [The core idea: resolve once, evaluate per preset](#the-core-idea-resolve-once-evaluate-per-preset)
3. [Zeroed-space authoring](#zeroed-space-authoring)
4. [The resolve algorithm](#the-resolve-algorithm)
5. [Cap modes (what "volume" means)](#cap-modes-what-volume-means)
6. [Box rotation](#box-rotation)
7. [The session cache + fingerprint](#the-session-cache--fingerprint)
8. [Authoring lifecycle (editor)](#authoring-lifecycle-editor)
9. [Vertex-edit layer (curated regions)](#vertex-edit-layer-curated-regions)
10. [Persistence model](#persistence-model)
11. [Code map](#code-map)

---

## Why it exists

The other measurement kinds are 2- or 4-vertex distances/ratios (`PointDistance`, `RatioDistance`, …). They're hostage to a couple of anchor vertices, which makes "fullness" hard to classify: `chest_projection` over-reads a long/narrow chest, `chest_width` over-reads a broad/flat one. The analytically correct discriminator for cup-size-style fullness is **enclosed tissue volume**, integrated over a whole region rather than sampled at two points.

`MeasurementKind.RegionVolume` adds exactly that: a measurement whose value is the volume (NIF units³) of a body region — the breast bump, a thigh segment — bounded by the mesh surface plus a cap that closes it.

---

## The core idea: resolve once, evaluate per preset

A region is defined by an **axis-aligned box** (optionally rotated). The naive implementation — clip the box against each preset's deformed mesh and integrate — fails: the box is fixed in space, but the body moves through it as sliders change, so the same box catches different anatomy on a flat-chested vs a busty preset (and often fails to form a clean closed patch at all).

The design instead splits resolution from evaluation:

1. **Resolve once** (per body, per session): clip the box against the **topology-stable sliders-0 mesh**, extract the surface patch + its boundary loop(s), and bake the result as a set of **barycentric references** into the original triangles (`PatchVertexRef` = three original vertex indices + weights). This is `ResolveRegion` → `ResolvedRegion`.
2. **Evaluate per preset**: for any preset's deformed positions, evaluate each baked ref (`w0·p[a] + w1·p[b] + w2·p[c]`) to get the live patch, then sum signed tetrahedra for the volume. This is `ComputeVolume`.

Because barycentric weights are **affine-invariant**, the baked refs track the same anatomy across every preset and weight, and the volume is a continuous function of vertex positions (no per-preset re-clipping, no "the box stopped forming a patch" failures). The expensive topology work happens once; per-preset cost is just position reads + one tetra sum.

This is the same "resolve a stable definition once, re-evaluate against the live mesh" pattern the BoundingBox key-vertex strategy uses — and the reason only the **box** is persisted, never the resolved indices (see [Persistence model](#persistence-model)).

---

## Zeroed-space authoring

The reference mesh a region resolves against is the **sliders-0 (undeformed) body** — the one preset guaranteed to exist regardless of which presets a user has installed. `VM_CharacterViewer.GetZeroedShapePositions(shape, weight)` produces it non-destructively (base-pose lerp + skinning, no slider deltas; see RENDERING_PIPELINE.md Part 3).

But you *author* against whatever preset is loaded — it's far easier to frame an exaggerated feature on a curvy preset than on the flat zeroed body. So the box is **drawn in deformed space and stored in zeroed space**:

- On confirm, `ConvertBoxByVertexSet(deformedPositions, zeroedPositions, drawnBox)` finds which vertices fall in the drawn box on the deformed mesh, then returns the box that bounds **those same vertices** on the zeroed mesh.
- It preserves the drawn box's **per-face air margins**: a region box is meant to be loose on its "air" faces (clearing the bump) and cutting on the face(s) that slice the body. A naive tight AABB of the captured set would hug all six faces and fragment the patch into many cut loops; carrying each face's drawn gap onto the zeroed tight bound keeps cut faces cutting and air faces loose → one clean cap loop.

The "Show zeroed body" toggle on the pending-box panel lets the author flip the rendered body between the current preset and zeroed while a box is up, to verify the ground-truth patch before confirming. When flipped, the pending box is re-expressed into the shown body's space (the same `ConvertBoxByVertexSet`, in whichever direction) so the wireframe stays on the same anatomy.

---

## The resolve algorithm

`ResolveRegion(zeroedPositions, indices, box, rotation, options)` → `ResolvedRegion`:

1. **Clip** every mesh triangle to the box (Sutherland–Hodgman, six axis-aligned half-spaces), tracking barycentric coords on cut edges so split vertices stay referenced to their parent triangle. For a rotated box, the mesh is first transformed into box-local space so the same axis-aligned clip applies (see [Box rotation](#box-rotation)).
2. **Weld** clipped vertices by quantized position; bake each surviving vertex as a `PatchVertexRef`.
3. **Boundary loops**: build a directed-edge map over the patch; edges used once are boundary edges; chain them into closed loops. **Validate**: single manifold component, every loop on a box face, loop count within the allowed set (1 = chest bump, 2 = limb segment), non-zero area. On any failure the result is `IsValid = false` with a human-readable `Diagnostic` (e.g. "3 boundary loops found, expected 1 — the box is catching the arm").
4. **Cut normal**: auto-detect the axis the loop(s) lie perpendicular to (least-spread box-local axis), mapped back to world. Used by FlatPlane capping.

The batch entry point `ResolveRegions(regions, posLookup, idxLookup)` resolves a list against lookups (typically the viewer's zeroed-positions / indices accessors) and returns a `{name → ResolvedRegion}` map.

---

## Cap modes (what "volume" means)

A region's surface patch is open at the cut; closing it into a solid requires a cap, and how you cap it changes what the volume *means*. `NamedRegion.CapMode` (default `FlatPlane`):

- **FlatPlane** — cap against a flat plane perpendicular to the cut normal, at the loop's mean signed distance along that normal. The lid is flat in every view (a "salami cut"); the volume is the tissue protruding past that plane. This is the right default: the cut ring is planar on the zeroed body but deforms **non-planarly** with the bust, so a centroid-fan lid looks "scooped" (a Pringle/saddle) on a curvy preset, while the flat plane stays clean.
- **AnatomicalFan** — cap by fanning each loop from its own (deformed) centroid. The lid follows the exact deformed ring, so the volume is bounded by that ring; on a non-rigidly inflated bust the lid looks scooped, but it's the literal enclosed solid of the bump-plus-ring.

For a single planar loop the two modes give the same volume (divergence theorem — the volume depends on the boundary); they diverge in lid *shape* and, on a deformed/skewed ring, in volume. The cap mode is part of the measurement fingerprint because it changes the computed value.

---

## Box rotation

`NamedRegion.RotX/Y/Z` (intrinsic Euler degrees, default 0/0/0 = axis-aligned) rotate the box about its own center, so the cut plane can align with a feature that isn't square to the body axes (a tilted chest wall). Implementation: at resolve time the mesh is transformed into the box's local frame (`BoxRotation.Basis()` gives the orthonormal axes; world→local maps each vertex), and the **same axis-aligned clip** runs there. The baked refs are barycentric → frame-independent, so volume, overlay, cap modes, and tracking need no rotation-specific code. The detected cut normal is the chosen box-local axis mapped back to world (so FlatPlane caps perpendicular to the rotated plane).

An identity rotation takes a path byte-identical to the no-rotation overload — existing axis-aligned regions are unaffected.

---

## The session cache + fingerprint

Resolving is expensive, so the editor caches `ResolvedRegion`s for the session, keyed by region name and tagged with `(body-mesh hash, box+rotation+cap fingerprint)` — `VM_BodyTypeProfile.GetOrResolveRegion`. A cache entry is reused while the body topology and the region's resolve inputs are unchanged; it's invalidated (re-resolved) on a box/shape/rotation/cap edit, a rename (entry moved to the new key), a delete, or a viewer rebind.

The measurement **cache fingerprint** (`MeasurementCacheStore.AppendRegion`) hashes the region's identity — shape, box coords, expected cap count, **cap mode**, **rotation**, and the **vertex edits** (the latter three only when non-default/non-empty, so existing axis-aligned/flat box-only regions don't re-fingerprint). The edits are quantized and emitted order-independently (sorted) so reordering the list doesn't shift the hash; the `IndexHint` is excluded (it's a re-validated cache, not identity). It deliberately **excludes** the resolved patch/loops and the defining preset/weight: those are session-derived or informational and don't change the computed value. This mirrors the BoundingBox key-vertex fingerprint (commit `5e9610a7`), which excludes the resolved vertex index for the same reason — including session-volatile data spuriously invalidates the disk cache on every restart.

---

## Authoring lifecycle (editor)

In the BodyTypeProfile editor's **Regions** tab:

- **Create**: enable Pick Box, drag a box on the loaded (deformed) preset, click **Confirm as Region** on the pending-box panel. The deformed box is converted to zeroed space and added as a new region row.
- **Select to edit**: clicking a region row brings its stored box back up in the pending-box editor (transformed onto the current body so the wireframe hugs the visible bump). Adjust the box / rotation / cap, then **Confirm as Region** — this **updates the selected row** (it doesn't add a new one). The cyan cap-loop overlay updates live as you drag the box spinners.
- **Verify**: the "Show zeroed body" flip shows the patch against the undeformed reference; the **Viewer display** toggle switches the region's visualization between **End-Cap** (cyan cut contour) and **Solid** (magenta filled surface + cyan wireframe), both visible through the body.
- **Status**: each row shows a resolve badge — OK (with loop count + live volume), Shape? (no loaded geometry), or Bad box (failed validation, with the diagnostic as tooltip).
- **Reference**: a RegionVolume measurement on the **Measurements** tab points at a region by name (the Region dropdown, enabled for the RegionVolume kind); its Live column shows the volume on the current preset.

---

## Vertex-edit layer (curated regions)

A pure box selects a clean rectangular prism of surface. To carve an **irregular** selection — add a vertex the box missed, or remove one it wrongly caught — a region can carry a **vertex-edit layer** on top of its box (`NamedRegion.VertexEdits`). This is the "Option B" design: keep the box, layer hand-curated add/remove edits on it.

**What an edit is.** Each `RegionVertexEdit` is one vertex's **zeroed-space position** (XYZ) plus an `Additive` flag (true = force into the region, false = force out) and a non-authoritative `IndexHint`. Edits are stored as *positions*, never raw indices, for the same reason the box is — a body-mod reinstall renumbers vertices, but the mesh-local position survives. At resolve time `RegionVolumeEvaluator.MatchVertexEdits` matches each stored position to the nearest current-mesh vertex within an epsilon (the hint is tried first and accepted only if it still sits on the stored position; otherwise a brute-force nearest search runs). Unmatched edits are **logged, never silently dropped**.

> The same position→index idea backs the `KeyVertexStrategy.Coordinate` key vertex (a picked vertex stored as a zeroed-space position and re-matched per session) via the single-point sibling `RegionVolumeEvaluator.MatchNearestVertex` — which drops the epsilon rejection, since a lone anchor should always resolve to its closest counterpart even on a reshaped body variant. See the `KeyVertexStrategy` enum docs in [BodyTypeProfile.cs](Settings/Settings_OBody/BodyTypeProfile.cs).

**Induced-on-edit (the watertightness rule).** A region with **no** edits resolves exactly as before — the smooth analytic box clip, byte-identical. The moment it has **any** edit, the *whole* patch switches to a vertex-granular **induced rule**: a triangle is in the patch iff all three of its original vertices are members of `(box ∪ additive-edits) \ subtractive-edits`. The smooth box clip and vertex-granular growth are deliberately **not** mixed: where a grown whole-triangle's full edge meets a box-clipped sliver's truncated edge, the box-face cut point lands mid-edge on the grown triangle — a T-junction that gives a boundary vertex two outgoing edges and fails the manifold check. Resolving the entire edited patch by whole-triangle membership keeps it watertight (no split vertices → no T-junctions). The trade is a vertex-granular boundary once edited; on a dense body mesh this is barely visible, and it's the natural cost of thinking in individual vertices. The existing weld → boundary-loop → validate → cut-normal → volume tail is reused unchanged, so caps, rotation, and cross-preset tracking are unaffected. Removing an interior vertex drops its incident triangles and **opens a hole** (an extra boundary loop); adding an out-of-box vertex pulls in the triangles all of whose vertices have become members.

**Minimal edit set.** The editor only stores an edit that genuinely deviates from the box: **Add** records a force-in edit only for a vertex the box does *not* already contain; **Remove** records a force-out edit only for one the box *does* contain (`RegionVolumeEvaluator.BoxContainsRotated` tests membership). So a region whose edits exactly match its box keeps an **empty** edit list and stays on the smooth-clip path — only real deviations switch it to the induced path. ("Clear" on the row empties the list and reverts to the box.)

**Migration / back-compat.** Old profiles deserialize with an empty `VertexEdits` list → plain box, unchanged. There's no separate "mode" flag: the box is always the base, edits just append ("bake-on-first-edit" collapses to this since under Option B the box never goes away). The disk-cache fingerprint and the session resolve fingerprint both include the edits (quantized, order-independent, appended only when non-empty), so an edit invalidates the cache but a box-only region fingerprints identically to before the feature.

**Authoring.** Select a region row, enable **Edit Region Verts** in the viewer toolbar, pick the **Add / Remove** direction, then **left-click** a vertex to toggle it or **left-drag** a rectangle to bulk-edit every enclosed vertex. The curated vertices are shown by **recoloring the region wireframe** (in Solid view): added vertices and the half of each incident edge nearest them turn green; removed vertices (and any isolated added vertex) get small red/green node crosses. The cap-loop / Solid overlay and the live volume re-resolve on every edit. Edits are stored at the region's **resolve weight** in **zeroed** space (the sliders-0 mesh lerps with NPC weight, so store-weight and match-weight must agree — the vertex *index* is weight-independent but the position→index re-match is not), and are display-space-independent, so the "Show zeroed body" flip needs no per-edit conversion.

---

## Persistence model

`NamedRegion` (on `BodyTypeProfile.Regions`) stores **only**:

- `Name`, `ShapeName`
- the box: `BoxMin/Max X/Y/Z` (zeroed-space), `RotX/Y/Z`
- `ExpectedCapCount`, `CapMode`
- `VertexEdits` — the curated add/remove layer, each a zeroed-space position + sign + index hint (see [Vertex-edit layer](#vertex-edit-layer-curated-regions)); empty for a plain box.
- `DefiningPresetLabel` / `DefiningWeight` — recordkeeping only (which preset+weight the box was framed against, for re-editing); **not** in the fingerprint.

Everything else — the resolved patch, boundary loops, baked refs, cached volumes — is **recomputed each session** from (box + edits + zeroed mesh). Vertex indices are never persisted (only zeroed positions, re-matched per session), because a body-mod update or reinstall can renumber them; the box and the edit positions, defined in mesh-local 3D space, survive that.

---

## Code map

| Concern | Location |
|---|---|
| Geometry core (clip, weld, loops, volume, caps, rotation, convert, solid/wire builders, vertex-edit induced patch + matcher) | [Classes_Core/Models/RegionVolumeEvaluator.cs](Classes_Core/Models/RegionVolumeEvaluator.cs) |
| Model (`NamedRegion`, `RegionVertexEdit`, `MeasurementKind.RegionVolume`, `MeasurementDefinition.RegionRefName`) | [Settings/Settings_OBody/BodyTypeProfile.cs](Settings/Settings_OBody/BodyTypeProfile.cs) |
| Cache fingerprint (`AppendRegion`) | [Classes_Core/Models/MeasurementCacheStore.cs](Classes_Core/Models/MeasurementCacheStore.cs) |
| Evaluator hook (`TryEvaluateRegionVolume`) | [Classes_Core/Models/BodySlideMeasurementEvaluator.cs](Classes_Core/Models/BodySlideMeasurementEvaluator.cs) |
| Editor VM (resolve cache, overlay, edit lifecycle, scan integration) | [Classes_Core/ViewModels/OBody SubModels/VM_BodyTypeProfileEditor.cs](Classes_Core/ViewModels/OBody%20SubModels/VM_BodyTypeProfileEditor.cs) |
| Zeroed-positions accessor + overlay channels + pending-box flip | [CharacterViewer.Rendering/ViewModels/VM_CharacterViewer.cs](../CharacterViewer.Rendering/ViewModels/VM_CharacterViewer.cs) |
| Overlay rendering | [CharacterViewer.Rendering/Gl/GlRenderer.cs](../CharacterViewer.Rendering/Gl/GlRenderer.cs) — see [RENDERING_PIPELINE.md](../CharacterViewer.Rendering/RENDERING_PIPELINE.md) Part 3 |
| Tests | [SynthEBD.Tests/RegionVolume*.cs](../SynthEBD.Tests/) |
