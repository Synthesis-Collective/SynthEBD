#version 330 core

// Minimal depth-only vertex shader for the key light's shadow-map pass.
// Transforms model-space vertex positions into the light's clip space;
// the fragment shader writes only depth (no color attachment) so the
// resulting depth texture can be sampled by basic.frag for PCF shadows.

layout (location = 0) in vec3 aPos;
layout (location = 2) in vec2 aTexCoords;

out vec2 TexCoords;

uniform mat4 u_model;
uniform mat4 u_lightViewProj;

// Section-clip plane, world space, (normal.xyz, -distance). Clipped in WORLD
// space even though gl_Position here is in the LIGHT's clip space, so the shadow
// pass drops exactly the geometry the camera pass drops. Without this, clipping
// the chest away would still leave the chest's shadow cast across the ribcage
// the user just uncovered.
uniform vec4 u_clipPlane;

void main()
{
    vec4 pos_worldSpace = u_model * vec4(aPos, 1.0);
    gl_Position = u_lightViewProj * pos_worldSpace;
    gl_ClipDistance[0] = dot(pos_worldSpace, u_clipPlane);
    // Forwarded so the alpha-test-aware fragment can sample the diffuse
    // texture and discard transparent texels (hair / brow strands), so
    // their cutout silhouette is reflected in the cast shadow rather than
    // appearing as solid card-shaped occluders.
    TexCoords = aTexCoords;
}
