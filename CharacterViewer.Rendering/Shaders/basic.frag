#version 330 core
out vec4 FragColor;

in vec3 v_viewSpacePos;
in vec2 TexCoords;
in vec4 vertexColor;
in mat3 v_tangentToViewMatrix;
in mat3 v_modelToViewNormalMatrix;
// TEMP DEBUG: world-space normal for DEBUG_VIZ_WORLD_NORMAL branch.
in vec3 v_worldNormal;
// World-space position used for shadow-map projection.
in vec3 v_worldPos;

#define MAX_LIGHTS 5

// TEMP DEBUG: when true, disables every view-dependent lighting term (specular,
// env reflection, hair backlight, skin rim) and the soft-wrap lighting so the
// remaining pure clamped NdotL diffuse term unambiguously shows which side of
// the mesh each directional light illuminates. Flip to false to restore normal
// shading. Dead branches are eliminated by the GLSL compiler since the value is
// a const, so there's no runtime cost.
const bool DEBUG_DIFFUSE_ONLY = false;

// TEMP DEBUG: when true, ignores all lighting and outputs the mesh's world-space
// normal as RGB color (normal * 0.5 + 0.5). Used to diagnose a 90-degree offset
// between the light arrow and the lit face of the mesh. Expected colors if the
// mesh is correctly converted to Y-up world space:
//   chest (faces -Z)          -> yellowish  (0.5, 0.5, 0.0)
//   back  (faces +Z)          -> blue-cyan  (0.5, 0.5, 1.0)
//   head top (faces +Y)       -> bright green (0.5, 1.0, 0.5)
//   feet soles (faces -Y)     -> purple     (0.5, 0.0, 0.5)
//   right side (faces +X)     -> pink-red   (1.0, 0.5, 0.5)
//   left side  (faces -X)     -> teal       (0.0, 0.5, 0.5)
// If the chest appears purple or the feet appear yellow, normals are rotated
// R_X(+90) extra (i.e. still in NIF Z-up space instead of Y-up). Set to false
// to restore normal shading.
const bool DEBUG_VIZ_WORLD_NORMAL = false;

// TEMP DEBUG: when true, outputs the MSN-texture-sampled normal (AFTER the DX
// G-flip and Z-up->Y-up swizzle) transformed back to world space as an RGB
// color. For MSN meshes (is_model_space=true) with a correctly-swizzled MSN
// path, this should show the same colors as DEBUG_VIZ_WORLD_NORMAL does on
// non-MSN meshes (chest=yellow, head top=green, etc.). For non-MSN meshes this
// branch just falls back to the vertex normal. Used to isolate whether the MSN
// swizzle in the fragment shader or the vertex-normal pipeline is the source
// of the 90-degree lighting offset.
const bool DEBUG_VIZ_MSN_NORMAL = false;

// FaceTint blend mode selector.
//
// Background: NifSkope's source ships TWO Skyrim face shaders --
// sk_default.frag (used for non-MSN faces) which has NO FaceTint
// code at all, and sk_msn.frag (used for MSN faces) which applies
// FaceTint as a Photoshop overlay then doubles the albedo. The
// in-game engine almost certainly mirrors this split, since vanilla
// FaceGen always bakes Model_Space_Normals while body/face replacers
// like UBE deliberately ship tangent-space (non-MSN) faces.
//
// Mode 0 (default): auto -- MSN gets overlay, non-MSN gets multiply.
// Mode 1: always overlay.
// Mode 2: always multiply.
// Mode 3: always skip (use for vanilla children whose multiply
//         output looks too brown; opt-in only).
// Mode 4: Pegtop soft-light (engine-faithful per Community Shaders'
//         Lighting.hlsl replacement shader, GetFacegenBaseColor).
//
// Promoted from const to uniform so the host can flip modes at
// runtime to triangulate the engine's actual FaceTint operator
// without re-loading the scene or recompiling the shader. Default
// value applied at the GlRenderer layer.
uniform int u_faceTintMode;

struct Light {
    int type; // 0:disabled, 1:ambient, 2:directional
    vec3 direction; // pre-transformed to view space
    vec3 color;
    float intensity;
};

// --- TEXTURE SAMPLERS ---
uniform sampler2D texture_diffuse;
uniform sampler2D texture_normal;
uniform sampler2D texture_skin;
uniform sampler2D texture_specular;
uniform sampler2D texture_face_tint;
uniform sampler2D texture_detail;
// Cubemap envmap (engine-faithful path) and 2D sphere-map fallback for
// mod-shipped panoramic envmaps. The host binds the matching one per mesh
// and pushes is_env_map_2d to choose which sampler the fragment shader
// reads. Skyrim's vanilla envmaps in slot 4 are DDS cubemaps; CS samples
// them via TextureCube as well (Lighting.hlsl, EmatEnvmap path).
uniform samplerCube texture_envmap;
uniform sampler2D texture_envmap_2d;
uniform sampler2D texture_envmask;
// Glow map (NIF slot 2 on non-skin shaders, SLSF2_Glow_Map). Modulates the
// emissive term per texel -- the map carries the glow pattern, the material's
// emissiveColor/emissiveMultiple carry its color/intensity (sk_default.frag).
uniform sampler2D texture_glow;

// --- MATERIAL FLAGS ---
uniform bool has_normal_map;
uniform bool has_skin_map;
uniform bool has_specular;
uniform bool has_specular_map;
uniform bool has_face_tint_map;
uniform bool has_greyscale_to_palette;
uniform bool has_tint_color;
uniform bool has_emissive;
uniform bool has_glow_map;
uniform bool is_model_space;
uniform bool has_hair_soft_lighting;
uniform bool has_soft_lighting;
uniform bool has_rim_lighting;
uniform bool has_vertex_colors;
uniform bool has_environment_map;
// True when the slot-4 envmap was loaded as a 2D sphere-map fallback rather
// than a real GL_TEXTURE_CUBE_MAP. Vanilla Skyrim envmaps are cubemaps, but
// some mods ship panoramic 2D sphere-maps; the host detects this at load
// time and toggles this flag so we sample texture_envmap_2d with the legacy
// spherical UV math instead of texture_envmap (samplerCube).
uniform bool is_env_map_2d;
uniform bool has_env_mask;
uniform bool has_detail_map;
uniform bool is_eye;
uniform bool is_face_shape;
// True for BSLSP_FACE (4) or BSLSP_SKINTINT (5) shapes -- i.e. the
// "skin shapes" (face + body + hands + feet). Hair / eyes / brows are
// excluded. Gates the host-tunable u_skinSaturationBoost which
// compensates for downstream desaturation that washes Imperials pale,
// Redguards Mediterranean, and Orcs olive. Set per-mesh by the host.
uniform bool is_skin;
uniform float skin_tint_alpha;
uniform bool is_face_empty_detail;
// True for BSLSP_HAIRTINT (ShaderType 6) shapes. These reuse the
// has_tint_color path (the diffuse is multiplied by the hair color
// from the NPC record), but they must NOT participate in the
// SkinTint operator experiments below: the body Pegtop path applies
// a (1.012, 0.996, 1.012) color-shift constant that has no business
// being on hair, and the soft-light/gamma/lerp ops would shift hair
// color away from the simple engine RGB multiply. When set, the
// operator branch forces op=0 (multiply) regardless of the host's
// u_skinTintOperator selection.
uniform bool is_hair_tint;

// --- RENDERER TOGGLES ---
uniform bool use_alpha_test;
uniform bool u_enableToneMapping;
uniform bool u_enableShadows;
uniform mat4 u_lightViewProj;
uniform sampler2DShadow u_shadowMap;
uniform bool u_enableAO;
uniform sampler2D u_ssaoMap;
uniform vec2 u_screenSize;
// Hair AO gap test (unit 11): the SSAO depth prepass excludes alpha-tested
// and alpha-blended geometry, so the AO texel under a hair/beard fragment
// always belongs to the opaque surface behind the strands. These reconstruct
// the view-space gap between this fragment and that surface; AO fades out
// past u_ssaoHairGap (fully gone at 2x the gap) so background structure
// (collar edges, lip lines) cannot ghost through the beard, while contact
// regions (hairline on scalp, beard on chin) keep their AO.
uniform sampler2D u_ssaoDepthTex;
uniform mat4 u_invProjection;
uniform float u_ssaoHairGap;
uniform bool u_enableEyeCatchlight;
// Skin-shading correctness toggles (host checkboxes, read each frame).
// u_specularAchromatic: when true, dielectric (skin) specular is added on
// top of the albedo*light term instead of being multiplied through
// baseColor.rgb -- matches NifSkope sk_default.frag (color = albedo *
// (diffuse + emissive) + spec) and Community Shaders' additive specular.
// Off restores the legacy albedo-tinted specular for A/B comparison.
uniform bool u_specularAchromatic;
// u_skinFaithfulSoftLight: when true, the skin soft-lighting term uses the
// NifSkope / Community Shaders wrap formulation at honest material strength
// (sqrt(rolloff)) so the terminator warmth is visible; off uses the legacy
// terminator-delta + transmission term. Both still scale by
// u_subsurfaceStrength, so set that ~1.0 to see honest strength.
uniform bool u_skinFaithfulSoftLight;
// --- Hair / daylight finishing toggles (host-controlled, read each frame) ---
// These target the documented reasons blonde hair renders darker here than in
// the engine (see the hair notes below).
//
// u_tonemapHairRelief: when true, hair pixels skip the fresnel contour
// darkening and use a gentler exposure pull-down (0.8 vs 0.6) into the ACES
// curve, so the brown hair midtone is not crushed the way the skin-tuned
// finishing chain crushes it. Skin and everything else are untouched.
uniform bool u_tonemapHairRelief;
// u_daylightBoost: when true, directional lights (not ambient) are scaled by
// u_daylightBoostIntensity and warmed slightly, lifting blonde hair toward its
// in-game daylight appearance without the user hand-tuning the Key light.
// Composes additively with whatever preset/manual lights are active.
uniform bool u_daylightBoost;
// Directional-light gain when u_daylightBoost is on. 1.0 = warmth only (no
// brightening); higher brightens. The warm tint is fixed.
uniform float u_daylightBoostIntensity;
uniform float u_subsurfaceStrength;
// Skin-only saturation multiplier applied post-tint, pre-lighting.
// 1.0 = no-op (default). >1 boosts chroma along the original hue
// (luminance-preserving lerp from luma-grey toward source). Compensates
// for downstream desaturation in the lighting + tonemap stack that
// washes race-distinguishing skin character toward neutral. Gated on
// is_skin, so hair/eyes/brows pass through unchanged.
uniform float u_skinSaturationBoost;
uniform float u_vignetteRadius;
uniform float u_vignetteIntensity;
// Tone-map exposure multiplier (interactive). 1.0 = neutral (the legacy
// hardcoded look); >1 brightens, <1 darkens. Scales the linear color
// going into the ACES curve. Gated under u_enableToneMapping like the
// rest of the finishing stage, so the off path stays bit-for-bit legacy.
uniform float u_exposure;
// Skin-tint debug operator (interactive selector). 0 = multiply
// (production default), 1 = overlay, 2 = linear-space multiply,
// 3 = gamma-aware multiply, 4 = lerp(strength), 5 = lerp weighted by
// the NIF's per-shape skin_tint_alpha, 6 = Pegtop soft-light + body
// color-shift constant (engine-faithful per Community Shaders).
// u_skinTintApplyToFace gates whether ShaderType==4 face shapes
// participate (production: false).
uniform bool u_skinTintApplyToFace;
uniform int u_skinTintOperator;
uniform float u_skinTintLerpStrength;

// Debug override for the vertex-color multiply branch.
// 0 = auto (production), 1 = force on, 2 = force off.
uniform int u_vertexColorMode;

// Experimental: when true, face shapes whose NIF has the
// SLSF1_Facegen_Detail_Map flag set but slot 3 is empty in their
// BSShaderTextureSet (signaled by per-mesh is_face_empty_detail) are
// rendered with the FaceTint operator forced to multiply, regardless
// of u_faceTintMode. Empirically resolves the seam on modder faces
// that omit slot 3 (Brynjolf, Aia Arria, Angeline Morrard) without
// affecting NPCs whose slot 3 is populated.
uniform bool u_faceTintMultiplyOnEmptyDetail;

// Experimental: engine-style detail-map handling for face shapes.
// Per Community Shaders' Lighting.hlsl GetFacegenBaseColor, the
// engine treats slot 3 as a multiplicative scaling map applied AFTER
// the FaceTint blend, with a specific transform:
//   detailColor = 3.984375 * ((1/255, 0, 1/255) + sampled)
// When this toggle is on, our renderer skips the existing pre-FaceTint
// overlay step and applies the engine-style multiply post-blend.
uniform bool u_detailMapEngineStyle;

// --- PER-SHAPE TEXTURE VISIBILITY TOGGLES ---
uniform bool u_enableDiffuse;
uniform bool u_enableNormal;
uniform bool u_enableSkin;
uniform bool u_enableSpecular;
uniform bool u_enableFaceTint;
uniform bool u_enableDetail;
uniform bool u_enableEnvMap;
uniform bool u_enableEmissive;
uniform bool u_enableTintColor;

// --- MATERIAL PROPERTIES ---
uniform float alpha_threshold;
uniform float greyscaleToPaletteScale;
uniform vec3 tint_color;
uniform float materialGlossiness;
uniform float materialSpecularStrength;
uniform vec3 specularColor;
uniform float rimlightPower;
uniform float subsurfaceRolloff;
uniform vec3 emissiveColor;
uniform float emissiveMultiple;
// Effective cubemap scale. The host selects between the NIF's envMapScale
// and eyeCubemapScale by SHADER TYPE (only BSLSP_EYE uses the eye scale
// in-engine) -- see GlRenderer. Not keyed on is_eye, which is a broader
// semantic flag (AO opt-out / catchlight) that also covers ENVMAP-typed
// eyeballs.
uniform float envMapScale;

// --- GENERAL UNIFORMS ---
uniform Light lights[MAX_LIGHTS];
uniform vec3 u_backlightColor;
uniform mat4 u_view;
// World-space camera position. Used for the cubemap reflection-vector
// calculation (reflect(-(cameraPos - worldPos), worldNormal)). Pushed once
// per frame from OrbitCamera.GetEyePosition() in GlRenderer.
uniform vec3 u_cameraPos;

// Photoshop-style overlay blend (matches NifSkope / Bethesda engine)
float overlayBlend(float b, float l)
{
    if (b < 0.5)
        return 2.0 * b * l;
    else
        return 1.0 - 2.0 * (1.0 - l) * (1.0 - b);
}

vec3 overlayBlend(vec3 b, vec3 l)
{
    return vec3(overlayBlend(b.r, l.r), overlayBlend(b.g, l.g), overlayBlend(b.b, l.b));
}

// Pegtop soft-light (the formula Skyrim's actual face/body shader uses
// for SkinTint blends, per Community Shaders' Lighting.hlsl
// replacement shader):
//   pegtop(b, t) = b*b + 2*t*b*(1-b)
// At t=0.5 returns b (identity), at t=0 returns b*b (quadratic darken),
// at t=1 returns 1-(1-b)^2 (quadratic brighten). Symmetric and gentle
// vs Photoshop overlay's harsher piecewise behavior.
vec3 pegtopBlend(vec3 b, vec3 t)
{
    return b*b + 2.0 * t * b * (vec3(1.0) - b);
}

// PCF shadow lookup for the key directional light. Returns 1.0 (lit)
// when fully outside the shadow caster, 0.0 (shadowed) when fully
// occluded, with smooth values in between thanks to (a) the GL hardware
// PCF on sampler2DShadow + LINEAR filter giving 4-tap bilinear, and
// (b) a 3x3 manual kernel on top giving 36 effective samples.
//
// shadowCoord is in [-1,1] clip space; we map to [0,1] for the texture
// lookup. Slope-scale bias avoids self-shadowing acne on grazing
// surfaces while keeping contact shadows tight on flat planes.
float sampleShadowPCF(vec3 worldPos, vec3 normal_view, vec3 lightDir_view)
{
    if (!u_enableShadows) return 1.0;

    vec4 lightClip = u_lightViewProj * vec4(worldPos, 1.0);
    vec3 ndc = lightClip.xyz / lightClip.w;
    vec3 uv = ndc * 0.5 + 0.5;

    // Outside the shadow frustum: assume lit. Avoids dark borders where
    // the shadow map's clamp-to-edge would otherwise return 0.
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || uv.z > 1.0) {
        return 1.0;
    }

    // Slope-scale bias: angle between surface normal and light direction
    // controls the bias amount. Grazing angles (NdotL near 0) need more
    // bias to avoid acne; head-on (NdotL near 1) need almost none. The
    // orthographic shadow projection uses a 1200-unit depth range, so
    // an NDC bias of 0.001 corresponds to ~1.2 world units which is
    // about right for face geometry (sub-unit features stay sharp,
    // grazing-angle acne stays at bay).
    float NdotL = max(dot(normal_view, lightDir_view), 0.0);
    float bias = max(0.003 * (1.0 - NdotL), 0.0005);
    uv.z -= bias;

    // 3x3 PCF kernel on top of the hardware bilinear PCF.
    vec2 texelSize = 1.0 / vec2(textureSize(u_shadowMap, 0));
    float sum = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 off = vec2(x, y) * texelSize;
            sum += texture(u_shadowMap, vec3(uv.xy + off, uv.z));
        }
    }
    return sum / 9.0;
}

void main()
{
    // TEMP DEBUG: short-circuit to visualize world-space mesh normals as color.
    if (DEBUG_VIZ_WORLD_NORMAL) {
        vec3 n = normalize(v_worldNormal);
        FragColor = vec4(n * 0.5 + 0.5, 1.0);
        return;
    }

    // TEMP DEBUG: short-circuit to visualize the MSN-sampled normal (post
    // G-flip, post current swizzle) as color, treated as if it were already
    // in Y-up world space (valid since u_model is identity). For MSN meshes
    // this should produce the same colors as DEBUG_VIZ_WORLD_NORMAL IF the
    // current swizzle on line 178 is correct. If colors are rotated 90deg
    // (e.g. chest green instead of yellow), the swizzle is wrong and the
    // MSN texture is stored in Y-up, not NIF Z-up. Non-MSN meshes fall
    // back to the vertex normal so the viewport stays visually coherent.
    if (DEBUG_VIZ_MSN_NORMAL) {
        vec3 n;
        if (is_model_space && has_normal_map && u_enableNormal) {
            vec3 normal_modelSpace = texture(texture_normal, TexCoords).rgb * 2.0 - 1.0;
            // FIXED: Bethesda MSN is stored in Y-up local model space (character
            // faces +Z_local), not NIF Z-up with DX Y-flip. Our viewer uses
            // Y-up world space with character facing -Z_world, so flip Z.
            // No DX G-flip for MSN.
            normal_modelSpace = vec3(normal_modelSpace.x, normal_modelSpace.y, -normal_modelSpace.z);
            n = normalize(normal_modelSpace);
        } else {
            n = normalize(v_worldNormal);
        }
        FragColor = vec4(n * 0.5 + 0.5, 1.0);
        return;
    }

    // --- 1. BASE COLOR & ALPHA TEST ---
    vec4 baseColor;
    if (u_enableDiffuse) {
        baseColor = texture(texture_diffuse, TexCoords);
    } else {
        baseColor = vec4(0.8, 0.8, 0.8, 1.0); // neutral gray fallback
    }

    // Vertex-color multiply with debug override.
    // 0 (auto) -- use the per-shape has_vertex_colors flag.
    // 1 (force on) -- always multiply (visually inert for shapes
    //   without VC data because the host uploads (1,1,1,1) per vertex).
    // 2 (force off) -- never multiply.
    bool applyVertexColors;
    if (u_vertexColorMode == 1) applyVertexColors = true;
    else if (u_vertexColorMode == 2) applyVertexColors = false;
    else applyVertexColors = has_vertex_colors;
    if (applyVertexColors) {
        baseColor.rgb *= vertexColor.rgb;
        baseColor.a *= vertexColor.a;
    }

    if (use_alpha_test && baseColor.a < alpha_threshold) {
        discard;
    }

    // Greyscale-to-palette (hair tinting)
    if (has_greyscale_to_palette && u_enableDiffuse) {
        baseColor.rgb = baseColor.rrr * tint_color * greyscaleToPaletteScale;
    } else if (has_tint_color && u_enableTintColor) {
        // Face shapes (ShaderType 4) only participate when the debug
        // toggle is on. Body shapes (ShaderType 5) and hair-tint shapes
        // always participate. is_face_shape is set by the host alongside
        // tint_color so the toggle can flip without reload.
        bool applyTint = !is_face_shape || u_skinTintApplyToFace;
        if (applyTint) {
            // Hair tint always uses simple multiply: the operator
            // experiments are scoped to body/face skin tinting and the
            // Pegtop body color-shift constant would mis-color hair.
            int op = is_hair_tint ? 0 : u_skinTintOperator;
            if (op == 0) {
                // 0 -- straight multiply (legacy production default)
                baseColor.rgb *= tint_color;
            } else if (op == 1) {
                // 1 -- overlay (Photoshop-style)
                baseColor.rgb = overlayBlend(baseColor.rgb, tint_color);
            } else if (op == 2) {
                // 2 -- linear-space multiply: gamma-decode both, multiply,
                // re-encode. Models tinting performed in linear lighting
                // space rather than directly on sRGB-encoded texels.
                vec3 albedoLin = pow(baseColor.rgb, vec3(2.2));
                vec3 tintLin = pow(tint_color, vec3(2.2));
                baseColor.rgb = pow(albedoLin * tintLin, vec3(1.0 / 2.2));
            } else if (op == 3) {
                // 3 -- gamma-aware: pow(albedo, 1/tint) with safety floor.
                // Reduces dark-region darkening for low tint values.
                vec3 t = max(tint_color, vec3(0.001));
                baseColor.rgb = pow(baseColor.rgb, vec3(1.0) / t);
            } else if (op == 4) {
                // 4 -- lerp by user-controlled strength.
                vec3 tinted = baseColor.rgb * tint_color;
                baseColor.rgb = mix(baseColor.rgb, tinted, u_skinTintLerpStrength);
            } else if (op == 5) {
                // 5 -- lerp weighted by the NIF's per-shape skinTintAlpha.
                // Always 0.0 in vanilla / replacer NIFs we've sampled, so
                // this operator effectively reproduces "no tint." Useful as
                // a control point.
                vec3 tinted = baseColor.rgb * tint_color;
                baseColor.rgb = mix(baseColor.rgb, tinted, skin_tint_alpha);
            } else if (op == 6) {
                // 6 -- Pegtop soft-light (engine-faithful for body shapes,
                // ShaderType 5 / kFaceGenRGBTint). Per Community Shaders'
                // GetFacegenRGBTintBaseColor, the engine multiplies the
                // result by a small color-shift constant; we do the same.
                baseColor.rgb = pegtopBlend(baseColor.rgb, tint_color);
                baseColor.rgb *= vec3(1.01171875, 0.99609375, 1.01171875);
            } else {
                baseColor.rgb *= tint_color; // unknown op => safe fallback
            }
        }
    }

    // Detail map overlay (applied before face tint, matching NifSkope order)
    // Detail map: legacy pre-FaceTint overlay. When the engine-style
    // detail-map toggle is on, this step is skipped and the detail map
    // is applied AFTER the FaceTint blend as a multiply (matching the
    // engine's GetFacegenBaseColor order).
    if (has_detail_map && u_enableDetail && !u_detailMapEngineStyle) {
        vec3 detailSample = texture(texture_detail, TexCoords).rgb;
        baseColor.rgb = overlayBlend(baseColor.rgb, detailSample);
    }

    // Face tint blend (RGB only - alpha is unused; verified mean=255 across
    // both vanilla and UBE FaceTints). The blend choice is gated on the
    // SLSF1_Model_Space_Normals flag, mirroring NifSkope's sk_msn vs
    // sk_default split. See FACE_TINT_MODE const at file top.
    // Face tint blend (RGB only - alpha is unused; verified mean=255 across
    // both vanilla and UBE FaceTints). The blend choice is gated on
    // u_faceTintMode (and may be overridden per-mesh by the
    // multiply-on-empty-slot-3 toggle).
    if (has_face_tint_map && u_enableFaceTint) {
        // Per-mesh override: face shapes with empty slot 3 (and the
        // experimental toggle on) force multiply, ignoring u_faceTintMode.
        bool forceMultiply = u_faceTintMultiplyOnEmptyDetail && is_face_empty_detail;
        bool useOverlay = !forceMultiply
                       && ((u_faceTintMode == 1)
                           || (u_faceTintMode == 0 && is_model_space));
        bool useMultiply = forceMultiply
                        || (u_faceTintMode == 2)
                        || (u_faceTintMode == 0 && !is_model_space);
        bool usePegtop = !forceMultiply && (u_faceTintMode == 4);
        // Mode 3 (always skip) falls through with no blend.
        if (useOverlay) {
            vec3 tintSample = texture(texture_face_tint, TexCoords).rgb;
            baseColor.rgb = overlayBlend(baseColor.rgb, tintSample);
        } else if (useMultiply) {
            vec3 tintSample = texture(texture_face_tint, TexCoords).rgb;
            baseColor.rgb *= tintSample;
        } else if (usePegtop) {
            // Mode 4 -- Pegtop soft-light (engine-faithful per Community
            // Shaders' Lighting.hlsl replacement shader, GetFacegenBaseColor).
            vec3 tintSample = texture(texture_face_tint, TexCoords).rgb;
            baseColor.rgb = pegtopBlend(baseColor.rgb, tintSample);
        }
    }

    // Detail map: engine-style post-FaceTint multiply. Per Community
    // Shaders' GetFacegenBaseColor, the engine transforms the slot-3
    // sample by 4 * ((1/255, 0, 1/255) + sampled) and multiplies onto
    // the post-FaceTint result. Active only when the toggle is on AND
    // the shape has a detail map; otherwise the legacy pre-FaceTint
    // overlay (above) ran instead.
    if (has_detail_map && u_enableDetail && u_detailMapEngineStyle) {
        vec3 detailSample = texture(texture_detail, TexCoords).rgb;
        vec3 detailColor = vec3(3.984375)
            * (vec3(0.00392156886, 0.0, 0.00392156886) + detailSample);
        baseColor.rgb *= detailColor;
    }

    // Skin-only saturation boost (pragmatic compensation for downstream
    // desaturation in the lighting + tonemap stack). Applied AFTER all
    // diffuse-modulation stages (tint, detail, FaceTint) and BEFORE
    // normal/lighting, so it goes into the diffuse albedo. Specular
    // remains achromatic (uses specularColor, not baseColor) which is
    // what skin should look like -- wet/oily highlights stay neutral
    // while skin chroma is restored. Engine-faithful Saturation
    // formulation per Color::Saturation in CS Color.hlsli.
    if (is_skin && u_skinSaturationBoost != 1.0) {
        float lum = dot(baseColor.rgb, vec3(0.2126, 0.7152, 0.0722));
        baseColor.rgb = max(mix(vec3(lum), baseColor.rgb, u_skinSaturationBoost), 0.0);
    }

    // --- 2. NORMAL CALCULATION ---
    vec3 normal_viewSpace;
    bool tbnIsValid = length(v_tangentToViewMatrix[0]) > 0.0;

    if (has_normal_map && u_enableNormal) {
        if (is_model_space) {
            vec3 normal_modelSpace = texture(texture_normal, TexCoords).rgb * 2.0 - 1.0;
            // Bethesda MSN textures are stored in Y-up local model space with
            // the character's forward direction as +Z_local. Our viewer uses
            // Y-up world space with character facing -Z_world, so flip Z to
            // align. Unlike tangent-space normal maps, MSN does NOT use the
            // DirectX G-flip convention because MSN is model-space not
            // tangent-space. Empirical verification: raw texture sample at
            // chest is (0.5, 0.5, 1.0) -> u = (0,0,1); after flipping Z we
            // get (0,0,-1) which correctly faces the camera at Az=180.
            normal_modelSpace = vec3(normal_modelSpace.x, normal_modelSpace.y, -normal_modelSpace.z);
            normal_viewSpace = normalize(v_modelToViewNormalMatrix * normal_modelSpace);
        }
        else if (tbnIsValid) {
            vec3 normal_tangentSpace = texture(texture_normal, TexCoords).rgb * 2.0 - 1.0;
            normal_tangentSpace.g *= -1.0;
            normal_viewSpace = normalize(v_tangentToViewMatrix * normal_tangentSpace);
        }
        else {
            normal_viewSpace = normalize(v_tangentToViewMatrix[2]);
        }
    } else {
        if (tbnIsValid) {
            normal_viewSpace = normalize(v_tangentToViewMatrix[2]);
        } else {
            normal_viewSpace = normalize(v_modelToViewNormalMatrix * vec3(0.0, 0.0, 1.0));
        }
    }

    // --- 3. DYNAMIC LIGHTING ---
    vec3 finalColor = vec3(0.0);

    // Sample the SSAO occlusion factor once per fragment (in [0, 1];
    // 1 = unoccluded, 0 = fully occluded). Used to darken the
    // ambient + diffuse + SSS + indirect-fill terms - but NOT specular,
    // since specular is a direct mirror reflection that real-world
    // surface roughness doesn't AO out the same way diffuse light is
    // occluded by nearby geometry.
    //
    // Eye shapes (is_eye, BSLSP shader type 16) opt OUT of receiving AO:
    // eyeballs sit a tiny ΔZ behind the lash cards in the depth prepass
    // (lashes set HasAlphaBlend+HasAlphaTest both, so they pass the
    // prepass gate at GlRenderer.cs RenderDepthPrepass and write depth
    // wherever their alpha-test passes - see Nif/NifMeshBuilder.cs flag
    // parsing). That depth step is small but sharp, and SSAO amplifies
    // it into a visible horizontal darkening across the eyeball that
    // scales with u_intensity. The eyeball is also a wet glossy sphere
    // that physically wouldn't benefit from diffuse AO anyway, so
    // skipping it here costs nothing visual and eliminates the
    // lash-edge artifact entirely. Lashes still occlude AO on the
    // surrounding face skin, which is the contact-shadow we want to
    // keep (e.g. faint darkening under the upper lid on the cheekbone).
    //
    // Hair/beard (is_hair_tint) DOES receive AO. A beard is thin strand
    // geometry sitting directly in front of the body (collar, neck, jaw); a
    // naive SSAO occludes every strand against the body just behind it,
    // painting the underlying surface's shading and silhouette edges (e.g. a
    // shirt-collar line) onto the strands so the beard reads as translucent.
    // Two defenses against that: ssao.frag's occluder-thickness test discards
    // occluders more than ~u_thickness behind a fragment (with the bilateral
    // ssao_blur keeping body AO out of the strand gaps), and the hair AO gap
    // fade below drops the sampled AO entirely when the prepass surface under
    // this hair fragment is farther behind than u_ssaoHairGap - since hair is
    // excluded from the prepass, that AO belongs to the background surface,
    // not the hair. (Eyes still opt out: their lash-edge depth step is a
    // separate artifact, and a wet glossy sphere gains nothing from diffuse
    // AO anyway.)
    float ao = (u_enableAO && !is_eye)
        ? texture(u_ssaoMap, gl_FragCoord.xy / u_screenSize).r : 1.0;

    // Hair AO gap fade. Hair is absent from the depth prepass, so the AO
    // sampled above belongs to whatever opaque surface is behind this strand.
    // Reconstruct the view-space depth of both this fragment and that surface;
    // when the surface is farther behind than u_ssaoHairGap its AO is
    // background structure (a collar edge, the lip line under a mustache)
    // that must not shade the hair - fade it back to unoccluded. Contact-range
    // surfaces (scalp under a hairline, chin under a beard) keep their AO as a
    // stand-in for the hair's own local occlusion. See GlRenderer.SsaoHairGap.
    if (u_enableAO && !is_eye && is_hair_tint) {
        vec2 aoUv = gl_FragCoord.xy / u_screenSize;
        float sceneWinZ = texture(u_ssaoDepthTex, aoUv).r;
        vec4 sceneClip = vec4(aoUv * 2.0 - 1.0, sceneWinZ * 2.0 - 1.0, 1.0);
        vec4 sceneView = u_invProjection * sceneClip;
        float sceneZ = sceneView.z / sceneView.w;
        vec4 fragClip = vec4(aoUv * 2.0 - 1.0, gl_FragCoord.z * 2.0 - 1.0, 1.0);
        vec4 fragView = u_invProjection * fragClip;
        float fragZ = fragView.z / fragView.w;
        float gap = abs(fragZ - sceneZ);
        float keep = 1.0 - smoothstep(u_ssaoHairGap, 2.0 * u_ssaoHairGap, gap);
        ao = mix(1.0, ao, keep);
    }

    for (int i = 0; i < MAX_LIGHTS; i++) {
        if (lights[i].type == 0) continue;
        vec3 lightColor = lights[i].color * lights[i].intensity;

        if (lights[i].type == 1) {
            // Ambient - the canonical AO target. Crevices that block
            // sky / hemisphere light should darken proportionally to the
            // AO factor.
            finalColor += lightColor * baseColor.rgb * ao;
        }
        else if (lights[i].type == 2) {
            // Directional
            // Daylight boost: a noon-sun gain + slight warm tint on the
            // directional lights only (ambient is left alone), so blonde
            // hair reads blonde without hand-tuning the Key arrow. Off by
            // default -- this scales the lit terms, not the ambient fill.
            if (u_daylightBoost) {
                lightColor *= u_daylightBoostIntensity * vec3(1.0, 0.97, 0.90);
            }
            vec3 lightDir = normalize(lights[i].direction);
            vec3 viewDir = normalize(-v_viewSpacePos);

            // Shadow factor: only the key light (index 1) casts shadows.
            // Fill and rim lights are typically arranged to bounce or
            // wrap, so shadowing them would lose their wraparound feel
            // and create double-darkening in occluded regions.
            float shadow = (i == 1)
                ? sampleShadowPCF(v_worldPos, normal_viewSpace, lightDir)
                : 1.0;

            // Diffuse
            float NdotL = dot(normal_viewSpace, lightDir);
            float diffuseStrength;
            if (DEBUG_DIFFUSE_ONLY) {
                diffuseStrength = max(NdotL, 0.0);
            } else if (has_soft_lighting) {
                diffuseStrength = NdotL * 0.5 + 0.5; // wrap lighting
            } else {
                diffuseStrength = max(NdotL, 0.0);
            }
            vec3 diffuse = diffuseStrength * lightColor * shadow;

            // Specular (Blinn-Phong)
            vec3 specular = vec3(0.0);
            if (!DEBUG_DIFFUSE_ONLY && has_specular && u_enableSpecular) {
                // Per-pixel specular mask, engine-faithful. Skin shapes carry a
                // dedicated slot-7 _s.dds mask; every other shape (clothes,
                // armor, hair, eyes) stores the mask in the normal map's ALPHA
                // channel (NifSkope sk_default.frag uses normalMap.a; sk_msn.frag
                // falls back to it when no slot-7 map; Community Shaders'
                // Lighting.hlsl samples normal.w on the non-MSN path). Fabric is
                // painted dark there and metal bright, so without this fallback
                // slot-7-less garments rendered at mask=1.0 and cloth read as
                // glossy plastic.
                float specMask = 1.0;
                if (has_specular_map) {
                    specMask = texture(texture_specular, TexCoords).r;
                } else if (has_normal_map && u_enableNormal) {
                    specMask = texture(texture_normal, TexCoords).a;
                }
                vec3 halfwayDir = normalize(lightDir + viewDir);
                float specAmount = pow(max(dot(normal_viewSpace, halfwayDir), 0.0), materialGlossiness);
                specular = specAmount * specMask * lightColor * specularColor * materialSpecularStrength * shadow;
            }

            // Backlight / rimlight (hair)
            vec3 backlight = vec3(0.0);
            if (!DEBUG_DIFFUSE_ONLY && has_hair_soft_lighting) {
                float rim = pow(1.0 - max(dot(viewDir, normal_viewSpace), 0.0), rimlightPower);
                backlight = rim * u_backlightColor * lightColor * diffuseStrength * baseColor.rgb;
            }

            // Rim lighting (non-hair, e.g. skin translucency)
            vec3 rimlight = vec3(0.0);
            if (!DEBUG_DIFFUSE_ONLY && has_rim_lighting) {
                float rim = pow(1.0 - max(dot(viewDir, normal_viewSpace), 0.0), rimlightPower);
                rimlight = rim * lightColor * baseColor.rgb;
            }

            // Subsurface scattering (skin). Two terms: forward scatter
            // (light diffusing through the top layer of skin on the lit
            // side, using subsurfaceRolloff as the proper Bethesda-style
            // wrap parameter) plus back scatter / translucency (light
            // passing through thin areas - ears, nostril rims, lip
            // edges - and emerging on the shadow side). The combined
            // result is added to finalColor OUTSIDE the *baseColor.rgb
            // multiply so the warm-flesh hue isn't double-tinted away;
            // sss_color carries its own already-skin-tinted color.
            //
            // u_subsurfaceStrength is a global multiplier letting users
            // dial the SSS up toward the more pronounced look in
            // professional portrait reference. 0 disables; 1 is honest
            // source-value SSS; >1 boosts. At 0 the corrected pipeline
            // produces zero contribution, matching the pre-2.5.14 look
            // when the host has SubsurfaceStrength set to 0.
            vec3 subsurface = vec3(0.0);
            if (has_skin_map && u_enableSkin && u_subsurfaceStrength > 0.0) {
                float sss_mask = texture(texture_skin, TexCoords).r;

                // Warm flesh tint, biased toward surface color. Only
                // applied to the terminator/back-scatter delta below,
                // never to the fully-lit hemisphere -- so non-warm
                // skin (orcs, dark-skinned NPCs) keep their hue.
                vec3 sss_color = mix(vec3(1.0, 0.35, 0.25), baseColor.rgb, 0.4);

                // Subsurface wrap term. Both branches start from N.L; the
                // subsurfaceRolloff is the Bethesda "Subsurface Rolloff"
                // wrap parameter.
                float NdotL = dot(normal_viewSpace, lightDir);
                if (u_skinFaithfulSoftLight) {
                    // Game-faithful soft-lighting, per NifSkope
                    // sk_default.frag and Community Shaders'
                    // GetSoftLightMultiplier: a wrapped half-lambert
                    // weighted toward the terminator by smoothstep and
                    // driven at honest material strength sqrt(rolloff), so
                    // the warm terminator band is visible instead of
                    // washing out. We reuse the plumbed subsurfaceRolloff
                    // (~0.3) as a proxy for the material soft-lighting value
                    // (~0.4) -- a minor, intentional deviation. sss_color
                    // keeps our warm-flesh bias (a deliberate deviation
                    // from NifSkope's raw mask color, since our mask is a
                    // single channel).
                    float e1 = clamp(subsurfaceRolloff, 0.0, 1.0);
                    float wrap = (NdotL + e1) / (1.0 + e1);
                    // smoothstep(0,1,1-NdotL) is the spec-valid equivalent of
                    // NifSkope's smoothstep(1,0,NdotL): full at the
                    // terminator/backlit side, fading to 0 where fully lit.
                    float soft = max(wrap, 0.0)
                               * smoothstep(0.0, 1.0, 1.0 - NdotL)
                               * sqrt(e1);
                    subsurface = soft * sss_mask * sss_color * lightColor
                               * u_subsurfaceStrength;
                } else {
                    // Legacy terminator-delta forward scatter + cubic
                    // back-scatter transmission. Kept for A/B comparison.
                    float R = clamp(subsurfaceRolloff, 0.001, 1.0);
                    float wrap = max((NdotL + R) / (1.0 + R), 0.0);
                    float lambert = max(NdotL, 0.0);
                    float fwd_amount = max(wrap - lambert, 0.0);
                    float backlit = pow(max(-NdotL, 0.0), 3.0);
                    vec3 forward = sss_color * fwd_amount;
                    vec3 transmission = sss_color * backlit * 0.6;
                    subsurface = lightColor * sss_mask * (forward + transmission)
                               * u_subsurfaceStrength;
                }
            }

            // AO modulates the diffuse + indirect-fill terms but not
            // specular (real specular doesn't get occluded by nearby
            // crevices the way diffuse light does).
            if (u_specularAchromatic && !is_hair_tint) {
                // Game-faithful dielectric specular: skin's highlight is a
                // near-white surface reflection, not tinted by albedo.
                // NifSkope sk_default.frag: color = albedo*(diffuse+emissive)
                // + spec. Community Shaders accumulates specular additively.
                // So tint only the diffuse/indirect terms by albedo and add
                // the already-light-colored specular on top.
                //
                // Hair (BSLSP_HAIRTINT, is_hair_tint) is EXCLUDED and falls
                // through to the legacy albedo-multiplied branch below. Vanilla
                // hair carries a broad low-exponent specular lobe (glossiness
                // ~10) with a white specularColor and NO specular map, so
                // specMask stays 1.0 across the whole shape. Added achromatically
                // on top, summed over every directional light, that lobe blows
                // the hair out to a luminescent halo (e.g. vanilla Aela), and on
                // hair with a real lobe (e.g. Bijin) it reads metallic.
                // Multiplying it through the dark hair albedo (legacy path)
                // keeps it a dim fiber sheen -- the engine-faithful look. Blonde
                // brightness is instead recovered by the daylight / bloom /
                // tonemap-relief finishing toggles, not by hair specular.
                finalColor += (diffuse + backlight + rimlight) * ao * baseColor.rgb;
                finalColor += specular;
            } else {
                // Legacy: specular multiplied through baseColor.rgb, which
                // dims and skin-tints the highlight. Kept for A/B.
                finalColor += ((diffuse + backlight + rimlight) * ao + specular) * baseColor.rgb;
            }
            // SSS is added separately so its warm-flesh tint isn't
            // double-multiplied by the surface color - sss_color already
            // mixes baseColor in at the right ratio. AO still modulates
            // the SSS contribution since deep crevices block scattering.
            finalColor += subsurface * ao;

            // Eye catch-light. Tight high-glossiness Blinn-Phong spot
            // from the key light only (i==1), only for eye shapes.
            // Applied AFTER the baseColor multiply so the bright dot
            // stays white-on-iris regardless of eye color (same way
            // a real catch-light is the studio key reflecting off the
            // wet eye surface, not tinted by the iris pigment).
            if (is_eye && u_enableEyeCatchlight && i == 1 && !DEBUG_DIFFUSE_ONLY) {
                vec3 halfwayDir = normalize(lightDir + viewDir);
                float catchSpec = pow(max(dot(normal_viewSpace, halfwayDir), 0.0), 256.0);
                // Bright but bounded - 1.5 * lightColor with the tight
                // exponent gives a small, intense reflection without
                // blowing out the rest of the eye.
                finalColor += catchSpec * lightColor * 1.5;
            }
        }
    }

    // --- 4. ENVIRONMENT MAPPING (cubemap, with 2D sphere-map fallback) ---
    //
    // Vanilla Skyrim envmaps (slot 4) are DDS cubemaps; CS samples them via
    // TextureCube on the world-space reflection vector. Some mods ship 2D
    // sphere-maps instead -- the host loader detects this at decode time and
    // sets is_env_map_2d so this branch falls back to the legacy spherical
    // UV math.
    //
    // Reflection is computed in world space against the per-pixel BUMPED
    // normal, not the flat vertex/geometry normal. This is what produces the
    // granular "field of sequins" sparkle on env-mapped garments (shaderType 1
    // BSLSP_ENVMAP, e.g. glitter/metallic dresses): each sequin facet in the
    // normal map reflects a different direction of the cubemap. Reflecting off
    // the macro geometry normal instead makes the whole surface mirror one
    // smooth blob of the cubemap that slides across as the model rotates -- the
    // "oily" look. normal_viewSpace already holds the correctly bump-mapped
    // normal (MSN and tangent-space paths both feed it), so we rotate it back
    // to world space. The view matrix's rotation is orthonormal, so its inverse
    // is its transpose; translation lives in the 4th column and drops out of
    // mat3(). The env MASK (slot 5) still only modulates reflection intensity;
    // the per-sequin sparkle comes from this bumped reflection direction.
    if (!DEBUG_DIFFUSE_ONLY && has_environment_map && u_enableEnvMap) {
        vec3 viewDirWorld = normalize(u_cameraPos - v_worldPos);
        vec3 nWorld       = normalize(transpose(mat3(u_view)) * normal_viewSpace);
        vec3 reflectWorld = reflect(-viewDirWorld, nWorld);
        vec3 envColor;
        if (is_env_map_2d) {
            float m = 2.0 * sqrt(reflectWorld.x * reflectWorld.x
                              +  reflectWorld.y * reflectWorld.y
                              + (reflectWorld.z + 1.0) * (reflectWorld.z + 1.0));
            vec2 envUV = vec2(reflectWorld.x / m + 0.5, reflectWorld.y / m + 0.5);
            envColor = texture(texture_envmap_2d, envUV).rgb;
        } else {
            envColor = texture(texture_envmap, reflectWorld).rgb;
        }
        float envMask = has_env_mask ? texture(texture_envmask, TexCoords).r : 1.0;
        finalColor += envColor * envMask * envMapScale;
    }

    // --- 5. EMISSIVE ---
    // Multiplied by baseColor.rgb to match the engine-correct math in
    // NifSkope's sk_default.frag / sk_msn.frag:
    //   color.rgb = albedo * (diffuse + emissive) + spec
    // i.e. the emissive is "absorbed" by the surface color -- a black
    // surface emits nothing, a bright surface emits at full intensity.
    // Without this multiply, strong-emissive shapes (e.g., BB's Serana
    // Replacer's vampire eyes with emissive (0.89, 0.65, 0) x 1.42) leak
    // saturated yellow onto every fragment of the mesh including dark
    // regions like lash hairs that are part of the same shape.
    // Glow map (SLSF2_Glow_Map, BSLSP_GLOWMAP gear like the Nightingale
    // cowl's _emit.dds): modulates the emissive per texel, matching
    // sk_default.frag's emittance = glowColor * glowMult * glowMap.rgb.
    // Typical authoring is a white emissiveColor x 1.0 with the pattern
    // and color entirely in the map. Without the map the term is flat as
    // before; the albedo multiply stays (see block comment above).
    if (has_emissive && u_enableEmissive) {
        vec3 emittance = emissiveColor * emissiveMultiple;
        if (has_glow_map) {
            emittance *= texture(texture_glow, TexCoords).rgb;
        }
        finalColor += emittance * baseColor.rgb;
    }

    // --- 6. TONE-MAPPING & COLOR GRADE (CharacterViewer.Rendering 2.5.9+) ---
    // ACES filmic approximation (Narkowicz 2015) compresses HDR highlights,
    // adds a soft toe in the shadows, and produces the warm shoulder that
    // makes the output read as a portrait rather than a flat linear render.
    // Pairs with FRAMEBUFFER_SRGB on the host side: this stage outputs
    // linear values, the framebuffer gamma-encodes on write. Without
    // FRAMEBUFFER_SRGB the output displays too dark.
    //
    // Mild saturation boost (1.10x) afterwards gives skin tones a touch
    // more warmth without looking gaudy. Skip both when the toggle is
    // off so the legacy linear pipeline is reproducible bit-for-bit.
    if (u_enableToneMapping) {
        // Fresnel contour darkening (2.5.13+). Subtle ~15% darkening
        // at silhouette edges where the surface normal is nearly
        // perpendicular to the view direction. Defines the silhouette,
        // adds the slight rim-shadow that real photography has from
        // grazing-angle reflections + microfacet shadowing. Folded
        // under the tone-mapping toggle since both are "finishing"
        // touches that ship together.
        // Hair relief (off by default): hair strands graze the view at the
        // silhouette, so the fresnel contour darkening lands right on the
        // wispy edges and reads as a darker, browner outline. When relieving
        // hair, skip it for hair pixels (skin/everything else untouched).
        bool hairRelief = u_tonemapHairRelief && is_hair_tint;
        vec3 viewDirCam = normalize(-v_viewSpacePos);
        float fresnel = pow(1.0 - max(dot(normal_viewSpace, viewDirCam), 0.0), 4.0);
        finalColor *= mix(1.0, hairRelief ? 1.0 : 0.85, fresnel);

        // Slight exposure pull-down: the lit color sits ~1.0-1.5 in linear
        // space typically; 0.6 keeps the tone-curve toe in a useful range.
        // u_exposure (default 1.0) scales this baseline so the user can
        // brighten/darken the tone-mapped result without re-balancing lights.
        // Hair relief lifts that baseline toward 0.8 so the brown hair midtone
        // is not crushed as hard by the skin-tuned ACES toe.
        float preExp = hairRelief ? 0.8 : 0.6;
        vec3 c = finalColor * preExp * u_exposure;
        c = (c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14);
        float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
        c = mix(vec3(lum), c, 1.10);

        // Vignette (2.5.15+). Subtle radial darkening from screen
        // center toward the corners; reads less as a "vignette effect"
        // and more as the natural lens falloff every photographic
        // portrait has. Folded under the tone-map toggle so the
        // legacy bit-for-bit-reproducible linear path stays untouched
        // when tone-mapping is off.
        //
        // u_vignetteRadius (NDC units, ~0..1.4): pixels within this
        //   distance of screen center are unaffected.
        // u_vignetteIntensity (0..1): how dark the corner pixels go.
        //   0 = off, 1 = corners to black.
        vec2 vignetteUv = gl_FragCoord.xy / u_screenSize;
        vec2 vignetteCentered = vignetteUv * 2.0 - 1.0;
        float vignetteDist = length(vignetteCentered);
        float vignetteFalloff = smoothstep(u_vignetteRadius, 1.4142136, vignetteDist);
        c *= 1.0 - vignetteFalloff * u_vignetteIntensity;

        finalColor = clamp(c, 0.0, 1.0);
    }

    // Alpha output is just the surface's own alpha. Earlier versions of this
    // shader had a luma-based hack to make the wet-eye outer cornea (UBE-
    // style separate alpha-blended overlay shape with a near-black diffuse)
    // fade where dim, since hard-coded GL_SRC_ALPHA / GL_ONE_MINUS_SRC_ALPHA
    // blending would otherwise paint a solid black void over the iris. The
    // hack was replaced (commit history) with per-mesh blend factors read
    // from NiAlphaProperty.SrcBlend/DstBlend -- UBE's wet-eye is authored
    // with additive blend (SRC_ALPHA, ONE), which produces the engine-
    // correct "black cornea adds nothing, bright catchlight adds brightness"
    // behavior natively. No special-case shader logic needed.
    FragColor = vec4(finalColor, baseColor.a);
}
