#version 330 core

// === INPUTS (Per-Vertex Data) ===
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoords;
layout (location = 3) in vec4 aColor;
layout (location = 4) in vec3 aTangent;
layout (location = 5) in vec3 aBitangent;
// Terrain layer weights (GlMesh.UploadLayerWeights): eight per vertex, in blend order.
// Other meshes never enable these arrays; the fragment shader reads them only for terrain.
layout (location = 6) in vec4 aLayerWeights0;
layout (location = 7) in vec4 aLayerWeights1;

// === OUTPUTS ===
out vec3 v_viewSpacePos;
out vec2 TexCoords;
out vec4 vertexColor;
out mat3 v_tangentToViewMatrix;
out mat3 v_modelToViewNormalMatrix;
// TEMP DEBUG: world-space normal (post model transform, pre view transform)
// used by DEBUG_VIZ_WORLD_NORMAL in the fragment shader. Safe to remove once
// the 90-degree lighting offset is diagnosed.
out vec3 v_worldNormal;
// World-space position of this vertex, used by the fragment shader to
// transform into the key light's clip space for shadow-map sampling.
out vec3 v_worldPos;
out vec4 v_layerWeights0;
out vec4 v_layerWeights1;
// Water only: the reflection colour (rgb) and the fraction of it (a) that the fragment
// shader mixes over the lit body colour.
out vec4 v_waterReflection;
// Water only: (Fresnel, distance factor) separately, for the per-pixel depth path in basic.frag.
out vec2 v_waterTerms;

// === UNIFORMS ===
uniform mat4 u_model;
uniform mat4 u_view;
uniform mat4 u_projection;
uniform vec2 u_uvScale;
uniform vec2 u_uvOffset;

// Section-clip plane in WORLD space, packed as (normal.xyz, -distance). A vertex
// survives when dot(worldPos, normal) >= distance, i.e. when the clip distance
// written below is non-negative. The renderer pushes (0,0,0,1) whenever clipping
// is off, which yields a constant +1 and keeps every vertex, so this line is inert
// unless GL_CLIP_DISTANCE0 is also enabled around the draw. See
// GlRenderer.ClipPlane / ClipPlaneMode / RENDERING_PIPELINE.md.
uniform vec4 u_clipPlane;

// Eye-space depth bias in world units (GlMesh.DepthBias): the vertex slides along its
// view ray, so its screen position is unchanged and only its depth moves. Positive =
// farther from the camera. 0 leaves gl_Position exactly as before.
uniform float u_depthBias;

// Distant LOD kind (GlMesh.DistantLod): 0 none, 1 terrain, 2 objects, 3 trees, 4 water. Terrain LOD gets the
// engine's far depth bias (Community Shaders LodLandscape.hlsli, AdjustLodLandscapeVertexPositionCS):
// D3D clip z += min(1, 1e-4 * max(0, z - 70000)) * 0.5, which pushes terrain LOD behind object LOD
// in the distance. GL's depth range is twice D3D's, so the same depth offset is twice the clip value.
uniform int u_lodKind;

// Water (GlMesh.IsWater): the vertex colour arrives as the body colour (lit in the fragment
// shader like any surface). The reflection fraction is worked out here from the live camera,
// as Community Shaders' Water.hlsl does it: Schlick Fresnel with F0 = the water type's Fresnel,
// and a distance blend between that Fresnel (near) and 1, a mirror (far), ramping between the
// water type's noise falloff distance and 8192 units. Up is +Y. u_waterReflected = the
// reflection colour: the sky, or the water type's reflection colour x reflectivity.
uniform bool u_isWater;
uniform vec3 u_cameraPos;
uniform float u_waterFresnel;
uniform float u_waterNoiseFalloff;
uniform vec3 u_waterReflected;

void main()
{
    vec4 pos_worldSpace = u_model * vec4(aPos, 1.0);

    vec4 pos_viewSpace = u_view * pos_worldSpace;
    if (u_depthBias != 0.0)
        pos_viewSpace.xyz += normalize(pos_viewSpace.xyz) * u_depthBias;
    gl_Position = u_projection * pos_viewSpace;
    if (u_lodKind == 1)
        gl_Position.z += min(1.0, 1e-4 * max(0.0, -pos_viewSpace.z - 70000.0)) * 0.5 * 2.0;
    gl_ClipDistance[0] = dot(pos_worldSpace, u_clipPlane);
    v_viewSpacePos = vec3(u_view * pos_worldSpace);
    v_worldPos = pos_worldSpace.xyz;

    // Normal matrix: model-to-view for MSN path
    mat3 normalMatrix_modelToWorld = mat3(transpose(inverse(u_model)));
    mat3 normalMatrix_modelToView = mat3(u_view) * normalMatrix_modelToWorld;
    v_modelToViewNormalMatrix = normalMatrix_modelToView;

    // TBN matrix for tangent-space normal maps
    vec3 T_viewSpace = normalize(normalMatrix_modelToView * aTangent);
    vec3 B_viewSpace = normalize(normalMatrix_modelToView * aBitangent);
    vec3 N_viewSpace = normalize(normalMatrix_modelToView * aNormal);
    v_tangentToViewMatrix = mat3(T_viewSpace, B_viewSpace, N_viewSpace);

    // TEMP DEBUG: world-space normal for DEBUG_VIZ_WORLD_NORMAL visualization.
    v_worldNormal = normalize(normalMatrix_modelToWorld * aNormal);

    TexCoords = aTexCoords * u_uvScale + u_uvOffset;
    vertexColor = aColor;
    v_layerWeights0 = aLayerWeights0;
    v_layerWeights1 = aLayerWeights1;

    v_waterReflection = vec4(0.0);
    v_waterTerms = vec2(0.0);
    if (u_isWater) {
        vec3 toEye = u_cameraPos - pos_worldSpace.xyz;
        float dist = length(toEye);
        float cosI = max(toEye.y / max(dist, 1e-4), 0.0);
        float f0 = clamp(u_waterFresnel, 0.0, 1.0);
        float fres = f0 + (1.0 - f0) * pow(1.0 - cosI, 5.0);
        // distanceFactor = saturate(lerp(w, 1, (d - 8192) / (falloff - 8192))), with the
        // frame flag w taken as 0 (an in-game aerial reads as a sky mirror at this range).
        float distanceFactor = 1.0;
        if (u_waterNoiseFalloff > 0.0 && abs(u_waterNoiseFalloff - 8192.0) > 1.0)
            distanceFactor = clamp((dist - 8192.0) / (u_waterNoiseFalloff - 8192.0), 0.0, 1.0);
        // specularFraction = lerp(1, fresnel, distanceFactor)
        v_waterReflection = vec4(u_waterReflected, mix(1.0, fres, distanceFactor));
        v_waterTerms = vec2(fres, distanceFactor);
    }
}
