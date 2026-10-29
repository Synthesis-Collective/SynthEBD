#version 330 core

// Attribution: the face/skin tint composition (the Pegtop soft-light FaceTint blend, the
// SkinTint / FaceGen RGB tint base colour and its colour-shift constant, and the engine-style
// detail-map multiply) is ported to GLSL from the HLSL Lighting shader of Community Shaders
// (doodlum and contributors, https://github.com/community-shaders/skyrim-community-shaders,
// GPL-3.0). See the comments at each ported section and NOTICE.md.

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
in vec4 v_layerWeights0;
in vec4 v_layerWeights1;
in vec4 v_waterReflection;
uniform bool u_isWater;
in vec2 v_waterTerms;   // water only: (Fresnel, distance factor), from basic.vert
// Water, per-pixel depth (GlMesh.WaterSceneDepth): the engine's depth terms from the opaque scene's
// depth (the depth prepass on u_ssaoDepthTex), after Community Shaders' Water.hlsl. See waterRayDepth
// and the composition at the end of main.
uniform bool u_waterSceneDepth;
uniform vec3 u_waterShallow;
uniform vec3 u_waterDeep;
uniform vec3 u_waterFog;   // (above-water fog near, far, amount)
uniform vec3 u_waterSun;   // (sun specular power, sun specular magnitude, depth specular-lighting factor)
// Distance fog (GlRenderer.Fog, DistanceFog.cs), the engine's form:
// f = min(max, saturate(d * y - x) ^ power), u_fogParam = (near/range, 1/range, power, max).
uniform bool u_fogEnabled;
uniform vec3 u_fogNearColor;
uniform vec3 u_fogFarColor;
uniform vec4 u_fogParam;
// Image space (GlRenderer.ImageSpace, ImageSpaceFinish.cs): the engine's HDR tone map and cinematic
// grade, the retail ISHDRTonemapBlendCinematic arithmetic (Reinhard branch, no bloom). It replaces
// the ACES finish below when enabled.
uniform bool u_imageSpaceEnabled;
uniform vec4 u_isParam;     // (k = 1/(white*whiteScale)^2, exposure ratio, adapted luminance, output exponent)
uniform vec4 u_isCinematic; // (saturation, brightness, contrast, unused)
uniform vec4 u_isTint;      // (tint rgb, tint amount)

vec3 engineImageSpace(vec3 a) {
    const vec3 L = vec3(0.2125, 0.7154, 0.0721);
    float y = max(dot(a, L), 0.00001);
    float z = y * u_isParam.y;
    float q = z * (1.0 + u_isParam.x * z) / (1.0 + z);
    vec3 rgb = a * (q / y);
    float k = dot(rgb, L);
    vec3 g = mix(vec3(k), rgb, u_isCinematic.x);
    g = mix(g, k * u_isTint.rgb, u_isTint.a);
    vec3 c = vec3(u_isParam.z) + u_isCinematic.z * (u_isCinematic.y * g - vec3(u_isParam.z));
    return pow(clamp(c, 0.0, 1.0), vec3(u_isParam.w));
}

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
// FaceGen always bakes Model_Space_Normals while many replacer faces
// ship tangent-space (non-MSN).
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
// Terrain layer diffuses (GlMesh.IsTerrain), in blend order. They share texture units with
// samplers terrain never uses (skin, specular, face tint, detail, env mask, 2D env, glow),
// plus unit 13, so the total stays within GL 3.3's guaranteed 16 units.
uniform sampler2D texture_layer0;
uniform sampler2D texture_layer1;
uniform sampler2D texture_layer2;
uniform sampler2D texture_layer3;
uniform sampler2D texture_layer4;
uniform sampler2D texture_layer5;
uniform sampler2D texture_layer6;
uniform sampler2D texture_layer7;
uniform bool u_isTerrain;
// SLSF2_Tree_Anim: vertex alpha is the wind-sway weight, not opacity (GlMesh.VertexAlphaIsWindWeight).
uniform bool vertex_alpha_is_wind;
uniform int u_terrainLayerCount;
// TerrainBlendMode: 0 over, weighted by opacity x texture alpha; 1 over, by opacity;
// 2 base weight 1 plus layer opacities, normalised by their sum.
uniform int u_terrainBlendMode;

// One terrain layer over the albedo so far: "over" blending, weighted by the vertex layer
// weight times the layer texture's alpha (what a separately drawn, alpha-blended layer
// sheet produces).
vec3 terrainLayer(vec3 albedo, sampler2D layer, float weight) {
    vec4 t = texture(layer, TexCoords);
    float w = (u_terrainBlendMode == 0) ? weight * t.a : weight;
    return mix(albedo, t.rgb, clamp(w, 0.0, 1.0));
}

// Terrain layer normal maps (tangent space), on units 14-21 when the context has them.
uniform sampler2D texture_layer_normal0;
uniform sampler2D texture_layer_normal1;
uniform sampler2D texture_layer_normal2;
uniform sampler2D texture_layer_normal3;
uniform sampler2D texture_layer_normal4;
uniform sampler2D texture_layer_normal5;
uniform sampler2D texture_layer_normal6;
uniform sampler2D texture_layer_normal7;
uniform int u_terrainLayerNormalMask;   // bit i: layer i has a normal map bound
uniform bool u_terrainBaseNormal;      // the base texture has a normal map (texture_normal)

// Projected material (GlMesh.HasProjectedMaterial): the engine's single-pass PROJECTED_UV path,
// snow / ash / moss over a static's own diffuse. Its two textures ride the first two layer-normal
// units: a projected mesh is never terrain, so those samplers are free (GlRenderer.DrawMesh).
#define texture_projected_noise   texture_layer_normal0
#define texture_projected_diffuse texture_layer_normal1
uniform bool u_hasProjected;
uniform vec3 u_projectedDir;           // world (Y-up) direction a covered surface faces
uniform vec4 u_projectedParams;        // ProjectedUVParams: x noise weight, z noise per unit, w threshold
uniform vec3 u_projectedColor;         // ProjectedUVParams2
uniform float u_projectedDiffuseTiling; // fProjectedUVDiffuseNormalTilingScale
uniform bool u_projectedHasDiffuse;    // false = flat colour path (diffuse/normals disabled)

// TEMP DEBUG: when true, a projected mesh outputs its coverage weight as grey (white = fully
// covered) instead of shading, to check the coverage pattern against a game capture.
const bool DEBUG_VIZ_PROJECTED_WEIGHT = false;

vec3 terrainLayerNormal(sampler2D s, int i) {
    return ((u_terrainLayerNormalMask >> i) & 1) != 0 ? texture(s, TexCoords).rgb : vec3(0.5, 0.5, 1.0);
}

// A layer's weight in the "over" chain, matching terrainLayer() for the albedo.
float terrainChainWeight(sampler2D layer, float weight) {
    float w = (u_terrainBlendMode == 0) ? weight * texture(layer, TexCoords).a : weight;
    return clamp(w, 0.0, 1.0);
}

// The terrain's blended normal-map sample (0..1 encoded), weighted exactly like its albedo:
// a layer without a normal map contributes a flat normal.
vec3 terrainNormalSample() {
    vec3 n = u_terrainBaseNormal ? texture(texture_normal, TexCoords).rgb : vec3(0.5, 0.5, 1.0);
    if (u_terrainBlendMode == 2) {
        vec3 sum = n;
        float total = 1.0;
        if (u_terrainLayerCount > 0 && v_layerWeights0.x > 0.0) { sum += terrainLayerNormal(texture_layer_normal0, 0) * v_layerWeights0.x; total += v_layerWeights0.x; }
        if (u_terrainLayerCount > 1 && v_layerWeights0.y > 0.0) { sum += terrainLayerNormal(texture_layer_normal1, 1) * v_layerWeights0.y; total += v_layerWeights0.y; }
        if (u_terrainLayerCount > 2 && v_layerWeights0.z > 0.0) { sum += terrainLayerNormal(texture_layer_normal2, 2) * v_layerWeights0.z; total += v_layerWeights0.z; }
        if (u_terrainLayerCount > 3 && v_layerWeights0.w > 0.0) { sum += terrainLayerNormal(texture_layer_normal3, 3) * v_layerWeights0.w; total += v_layerWeights0.w; }
        if (u_terrainLayerCount > 4 && v_layerWeights1.x > 0.0) { sum += terrainLayerNormal(texture_layer_normal4, 4) * v_layerWeights1.x; total += v_layerWeights1.x; }
        if (u_terrainLayerCount > 5 && v_layerWeights1.y > 0.0) { sum += terrainLayerNormal(texture_layer_normal5, 5) * v_layerWeights1.y; total += v_layerWeights1.y; }
        if (u_terrainLayerCount > 6 && v_layerWeights1.z > 0.0) { sum += terrainLayerNormal(texture_layer_normal6, 6) * v_layerWeights1.z; total += v_layerWeights1.z; }
        if (u_terrainLayerCount > 7 && v_layerWeights1.w > 0.0) { sum += terrainLayerNormal(texture_layer_normal7, 7) * v_layerWeights1.w; total += v_layerWeights1.w; }
        return sum / total;
    }
        if (u_terrainLayerCount > 0) n = mix(n, terrainLayerNormal(texture_layer_normal0, 0), terrainChainWeight(texture_layer0, v_layerWeights0.x));
        if (u_terrainLayerCount > 1) n = mix(n, terrainLayerNormal(texture_layer_normal1, 1), terrainChainWeight(texture_layer1, v_layerWeights0.y));
        if (u_terrainLayerCount > 2) n = mix(n, terrainLayerNormal(texture_layer_normal2, 2), terrainChainWeight(texture_layer2, v_layerWeights0.z));
        if (u_terrainLayerCount > 3) n = mix(n, terrainLayerNormal(texture_layer_normal3, 3), terrainChainWeight(texture_layer3, v_layerWeights0.w));
        if (u_terrainLayerCount > 4) n = mix(n, terrainLayerNormal(texture_layer_normal4, 4), terrainChainWeight(texture_layer4, v_layerWeights1.x));
        if (u_terrainLayerCount > 5) n = mix(n, terrainLayerNormal(texture_layer_normal5, 5), terrainChainWeight(texture_layer5, v_layerWeights1.y));
        if (u_terrainLayerCount > 6) n = mix(n, terrainLayerNormal(texture_layer_normal6, 6), terrainChainWeight(texture_layer6, v_layerWeights1.z));
        if (u_terrainLayerCount > 7) n = mix(n, terrainLayerNormal(texture_layer_normal7, 7), terrainChainWeight(texture_layer7, v_layerWeights1.w));
    return n;
}

// Normalised-sum accumulation for one layer (u_terrainBlendMode == 2).
void terrainAccumulate(inout vec3 sum, inout float total, sampler2D layer, float weight) {
    if (weight > 0.0) {
        sum += texture(layer, TexCoords).rgb * weight;
        total += weight;
    }
}

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
// compensates for downstream desaturation that washes distinctive race
// skin tones toward neutral. Set per-mesh by the host.
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
// Hair-shadow troubleshooting Strategy B (soften + de-warp). When true,
// sampleShadowPCF drops the slope-scaled bias (whose NdotL variation warps
// the cast-shadow boundary into a "brow ridge" across the curved glabella)
// in favor of a small constant bias, and widens the PCF kernel by
// u_shadowPcfRadius so the strand-cutout pattern reads as a soft shadow.
uniform bool u_softenShadowEdges;
uniform float u_shadowPcfRadius;
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
// u_hairAlbedoCompensate: strength (0 = off) of the neutral-white-tint albedo
// compensation. This renderer lights on raw sRGB texels (gamma space), so a hair
// whose baked BSLSP tint is a neutral WHITE keeps its albedo un-attenuated (~3x
// the linear value) and clips under the rig -- a red wig reads pink. A colored
// tint (dark auburn, warm blonde) already multiplies the albedo down and never
// clips, so this keys off tint neutrality and applies the sRGB->linear the
// pipeline skips, ONLY to near-white-tint hair. Hair pixels only.
uniform float u_hairAlbedoCompensate;
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
// BSLightingShaderProperty.alpha. Scales the whole fragment alpha; applied
// alongside the vertex-color alpha below, before the alpha test.
uniform float material_alpha;
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
    vec2 texelSize = 1.0 / vec2(textureSize(u_shadowMap, 0));

    if (u_softenShadowEdges) {
        // Strategy B: the slope-scaled bias above swings from ~0.6 to
        // ~3.6 world units across the curved brow, which slides the
        // bangs' cast-shadow edge in and out along NdotL contours and
        // reads as an embossed "brow ridge". Use a small constant bias
        // instead (front-face culling in the depth pass already guards
        // acne), and widen the kernel so the strand cutout blurs into a
        // soft shadow. 5x5 taps stepped by u_shadowPcfRadius texels.
        uv.z -= 0.0008;
        float sum = 0.0;
        for (int x = -2; x <= 2; x++) {
            for (int y = -2; y <= 2; y++) {
                vec2 off = vec2(x, y) * texelSize * u_shadowPcfRadius;
                sum += texture(u_shadowMap, vec3(uv.xy + off, uv.z));
            }
        }
        return sum / 25.0;
    }

    float bias = max(0.003 * (1.0 - NdotL), 0.0005);
    uv.z -= bias;

    // 3x3 PCF kernel on top of the hardware bilinear PCF.
    float sum = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 off = vec2(x, y) * texelSize;
            sum += texture(u_shadowMap, vec3(uv.xy + off, uv.z));
        }
    }
    return sum / 9.0;
}

// --- EFFECT SHADER (GlMesh.IsEffectShader) ---
// BSEffectShaderProperty shapes: fire, smoke, mist, light shafts, magic glows. A port of the
// static (non-animated, non-membrane, non-particle) path of the engine's effect pixel shader,
// after Community Shaders' Effect.hlsl with its linear-lighting option off:
//   base   = sourceTexture (alpha forced to 1 under greyscale-to-alpha)
//   mul    = baseColor * vertexColor
//   base.a *= falloff            (SLSF1_Use_Falloff, smoothstep over |N.V|)
//   base   = mul * base; base.a *= soft depth fade (SLSF1_Soft_Effect)
//   alpha  = greyscale-to-alpha ? palette(base.a_tex, alpha).a : base.a
//   rgb    = greyscale-to-color ? baseColorScale * palette(tex.g, mul.r).rgb : base.rgb
//   rgb    = mix(rgb, light * rgb, lightingInfluence)
// baseColorScale only ever scales the palette colour. Blending (additive or "over") comes from
// the NiAlphaProperty factors like any other shape. "light" is the engine's PropertyColor: with
// SLSF2_Effect_Lighting it is the unshadowed ambient + directional light at the object
// (GetLightingColor's DLightColor), approximated here by the scene's summed lights; without the
// flag it is a per-object constant this renderer treats as white, so a self-lit flame with
// lightingInfluence 1 stays self-lit. Not modelled: UV/alpha animation controllers, membrane and
// blood variants, fog, point lights.
uniform bool u_isEffect;
uniform vec4 u_effectBaseColor;
uniform float u_effectBaseColorScale;
uniform float u_effectLightingInfluence;
uniform bool u_effectLit;              // SLSF2_Effect_Lighting
uniform bool u_effectHasTexture;
uniform bool u_effectGreyscaleColor;
uniform bool u_effectGreyscaleAlpha;
uniform bool u_effectFalloff;
// FalloffData: (startAngle, stopAngle, startOpacity, stopOpacity), angles as cosines.
uniform vec4 u_effectFalloffData;
// Soft-effect depth range in game units; 0 = not a soft effect.
uniform float u_effectSoftDepth;
uniform bool u_effectClampS;
uniform bool u_effectClampT;
// GlMesh.EffectBillboard: 0 none, 1 faces the camera, 2 turns about the up axis.
uniform int u_effectBillboard;
// TEMP DEBUG: effect-shader stage visualisation. 0 = off; 1 = source texture rgb; 2 = palette
// colour; 3 = final alpha as grey; 4 = vertex colour alpha as grey. Output alpha is 1, so an
// additive shape shows the value directly.
const int DEBUG_VIZ_EFFECT = 0;

// The greyscale palette rides the glow unit: an effect shape never has a glow map.
#define texture_effect_greyscale texture_glow

// Engine smoothstep falloff, from Effect.hlsl's vertex shader (here per pixel). Exposed as a
// function so the behaviour is in one place.
float effectFalloff(vec3 worldNormal, vec3 toEye, vec4 fd) {
    vec3 v = normalize(toEye);
    // A billboard is drawn in its authored pose, but the engine evaluates falloff on the turned
    // sprite, whose normal points at the camera (or at it in the horizontal plane).
    float d = u_effectBillboard == 1 ? 1.0
            : u_effectBillboard == 2 ? length(v.xz)
            : abs(dot(normalize(worldNormal), v));
    float span = fd.y - fd.x;
    float f = abs(span) > 1e-6 ? clamp((d - fd.x) / span, 0.0, 1.0) : (d >= fd.x ? 1.0 : 0.0);
    f = f * f * (3.0 - 2.0 * f);
    return mix(fd.z, fd.w, f);
}

// Palette lookup with clamp-to-edge coordinates: palettes are thin gradient strips, and a
// repeating sampler would blend the far end into a coordinate of exactly 1.0 (a white core).
vec4 effectPalette(vec2 uv) {
    vec2 h = 0.5 / vec2(textureSize(texture_effect_greyscale, 0));
    return textureLod(texture_effect_greyscale, clamp(uv, h, vec2(1.0) - h), 0.0);
}

vec4 shadeEffect() {
    if (DEBUG_VIZ_EFFECT == 4) return vec4(vec3(has_vertex_colors ? vertexColor.a : 1.0), 1.0);
    if (DEBUG_VIZ_EFFECT == 7) return vec4(u_effectFalloff ? 1.0 : 0.0, u_effectGreyscaleAlpha ? 1.0 : 0.0, u_effectSoftDepth > 0.0 ? 1.0 : 0.0, 1.0);
    if (DEBUG_VIZ_EFFECT == 6) {
        if (!u_effectFalloff) discard;
        return vec4(vec3(effectFalloff(v_worldNormal, u_cameraPos - v_worldPos, u_effectFalloffData)), 1.0);
    }
    if (DEBUG_VIZ_EFFECT == 5) return vec4(vec3(abs(dot(normalize(v_worldNormal), normalize(u_cameraPos - v_worldPos)))), 1.0);
    vec2 uv = TexCoords;
    if (u_effectClampS || u_effectClampT) {
        vec2 h = 0.5 / vec2(textureSize(texture_diffuse, 0));
        if (u_effectClampS) uv.x = clamp(uv.x, h.x, 1.0 - h.x);
        if (u_effectClampT) uv.y = clamp(uv.y, h.y, 1.0 - h.y);
    }
    vec4 baseTex = vec4(1.0);
    vec4 base = vec4(1.0);
    if (u_effectHasTexture || u_effectGreyscaleColor || u_effectGreyscaleAlpha) {
        baseTex = u_enableDiffuse ? texture(texture_diffuse, uv) : vec4(0.8, 0.8, 0.8, 1.0);
        base = baseTex;
        if (u_effectGreyscaleAlpha) base.a = 1.0;
    }

    vec4 mulc = u_effectBaseColor;
    if (has_vertex_colors) mulc *= vertexColor;

    if (u_effectFalloff)
        base.a *= effectFalloff(v_worldNormal, u_cameraPos - v_worldPos, u_effectFalloffData);

    base = mulc * base;

    // Soft effect: fade over u_effectSoftDepth as the scene surface behind comes close. Needs the
    // ambient-occlusion depth prepass (opaque geometry); without it the fade is skipped.
    if (u_effectSoftDepth > 0.0) {
        if (u_enableAO) {
            vec2 suv = gl_FragCoord.xy / u_screenSize;
            vec4 sceneView = u_invProjection * vec4(suv * 2.0 - 1.0, texture(u_ssaoDepthTex, suv).r * 2.0 - 1.0, 1.0);
            vec4 fragView = u_invProjection * vec4(suv * 2.0 - 1.0, gl_FragCoord.z * 2.0 - 1.0, 1.0);
            float gap = fragView.z / fragView.w - sceneView.z / sceneView.w;
            base.a *= clamp(gap / u_effectSoftDepth, 0.0, 1.0);
        }
        if (base.a < 0.003) discard;
    }

    float alpha = base.a;
    if (u_effectGreyscaleAlpha)
        alpha = effectPalette(vec2(baseTex.a, alpha)).a;
    if (u_effectGreyscaleColor)
        base.rgb = u_effectBaseColorScale * effectPalette(vec2(baseTex.g, mulc.r)).rgb;

    vec3 rgb = base.rgb;
    if (u_effectLit && u_effectLightingInfluence > 0.0) {
        vec3 sceneLight = vec3(0.0);
        for (int i = 0; i < MAX_LIGHTS; i++)
            if (lights[i].type != 0) sceneLight += lights[i].color * lights[i].intensity;
        rgb = mix(rgb, sceneLight * rgb, u_effectLightingInfluence);
    }

    if (DEBUG_VIZ_EFFECT == 1) return vec4(baseTex.rgb, 1.0);
    if (DEBUG_VIZ_EFFECT == 2) return vec4(effectPalette(vec2(baseTex.g, mulc.r)).rgb, 1.0);
    if (DEBUG_VIZ_EFFECT == 3) return vec4(vec3(alpha), 1.0);

    if (use_alpha_test && alpha < alpha_threshold) discard;


    // Same finishing as stage 6 (no fresnel contour: an effect has no surface to darken).
    // Kept in step with that block by hand. The image space, when on, grades the fragment before
    // blending (the engine grades the blended frame; an 8-bit target leaves no other place).
    if (u_imageSpaceEnabled) return vec4(engineImageSpace(rgb), alpha);
    if (u_enableToneMapping) {
        vec3 c = rgb * 0.6 * u_exposure;
        c = (c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14);
        float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
        c = mix(vec3(lum), c, 1.10);
        vec2 vc = (gl_FragCoord.xy / u_screenSize) * 2.0 - 1.0;
        c *= 1.0 - smoothstep(u_vignetteRadius, 1.4142136, length(vc)) * u_vignetteIntensity;
        rgb = clamp(c, 0.0, 1.0);
    }
    return vec4(rgb, alpha);
}

// Water depth along this pixel's view ray: the distance from the water surface to the opaque scene
// behind it, as the engine measures it (Water.hlsl: the depth buffer's view position, the share of
// the ray beyond the water plane, times its length). With one flat surface per pixel that is the
// distance to the scene minus the distance to this fragment. Nothing behind (the sky) is deep.
float waterRayDepth() {
    vec2 suv = gl_FragCoord.xy / u_screenSize;
    float z = texture(u_ssaoDepthTex, suv).r;
    if (z >= 1.0) return 1.0e9;
    vec4 scene = u_invProjection * vec4(suv * 2.0 - 1.0, z * 2.0 - 1.0, 1.0);
    return max(length(scene.xyz / scene.w) - length(v_viewSpacePos), 0.0);
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
    // this should produce the same colors as DEBUG_VIZ_WORLD_NORMAL if the
    // MSN swizzle in section 2 is correct. If colors are rotated 90deg
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

    // Effect shaders take their own path end to end (see shadeEffect).
    if (u_isEffect) {
        FragColor = shadeEffect();
        return;
    }

    // --- 1. BASE COLOR & ALPHA TEST ---
    vec4 baseColor;
    if (u_enableDiffuse) {
        baseColor = texture(texture_diffuse, TexCoords);
        if (u_isTerrain) {
            // Terrain: the quadrant's base texture, then each alpha layer in record order.
            // Lighting runs once on the result, below, exactly as it does for any mesh.
            vec3 albedo = baseColor.rgb;
            if (u_terrainBlendMode == 2) {
                vec3 sum = albedo;
                float total = 1.0;
                if (u_terrainLayerCount > 0) terrainAccumulate(sum, total, texture_layer0, v_layerWeights0.x);
                if (u_terrainLayerCount > 1) terrainAccumulate(sum, total, texture_layer1, v_layerWeights0.y);
                if (u_terrainLayerCount > 2) terrainAccumulate(sum, total, texture_layer2, v_layerWeights0.z);
                if (u_terrainLayerCount > 3) terrainAccumulate(sum, total, texture_layer3, v_layerWeights0.w);
                if (u_terrainLayerCount > 4) terrainAccumulate(sum, total, texture_layer4, v_layerWeights1.x);
                if (u_terrainLayerCount > 5) terrainAccumulate(sum, total, texture_layer5, v_layerWeights1.y);
                if (u_terrainLayerCount > 6) terrainAccumulate(sum, total, texture_layer6, v_layerWeights1.z);
                if (u_terrainLayerCount > 7) terrainAccumulate(sum, total, texture_layer7, v_layerWeights1.w);
                baseColor = vec4(sum / total, 1.0);
            } else {
                if (u_terrainLayerCount > 0) albedo = terrainLayer(albedo, texture_layer0, v_layerWeights0.x);
                if (u_terrainLayerCount > 1) albedo = terrainLayer(albedo, texture_layer1, v_layerWeights0.y);
                if (u_terrainLayerCount > 2) albedo = terrainLayer(albedo, texture_layer2, v_layerWeights0.z);
                if (u_terrainLayerCount > 3) albedo = terrainLayer(albedo, texture_layer3, v_layerWeights0.w);
                if (u_terrainLayerCount > 4) albedo = terrainLayer(albedo, texture_layer4, v_layerWeights1.x);
                if (u_terrainLayerCount > 5) albedo = terrainLayer(albedo, texture_layer5, v_layerWeights1.y);
                if (u_terrainLayerCount > 6) albedo = terrainLayer(albedo, texture_layer6, v_layerWeights1.z);
                if (u_terrainLayerCount > 7) albedo = terrainLayer(albedo, texture_layer7, v_layerWeights1.w);
                // Terrain is opaque: landscape diffuse alpha is not coverage.
                baseColor = vec4(albedo, 1.0);
            }
        }
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
        // Terrain vertex colours (VCLR) tint without darkening: the engine divides each by its
        // brightest channel first. White (the default) is unchanged.
        vec3 vc = u_isTerrain
            ? vertexColor.rgb / max(max(max(vertexColor.r, vertexColor.g), vertexColor.b), 1e-5)
            : vertexColor.rgb;
        baseColor.rgb *= vc;
        if (!vertex_alpha_is_wind) baseColor.a *= vertexColor.a;
    }

    // Water, per-pixel depth: the body colour is shallow to deep over the view-ray depth
    // (Water.hlsl refractionDiffuseColor = lerp(shallow, deep, distanceMul.y)), lit below like any
    // surface.
    float waterDepth = 0.0;
    if (u_isWater && u_waterSceneDepth) {
        waterDepth = waterRayDepth();
        float dm = u_waterFog.y > 0.0 ? clamp(waterDepth / u_waterFog.y, 0.0, 1.0) : 1.0;
        baseColor.rgb = mix(u_waterShallow, u_waterDeep, dm);
    }

    // Material alpha scales the whole alpha, matching NifSkope's sk_default /
    // sk_msn / sk_multilayer (all three do a plain "gl_FragColor.a *= alpha")
    // and the engine. It MUST land before the alpha test: a mod that hides a
    // head part by zeroing the material alpha relies on the cutout discarding
    // every texel, and skipping this made such shapes draw fully opaque.
    // Unconditional is safe -- the opaque and alpha-test passes run with
    // blending off, so the framebuffer ignores this channel there.
    baseColor.a *= material_alpha;

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

    // Neutral-white-tint albedo compensation (hair only). See u_hairAlbedoCompensate.
    // A near-white baked tint means the albedo was never pulled down, so in this
    // gamma-space pipeline it sits ~3x too bright and clips (red wig -> pink). Key
    // off tint neutrality (the tint's MIN channel): near-white -> apply the
    // sRGB->linear the pipeline skips (deep, not clipped); any colored tint (dark
    // auburn, warm blonde) has a low min channel and is exempt, so the dark-tint
    // replacers and blondes are untouched. Runs after the tint multiply so it acts
    // on the effective albedo.
    if (is_hair_tint && u_hairAlbedoCompensate > 0.0) {
        float tintNeutral = min(min(tint_color.r, tint_color.g), tint_color.b);
        float w = smoothstep(0.60, 0.95, tintNeutral) * u_hairAlbedoCompensate;
        baseColor.rgb = mix(baseColor.rgb,
                            pow(max(baseColor.rgb, vec3(0.0)), vec3(2.2)),
                            clamp(w, 0.0, 1.0));
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
    // both vanilla and replacer FaceTints). The blend choice is gated on
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
            // the model's forward direction as +Z_local. The renderer uses
            // Y-up world space with models facing -Z_world, so flip Z to
            // align. Unlike tangent-space normal maps, MSN does NOT use the
            // DirectX G-flip convention because MSN is model-space not
            // tangent-space. Empirical verification: raw texture sample at
            // chest is (0.5, 0.5, 1.0) -> u = (0,0,1); after flipping Z we
            // get (0,0,-1) which correctly faces the camera at Az=180.
            normal_modelSpace = vec3(normal_modelSpace.x, normal_modelSpace.y, -normal_modelSpace.z);
            normal_viewSpace = normalize(v_modelToViewNormalMatrix * normal_modelSpace);
        }
        else if (tbnIsValid) {
            vec3 normal_tangentSpace = (u_isTerrain ? terrainNormalSample()
                                                    : texture(texture_normal, TexCoords).rgb) * 2.0 - 1.0;
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

    // --- 2b. PROJECTED MATERIAL ---
    // The engine's single-pass PROJECTED_UV path (decompiled vanilla SE lighting shader; Community
    // Shaders keeps the same weight). Coverage is per PIXEL against the normal-mapped normal, so
    // the material settles into the base texture's own relief, and a tiling noise breaks the edge:
    //   w = dot(N, dir) * vertexAlpha - params.w - params.x * noise
    // with noise and diffuse planar in world X/Y (Skyrim's horizontal plane). The covered albedo
    // replaces the base before the vertex colour, which the engine applies afterwards; lighting
    // keeps the base normal (vanilla lerps the projected normal into a buffer lighting never reads).
    if (u_hasProjected) {
        vec3 n_world = normalize(transpose(mat3(u_view)) * normal_viewSpace);
        vec2 projUv = vec2(v_worldPos.x, -v_worldPos.z) * u_projectedParams.z;
        float projNoise = texture(texture_projected_noise, projUv).r;
        float projAlpha = vertex_alpha_is_wind ? 1.0 : vertexColor.a;
        float projW = dot(n_world, normalize(u_projectedDir)) * projAlpha
                    - u_projectedParams.w - u_projectedParams.x * projNoise;
        vec3 projAlbedo = u_projectedColor;
        float projMix;
        if (u_projectedHasDiffuse) {
            projAlbedo *= texture(texture_projected_diffuse, projUv * u_projectedDiffuseTiling).rgb;
            projMix = smoothstep(-0.1, 0.1, projW);
        } else {
            projMix = projW > 0.0 ? 1.0 : 0.0;
        }
        if (applyVertexColors) projAlbedo *= vertexColor.rgb;
        baseColor.rgb = mix(baseColor.rgb, projAlbedo, projMix);
        if (DEBUG_VIZ_PROJECTED_WEIGHT) {
            FragColor = vec4(vec3(projMix), 1.0);
            return;
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
    // eyeballs sit a tiny delta-Z behind the lash cards. The depth prepass
    // (GlRenderer.cs RenderDepthPrepass) now skips every alpha-test and
    // alpha-blend mesh, lashes included, but an eyeball's AO texel still
    // sits on the sharp depth step at the lid edge, and SSAO amplifies
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
            // hair reads blonde without hand-tuning the Key arrow. On by
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
            // multiply so the warm skin hue isn't double-tinted away;
            // sss_color carries its own already-skin-tinted color.
            //
            // u_subsurfaceStrength is a global multiplier letting users
            // dial the SSS up toward the more pronounced look in
            // studio photography. 0 disables; 1 is honest
            // source-value SSS; >1 boosts. At 0 the corrected pipeline
            // produces zero contribution, matching the pre-2.5.14 look
            // when the host has SubsurfaceStrength set to 0.
            vec3 subsurface = vec3(0.0);
            if (has_skin_map && u_enableSkin && u_subsurfaceStrength > 0.0) {
                float sss_mask = texture(texture_skin, TexCoords).r;

                // Warm skin tint, biased toward surface color. Only
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
                    // keeps our warm skin bias (a deliberate deviation
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
                // hair with a real lobe (e.g. some modded hair) it reads metallic.
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
            // SSS is added separately so its warm skin tint isn't
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

    if (u_isWater && u_waterSceneDepth) {
        // Water, per-pixel depth. Community Shaders' Water.hlsl (vertex-colour branch):
        //   refractionMul = 1 - saturate((far - min(d, far)) / (far - near)) ^ amount
        //   specularFraction = lerp(1, fresnel * refractionMul, distanceFactor)
        //   preFog = lerp(lerp(bed, body, refractionMul), reflection, specularFraction) + sun
        //   final = lerp(preFog, fogColor, fog)
        // d is the view-ray depth; bed is what lies behind (already drawn; the engine's refraction
        // texture, here the framebuffer under the blend). Far away (distanceFactor 0) the surface is
        // all reflection and hides the bed. Written as colour + alpha over the bed:
        // alpha = 1 - bed weight.
        float far = u_waterFog.y;
        float rm = 1.0;
        if (far > 0.0) {
            float range = max(far - u_waterFog.x, 1.0e-3);
            rm = 1.0 - pow(clamp((far - min(waterDepth, far)) / range, 0.0, 1.0), max(u_waterFog.z, 0.0));
        }
        float sf = mix(1.0, v_waterTerms.x * rm, v_waterTerms.y);
        // Sun specular (Water.hlsl GetSunColor; the vanilla SIMPLE technique's form):
        // saturate(dot(reflect(view, normal), sunDir)) ^ power x sun colour x magnitude, faded over
        // shallow water by the depth specular-lighting factor (depthControl.w). The key light is the sun.
        vec3 sunSpec = vec3(0.0);
        if (lights[1].type == 2 && u_waterSun.x > 0.0) {
            vec3 r = reflect(normalize(v_viewSpacePos), normal_viewSpace);
            float rl = clamp(dot(r, normalize(lights[1].direction)), 0.0, 1.0);
            float dm = far > 0.0 ? clamp(waterDepth / far, 0.0, 1.0) : 1.0;
            sunSpec = pow(rl, u_waterSun.x) * lights[1].color * lights[1].intensity * u_waterSun.y
                    * (1.0 + u_waterSun.z * (dm - 1.0));
        }
        vec3 overBed = (1.0 - sf) * rm * finalColor + sf * v_waterReflection.rgb + sunSpec;
        float bedWeight = (1.0 - sf) * (1.0 - rm);
        if (u_fogEnabled) {
            float fogF = min(u_fogParam.w, pow(clamp(length(v_viewSpacePos) * u_fogParam.y - u_fogParam.x, 0.0, 1.0), u_fogParam.z));
            overBed = (1.0 - fogF) * overBed + fogF * mix(u_fogNearColor, u_fogFarColor, fogF);
            bedWeight *= 1.0 - fogF;
        }
        baseColor.a = 1.0 - bedWeight;
        finalColor = baseColor.a > 1.0e-4 ? overBed / baseColor.a : vec3(0.0);
    } else {
        // Water: the reflection over the lit body colour (basic.vert works out the fraction).
        if (u_isWater) finalColor = mix(finalColor, v_waterReflection.rgb, v_waterReflection.a);

        // Distance fog, over the lit (and reflected) colour, as the engine applies it last.
        if (u_fogEnabled) {
            float fogF = min(u_fogParam.w, pow(clamp(length(v_viewSpacePos) * u_fogParam.y - u_fogParam.x, 0.0, 1.0), u_fogParam.z));
            finalColor = mix(finalColor, mix(u_fogNearColor, u_fogFarColor, fogF), fogF);
        }
    }

    // --- 6. TONE-MAPPING & COLOR GRADE (since 2.5.9) ---
    // ACES filmic approximation (Narkowicz 2015) compresses HDR highlights,
    // adds a soft toe in the shadows, and produces the warm shoulder that
    // makes the output read as photographic rather than a flat linear render.
    // Pairs with FRAMEBUFFER_SRGB on the host side: this stage outputs
    // linear values, the framebuffer gamma-encodes on write. Without
    // FRAMEBUFFER_SRGB the output displays too dark.
    //
    // Mild saturation boost (1.10x) afterwards gives skin tones a touch
    // more warmth without looking gaudy. Skip both when the toggle is
    // off so the legacy linear pipeline is reproducible bit-for-bit.
    if (u_imageSpaceEnabled) {
        // The engine's image space instead (see engineImageSpace above).
        finalColor = engineImageSpace(finalColor);
    } else if (u_enableToneMapping) {
        // Fresnel contour darkening (2.5.13+). Subtle ~15% darkening
        // at silhouette edges where the surface normal is nearly
        // perpendicular to the view direction. Defines the silhouette,
        // adds the slight rim-shadow that real photography has from
        // grazing-angle reflections + microfacet shadowing. Folded
        // under the tone-mapping toggle since both are "finishing"
        // touches that ship together.
        // Hair relief (on by default): hair strands graze the view at the
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
        // and more as the natural lens falloff of a camera lens. Folded under the tone-map toggle so the
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
    // shader had a luma-based hack to make the wet-eye outer cornea (replacer-
    // style separate alpha-blended overlay shape with a near-black diffuse)
    // fade where dim, since hard-coded GL_SRC_ALPHA / GL_ONE_MINUS_SRC_ALPHA
    // blending would otherwise paint a solid black void over the iris. The
    // hack was replaced (commit history) with per-mesh blend factors read
    // from NiAlphaProperty.SrcBlend/DstBlend -- a replacer's wet-eye is authored
    // with additive blend (SRC_ALPHA, ONE), which produces the engine-
    // correct "black cornea adds nothing, bright catchlight adds brightness"
    // behavior natively. No special-case shader logic needed.
    FragColor = vec4(finalColor, baseColor.a);
}
