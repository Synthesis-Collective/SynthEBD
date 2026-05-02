using System;
using System.Collections.Generic;
using System.Text;
using nifly;

namespace CharacterViewer.Rendering;

internal static partial class NifDiagnosticDumper
{
    // Conventional Bethesda texture-set slot meanings. The interpretation
    // depends on shader flags, but these are the names NifSkope uses in its UI.
    private static readonly string[] TextureSlotNames =
    {
        "Diffuse",         // 0
        "Normal",          // 1
        "Glow/SkinTint",   // 2
        "Height/Parallax", // 3
        "Environment",     // 4
        "EnvironmentMask", // 5
        "Tint/Inner",      // 6
        "Backlight/Specular", // 7
        "Slot8",           // 8 (rarely used)
    };

    // SLSF1 / SLSF2 bit-name tables — bit index → enum name.
    // Sourced from NifSkope's glproperty.h:450-517 (SLSF1) and 486-517 (SLSF2).
    private static readonly string?[] SLSF1_Names =
    {
        "Specular",                    // 0
        "Skinned",                     // 1
        "Temp_Refraction",             // 2
        "Vertex_Alpha",                // 3
        "Greyscale_To_PaletteColor",   // 4
        "Greyscale_To_PaletteAlpha",   // 5
        "Use_Falloff",                 // 6
        "Environment_Mapping",         // 7
        "Recieve_Shadows",             // 8
        "Cast_Shadows",                // 9
        "Facegen_Detail_Map",          // 10
        "Parallax",                    // 11
        "Model_Space_Normals",         // 12
        "Non_Projective_Shadows",      // 13
        "Landscape",                   // 14
        "Refraction",                  // 15
        "Fire_Refraction",             // 16
        "Eye_Environment_Mapping",     // 17
        "Hair_Soft_Lighting",          // 18
        "Screendoor_Alpha_Fade",       // 19
        "Localmap_Hide_Secret",        // 20
        "FaceGen_RGB_Tint",            // 21
        "Own_Emit",                    // 22
        "Projected_UV",                // 23
        "Multiple_Textures",           // 24
        "Remappable_Textures",         // 25
        "Decal",                       // 26
        "Dynamic_Decal",               // 27
        "Parallax_Occlusion",          // 28
        "External_Emittance",          // 29
        "Soft_Effect",                 // 30
        "ZBuffer_Test",                // 31
    };

    private static readonly string?[] SLSF2_Names =
    {
        "ZBuffer_Write",                // 0
        "LOD_Landscape",                // 1
        "LOD_Objects",                  // 2
        "No_Fade",                      // 3
        "Double_Sided",                 // 4
        "Vertex_Colors",                // 5
        "Glow_Map",                     // 6
        "Assume_Shadowmask",            // 7
        "Packed_Tangent",               // 8
        "Multi_Index_Snow",             // 9
        "Vertex_Lighting",              // 10
        "Uniform_Scale",                // 11
        "Fit_Slope",                    // 12
        "Billboard",                    // 13
        "No_LOD_Land_Blend",            // 14
        "EnvMap_Light_Fade",            // 15
        "Wireframe",                    // 16
        "Weapon_Blood",                 // 17
        "Hide_On_Local_Map",            // 18
        "Premult_Alpha",                // 19
        "Cloud_LOD",                    // 20
        "Anisotropic_Lighting",         // 21
        "No_Transparency_Multisampling",// 22
        "Unused01",                     // 23
        "Multi_Layer_Parallax",         // 24
        "Soft_Lighting",                // 25
        "Rim_Lighting",                 // 26
        "Back_Lighting",                // 27
        "Unused02",                     // 28
        "Tree_Anim",                    // 29
        "Effect_Lighting",              // 30
        "HD_LOD_Objects",               // 31
    };

    private static string DecodeFlags(uint flags, string?[] names)
    {
        var setNames = new List<string>(8);
        for (int bit = 0; bit < 32; bit++)
        {
            if ((flags & (1u << bit)) != 0)
            {
                string? n = bit < names.Length ? names[bit] : null;
                setNames.Add(n ?? $"Bit{bit}");
            }
        }
        if (setNames.Count == 0) return "(none)";
        return string.Join(" | ", setNames);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Per-block dispatch
    // ════════════════════════════════════════════════════════════════════

    private static void DumpOneBlock(StringBuilder sb, NiHeader header, uint id, HashSet<string> texturePaths)
    {
        string typeName = SafeStr(() => header.GetBlockTypeStringById(id));
        NiObject? blk = null;
        try { blk = header.GetBlockById(id); } catch { }
        if (blk == null)
        {
            sb.AppendLine($"--- Block [{id}] {typeName} ---");
            sb.AppendLine("  <unresolved>");
            return;
        }

        string name = "";
        if (blk is NiObjectNET nn && nn.name != null)
            name = SafeStr(() => nn.name.get());

        sb.Append("--- Block [").Append(id).Append("] ").Append(typeName);
        if (!string.IsNullOrEmpty(name)) sb.Append(" \"").Append(name).Append('"');
        sb.AppendLine(" ---");

        // Shared NiAVObject metadata (transforms, flags). NiNode/NiShape extend it.
        if (blk is NiAVObject av)
        {
            DumpAVObjectCommon(sb, av);
        }

        switch (blk)
        {
            case BSLightingShaderProperty bslsp:
                DumpBSLightingShaderProperty(sb, bslsp);
                break;
            case BSEffectShaderProperty bsesp:
                DumpBSEffectShaderProperty(sb, bsesp, texturePaths);
                break;
            case NiAlphaProperty alpha:
                DumpAlphaProperty(sb, alpha);
                break;
            case BSShaderTextureSet texSet:
                DumpTextureSet(sb, texSet, texturePaths);
                break;
            case NiSkinInstance skin:
                DumpSkinInstance(sb, skin);
                break;
            case NiNode node:
                DumpNiNode(sb, node);
                break;
            case NiShape shape:
                DumpNiShape(sb, header, shape, texturePaths);
                break;
            default:
                // Generic fallback — at least we showed the type.
                break;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  NiAVObject (base for NiNode + NiShape)
    // ────────────────────────────────────────────────────────────────────

    private static void DumpAVObjectCommon(StringBuilder sb, NiAVObject av)
    {
        try
        {
            sb.Append("  flags:           0x").AppendLine(av.flags.ToString("X8"));
        }
        catch { }

        try
        {
            var t = av.transform;
            if (t != null)
            {
                var tr = t.translation;
                if (tr != null)
                    sb.Append("  translation:     ").AppendLine($"({tr.x:F4}, {tr.y:F4}, {tr.z:F4})");

                float yaw = 0, pitch = 0, roll = 0;
                bool ok = false;
                try { ok = t.ToEulerDegrees(ref yaw, ref pitch, ref roll); } catch { }
                if (ok)
                    sb.Append("  rotation (Y/P/R):").AppendLine($"({yaw:F2}°, {pitch:F2}°, {roll:F2}°)");
                else
                    sb.AppendLine("  rotation:        <non-Euler matrix>");

                sb.Append("  scale:           ").AppendLine(t.scale.ToString("F4"));
            }
        }
        catch (Exception ex) { sb.AppendLine($"  <transform read failed: {ex.Message}>"); }

        try
        {
            var collRef = av.collisionRef;
            if (collRef != null && !collRef.IsEmpty())
                sb.Append("  collision:       [").Append(collRef.index).AppendLine("]");
        }
        catch { }
    }

    // ────────────────────────────────────────────────────────────────────
    //  NiNode-specific
    // ────────────────────────────────────────────────────────────────────

    private static void DumpNiNode(StringBuilder sb, NiNode node)
    {
        try
        {
            var children = node.GetChildren();
            if (children != null)
            {
                using var refs = children.GetRefs();
                if (refs != null)
                {
                    sb.Append("  children:        ").Append(refs.Count).Append(" [");
                    for (int i = 0; i < refs.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        try { sb.Append(refs[i].index); } catch { sb.Append('?'); }
                        if (i >= 31) { sb.Append(", …"); break; }
                    }
                    sb.AppendLine("]");
                }
            }
        }
        catch { }

        try
        {
            var effects = node.GetEffects();
            if (effects != null)
            {
                using var refs = effects.GetRefs();
                if (refs != null && refs.Count > 0)
                {
                    sb.Append("  effects:         ").Append(refs.Count).Append(" [");
                    for (int i = 0; i < refs.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        try { sb.Append(refs[i].index); } catch { sb.Append('?'); }
                    }
                    sb.AppendLine("]");
                }
            }
        }
        catch { }
    }

    // ────────────────────────────────────────────────────────────────────
    //  NiShape (BSTriShape / NiTriShape / BSDynamicTriShape / etc.)
    // ────────────────────────────────────────────────────────────────────

    private static void DumpNiShape(StringBuilder sb, NiHeader header, NiShape shape, HashSet<string> texturePaths)
    {
        try
        {
            sb.Append("  vertices:        ").AppendLine(shape.GetNumVertices().ToString());
            sb.Append("  triangles:       ").AppendLine(shape.GetNumTriangles().ToString());
        }
        catch { }

        try
        {
            sb.Append("  hasFlags:        ");
            sb.Append("Vertices=").Append(shape.HasVertices());
            sb.Append(" Normals=").Append(shape.HasNormals());
            sb.Append(" Tangents=").Append(shape.HasTangents());
            sb.Append(" UVs=").Append(shape.HasUVs());
            sb.Append(" VColors=").Append(shape.HasVertexColors());
            sb.Append(" Skinned=").Append(shape.IsSkinned());
            sb.AppendLine();
        }
        catch { }

        try
        {
            using var bounds = shape.GetBounds();
            if (bounds != null)
            {
                var c = bounds.center;
                if (c != null)
                    sb.Append("  bounding sphere: center=(")
                      .AppendFormat("{0:F2},{1:F2},{2:F2}", c.x, c.y, c.z)
                      .Append(") radius=").AppendLine(bounds.radius.ToString("F2"));
            }
        }
        catch { }

        try
        {
            var sref = shape.ShaderPropertyRef();
            if (sref != null && !sref.IsEmpty())
                sb.Append("  shaderProperty:  [").Append(sref.index).Append("] ")
                  .AppendLine(SafeStr(() => header.GetBlockTypeStringById(sref.index)));
            else sb.AppendLine("  shaderProperty:  None");
        }
        catch { }

        try
        {
            var aref = shape.AlphaPropertyRef();
            if (aref != null && !aref.IsEmpty())
                sb.Append("  alphaProperty:   [").Append(aref.index).Append("] ")
                  .AppendLine(SafeStr(() => header.GetBlockTypeStringById(aref.index)));
            else sb.AppendLine("  alphaProperty:   None");
        }
        catch { }

        try
        {
            var skref = shape.SkinInstanceRef();
            if (skref != null && !skref.IsEmpty())
                sb.Append("  skinInstance:    [").Append(skref.index).Append("] ")
                  .AppendLine(SafeStr(() => header.GetBlockTypeStringById(skref.index)));
            else sb.AppendLine("  skinInstance:    None");
        }
        catch { }
    }

    // ────────────────────────────────────────────────────────────────────
    //  BSLightingShaderProperty
    // ────────────────────────────────────────────────────────────────────

    private static void DumpBSLightingShaderProperty(StringBuilder sb, BSLightingShaderProperty p)
    {
        // Read fields defensively — niflysharp throws when accessing fields not
        // valid for the file's version. We catch and continue per field.
        Try(sb, "  shaderType:      ", () => DescribeShaderType(p.GetShaderType()));
        Try(sb, "  shaderFlags1:    ", () => $"0x{p.shaderFlags1:X8}  {DecodeFlags(p.shaderFlags1, SLSF1_Names)}");
        Try(sb, "  shaderFlags2:    ", () => $"0x{p.shaderFlags2:X8}  {DecodeFlags(p.shaderFlags2, SLSF2_Names)}");
        TrySafe(sb, "  uvOffset:        ", () => Vec2(p.uvOffset));
        TrySafe(sb, "  uvScale:         ", () => Vec2(p.uvScale));
        Try(sb, "  textureClampMode:", () => p.textureClampMode.ToString());

        TrySafe(sb, "  textureSet:      ", () =>
        {
            var r = p.textureSetRef;
            return (r == null || r.IsEmpty()) ? "None" : $"[{r.index}]";
        });
        TrySafe(sb, "  rootMaterial:    ", () =>
        {
            var s = p.rootMaterialName;
            return s == null ? "" : (SafeStr(s.get) ?? "");
        });

        Try(sb, "  alpha:           ", () => p.alpha.ToString("F3"));
        Try(sb, "  glossiness:      ", () => p.glossiness.ToString("F2"));
        TrySafe(sb, "  specularColor:   ", () => Vec3(p.specularColor));
        Try(sb, "  specularStrength:", () => p.specularStrength.ToString("F3"));

        Try(sb, "  softlighting:    ", () => p.softlighting.ToString("F3"));
        Try(sb, "  rimlightPower:   ", () => p.rimlightPower.ToString("F3"));
        Try(sb, "  backlightPower:  ", () => p.backlightPower.ToString("F3"));
        Try(sb, "  subsurfaceRoll:  ", () => p.subsurfaceRolloff.ToString("F3"));
        Try(sb, "  greyscale→Pal:   ", () => p.grayscaleToPaletteScale.ToString("F3"));
        Try(sb, "  fresnelPower:    ", () => p.fresnelPower.ToString("F3"));

        TrySafe(sb, "  emissiveColor:   ", () => Vec3(p.emissiveColor));
        Try(sb, "  emissiveMult:    ", () => p.emissiveMultiple.ToString("F3"));

        Try(sb, "  envMapScale:     ", () => p.environmentMapScale.ToString("F3"));
        Try(sb, "  eyeCubemapScale: ", () => p.eyeCubemapScale.ToString("F3"));
        Try(sb, "  refractionStr:   ", () => p.refractionStrength.ToString("F3"));

        TrySafe(sb, "  skinTintColor:   ", () => Vec3(p.skinTintColor));
        Try(sb, "  skinTintAlpha:   ", () => p.skinTintAlpha.ToString("F3"));
        TrySafe(sb, "  hairTintColor:   ", () => Vec3(p.hairTintColor));

        // Parallax (newer-version fields — quietly skipped if accessor throws)
        Try(sb, "  parallaxThick:   ", () => p.parallaxInnerLayerThickness.ToString("F4"));
        Try(sb, "  parallaxRefScale:", () => p.parallaxRefractionScale.ToString("F3"));
        TrySafe(sb, "  parallaxInnerUV: ", () => Vec2(p.parallaxInnerLayerTextureScale));
        Try(sb, "  parallaxEnvStr:  ", () => p.parallaxEnvmapStrength.ToString("F3"));

        // Wetness
        Try(sb, "  wetSpecScale:    ", () => p.wetnessSpecScale.ToString("F3"));
        Try(sb, "  wetSpecPower:    ", () => p.wetnessSpecPower.ToString("F3"));
        Try(sb, "  wetMinVar:       ", () => p.wetnessMinVar.ToString("F3"));
        Try(sb, "  wetEnvmapScale:  ", () => p.wetnessEnvmapScale.ToString("F3"));
        Try(sb, "  wetFresnelPow:   ", () => p.wetnessFresnelPower.ToString("F3"));
        Try(sb, "  wetMetalness:    ", () => p.wetnessMetalness.ToString("F3"));
    }

    private static string DescribeShaderType(uint type)
    {
        if (Enum.IsDefined(typeof(BSLightingShaderPropertyShaderType), (int)type))
            return $"{type} ({(BSLightingShaderPropertyShaderType)type})";
        return type.ToString();
    }

    // ────────────────────────────────────────────────────────────────────
    //  BSEffectShaderProperty
    // ────────────────────────────────────────────────────────────────────

    private static void DumpBSEffectShaderProperty(StringBuilder sb, BSEffectShaderProperty p, HashSet<string> texturePaths)
    {
        Try(sb, "  shaderFlags1:    ", () => $"0x{p.shaderFlags1:X8}  {DecodeFlags(p.shaderFlags1, SLSF1_Names)}");
        Try(sb, "  shaderFlags2:    ", () => $"0x{p.shaderFlags2:X8}  {DecodeFlags(p.shaderFlags2, SLSF2_Names)}");
        TrySafe(sb, "  uvOffset:        ", () => Vec2(p.uvOffset));
        TrySafe(sb, "  uvScale:         ", () => Vec2(p.uvScale));
        Try(sb, "  textureClampMode:", () => p.textureClampMode.ToString());

        EmitNiStringTexture(sb, texturePaths, "sourceTexture",   () => p.sourceTexture);
        EmitNiStringTexture(sb, texturePaths, "greyscaleTex",    () => p.greyscaleTexture);
        EmitNiStringTexture(sb, texturePaths, "envMapTexture",   () => p.envMapTexture);
        EmitNiStringTexture(sb, texturePaths, "normalTexture",   () => p.normalTexture);
        EmitNiStringTexture(sb, texturePaths, "envMaskTexture",  () => p.envMaskTexture);
        EmitNiStringTexture(sb, texturePaths, "lightingTexture", () => p.lightingTexture);
        EmitNiStringTexture(sb, texturePaths, "reflectanceTex",  () => p.reflectanceTexture);
        EmitNiStringTexture(sb, texturePaths, "emitGradient",    () => p.emitGradientTexture);

        Try(sb, "  envMapScale:     ", () => p.envMapScale.ToString("F3"));
        Try(sb, "  falloffStartAng: ", () => p.falloffStartAngle.ToString("F3"));
        Try(sb, "  falloffStopAng:  ", () => p.falloffStopAngle.ToString("F3"));
        Try(sb, "  falloffStartOpc: ", () => p.falloffStartOpacity.ToString("F3"));
        Try(sb, "  falloffStopOpc:  ", () => p.falloffStopOpacity.ToString("F3"));
        Try(sb, "  refractionPower: ", () => p.refractionPower.ToString("F3"));
        TrySafe(sb, "  baseColor:       ", () => Color4ToStr(p.baseColor));
        Try(sb, "  baseColorScale:  ", () => p.baseColorScale.ToString("F3"));
        Try(sb, "  softFalloffDepth:", () => p.softFalloffDepth.ToString("F3"));
        Try(sb, "  lumEmittance:    ", () => p.lumEmittance.ToString("F3"));
    }

    private static void EmitNiStringTexture(StringBuilder sb, HashSet<string> texturePaths, string label, Func<NiString?> getter)
    {
        try
        {
            var s = getter();
            if (s == null) return;
            string val = SafeStr(s.get) ?? "";
            sb.Append("  ").Append(label.PadRight(17)).Append(": \"").Append(val).AppendLine("\"");
            if (!string.IsNullOrWhiteSpace(val)) texturePaths.Add(val);
        }
        catch { }
    }

    // ────────────────────────────────────────────────────────────────────
    //  NiAlphaProperty
    // ────────────────────────────────────────────────────────────────────

    private static void DumpAlphaProperty(StringBuilder sb, NiAlphaProperty a)
    {
        try
        {
            ushort flags = a.flags;
            sb.Append("  flags:           0x").AppendLine(flags.ToString("X4"));

            bool blend       = (flags & 0x0001) != 0;
            int srcBlend     = (flags >> 1) & 0xF;
            int dstBlend     = (flags >> 5) & 0xF;
            bool test        = (flags & 0x0200) != 0;
            int testFunc     = (flags >> 10) & 0x7;
            bool noSorter    = (flags & 0x2000) != 0;

            sb.Append("    AlphaBlend:    ").AppendLine(blend.ToString());
            sb.Append("    SrcBlend:      ").Append(srcBlend).Append(" (").Append(BlendModeName(srcBlend)).AppendLine(")");
            sb.Append("    DstBlend:      ").Append(dstBlend).Append(" (").Append(BlendModeName(dstBlend)).AppendLine(")");
            sb.Append("    AlphaTest:     ").AppendLine(test.ToString());
            sb.Append("    TestFunc:      ").Append(testFunc).Append(" (").Append(TestFuncName(testFunc)).AppendLine(")");
            sb.Append("    NoSorter:      ").AppendLine(noSorter.ToString());
            sb.Append("  threshold:       ").AppendLine(a.threshold.ToString());
        }
        catch (Exception ex) { sb.AppendLine($"  <alpha read failed: {ex.Message}>"); }
    }

    private static string BlendModeName(int v) => v switch
    {
        0  => "ONE",
        1  => "ZERO",
        2  => "SRC_COLOR",
        3  => "INV_SRC_COLOR",
        4  => "DST_COLOR",
        5  => "INV_DST_COLOR",
        6  => "SRC_ALPHA",
        7  => "INV_SRC_ALPHA",
        8  => "DST_ALPHA",
        9  => "INV_DST_ALPHA",
        10 => "SRC_ALPHA_SAT",
        _  => "?",
    };

    private static string TestFuncName(int v) => v switch
    {
        0 => "ALWAYS",
        1 => "LESS",
        2 => "EQUAL",
        3 => "LEQUAL",
        4 => "GREATER",
        5 => "NOTEQUAL",
        6 => "GEQUAL",
        7 => "NEVER",
        _ => "?",
    };

    // ────────────────────────────────────────────────────────────────────
    //  BSShaderTextureSet
    // ────────────────────────────────────────────────────────────────────

    private static void DumpTextureSet(StringBuilder sb, BSShaderTextureSet t, HashSet<string> texturePaths)
    {
        try
        {
            var vec = t.textures;
            if (vec == null) { sb.AppendLine("  textures:        <null>"); return; }
            using var items = vec.items();
            int n = items?.Count ?? 0;
            sb.Append("  textures:        ").Append(n).AppendLine(" slots");
            for (int i = 0; i < n; i++)
            {
                string slotName = i < TextureSlotNames.Length ? TextureSlotNames[i] : $"Slot{i}";
                string path = "";
                try { path = items![i]?.get() ?? ""; } catch { }
                sb.Append("    [").Append(i).Append("] ").Append(slotName.PadRight(18)).Append(" : ");
                if (string.IsNullOrEmpty(path)) sb.AppendLine("(empty)");
                else
                {
                    sb.Append('"').Append(path).AppendLine("\"");
                    texturePaths.Add(path);
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  <texture set read failed: {ex.Message}>"); }
    }

    // ────────────────────────────────────────────────────────────────────
    //  NiSkinInstance / BSDismemberSkinInstance / BSSkinInstance
    // ────────────────────────────────────────────────────────────────────

    private static void DumpSkinInstance(StringBuilder sb, NiSkinInstance s)
    {
        try
        {
            var dr = s.dataRef;
            if (dr != null && !dr.IsEmpty()) sb.Append("  data:            [").Append(dr.index).AppendLine("]");
        }
        catch { }
        try
        {
            var pr = s.skinPartitionRef;
            if (pr != null && !pr.IsEmpty()) sb.Append("  skinPartition:   [").Append(pr.index).AppendLine("]");
        }
        catch { }
        try
        {
            var tr = s.targetRef;
            if (tr != null && !tr.IsEmpty()) sb.Append("  target:          [").Append(tr.index).AppendLine("]");
        }
        catch { }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Field-printing helpers
    // ════════════════════════════════════════════════════════════════════

    private static string Vec2(Vector2? v)
    {
        if (v == null) return "(null)";
        try { return $"({v.u:F4}, {v.v:F4})"; } catch { return "<err>"; }
    }

    private static string Vec3(Vector3? v)
    {
        if (v == null) return "(null)";
        try { return $"({v.x:F4}, {v.y:F4}, {v.z:F4})"; } catch { return "<err>"; }
    }

    private static string Color4ToStr(Color4? c)
    {
        if (c == null) return "(null)";
        try { return $"(R={c.r:F3}, G={c.g:F3}, B={c.b:F3}, A={c.a:F3})"; } catch { return "<err>"; }
    }

    /// <summary>
    /// Reads a string-valued field with a label. Catches accessor exceptions
    /// so unsupported-version fields are silently skipped.
    /// </summary>
    private static void Try(StringBuilder sb, string label, Func<string> reader)
    {
        try { sb.Append(label).AppendLine(reader()); }
        catch { /* field not present in this NIF version; skip */ }
    }

    /// <summary>
    /// Like <see cref="Try"/>, but also tolerates the reader returning null —
    /// useful for fields that wrap nullable nifly objects.
    /// </summary>
    private static void TrySafe(StringBuilder sb, string label, Func<string?> reader)
    {
        try
        {
            string? v = reader();
            if (!string.IsNullOrEmpty(v)) sb.Append(label).AppendLine(v);
        }
        catch { /* skip */ }
    }
}
