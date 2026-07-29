# Handoff: `ApplyTextureOverrides` forces specular on for slot-7 overrides

**Status:** found 2026-07-29 during a stale-doc audit in NPC Plugin Chooser 2. Not fixed —
deliberately left for a session that can render an A/B. Latent for NPC2 today (see §4), so there
is no urgency, but it is a real inconsistency and the fix is small.

## 1. The defect

`ViewModels/VM_CharacterViewer.cs`, in `ApplyTextureOverrides`, the slot-7 branch:

```csharp
else if (slot == 7)
{
    mesh.SpecularTexture = TextureManager.LoadTexture(source);
    mesh.HasSpecularMap = true;
    mesh.HasSpecular = true;          // <-- unconditional
    RecordTextureSource(mesh, "Specular", source);
}
```

`HasSpecular` is set from the mere presence of a specular map, ignoring the shape's
`SLSF1_Specular` shader flag.

## 2. Why that is wrong

`SLSF1_Specular` (BSLightingShaderProperty `shaderFlags1` bit 0) gates the **entire** specular term
in the engine. A shape can ship an authored `_s.dds` with the flag clear and render with no
specular at all in game — this is not hypothetical, it is how several vanilla assets are authored
(Dragonbone `MaleArmorBody` under DLC1 Keeper armor; beggar-robe and Falmer-boot body proxies).

This is the same defect as **AUD-3** in NPC2's `docs/OutfitRenderingAudit-2026-07.md`, which was
found and fixed on 2026-07-17 — but only in the main build path. `ApplyTexturesToGlMesh` now reads:

```csharp
// SLSF1_Specular gates the whole specular term in the engine - a shape can ship an authored
// _s.dds with the flag clear (vanilla Keeper armor body, beggar-robe body proxies) and renders
// WITHOUT specular in game (AUD-3). Previously forced true here.
glMesh.HasSpecular = (built.ShaderFlags1 & (1u << 0)) != 0;
```

…in **both** its branches (map present and map absent). `ApplyTextureOverrides` was not visited by
that fix and still carries the pre-AUD-3 behaviour.

## 3. The likely fix

Mirror the main path. The obstacle is that `ApplyTextureOverrides` works on a `GlMesh` that has
already been built, so `built.ShaderFlags1` is not in scope at that point — the flags have to be
readable from the mesh. Check whether `GlMesh` already carries them (it carries
`AlphaFlagsRaw`, added for AUD-6, so the precedent for surfacing raw NIF flags on the mesh
exists); if not, stashing `ShaderFlags1` on `GlMesh` at build time is the smallest change.

Then:

```csharp
mesh.HasSpecularMap = true;
mesh.HasSpecular = (mesh.ShaderFlags1 & (1u << 0)) != 0;
```

**Decide deliberately whether an explicit override should be able to force specular on.** There is
an argument that a host explicitly pushing a slot-7 texture is expressing intent that should beat
the NIF's flag. That argument was NOT made when this code was written — the unconditional `true`
is simply the pre-AUD-3 pattern that survived — so treat "match the engine" as the default and
only diverge on purpose. If a host does need to force it, that wants its own explicit field rather
than a side effect of setting a texture.

## 4. Exposure

- **NPC Plugin Chooser 2: none.** NPC2 never calls `ApplyTextureOverrides` (verified 2026-07-29 by
  grep over `BackEnd/` and `View Models/` — zero references to `ApplyTextureOverrides` or
  `TextureOverride`). It drives the renderer through mesh overrides and TXST slot maps instead.
- **SynthEBD:** this is the path SynthEBD's `FilePathReplacement` flows through, so any SynthEBD
  asset pack that replaces a `_s.dds` on a shape whose `SLSF1_Specular` is clear renders that shape
  with specular the game will not show. Whether any shipped pack does that is unmeasured.

## 5. How to verify a fix

Follow the AUD-3 protocol from NPC2's audit doc: one harness process per build, same
`RenderHarness.json`, `burnInRenders: 1`, then an amplified heat-diff of the PNG pair. The AUD-3
specimen (Azadi, `00081E:ccEDHSSE003.esl`) is the wrong one here — it exercises the *build* path
that is already fixed. A correct specimen needs a slot-7 texture **override** applied on top of a
flag-clear shape, which in practice means driving it from SynthEBD rather than from NPC2.

Expect the change to be invisible on most content and to show as "waxy sheen removed" on exactly
the flag-clear shapes, matching what the AUD-3 diff showed (1.3% of frame, localized to bare skin).
