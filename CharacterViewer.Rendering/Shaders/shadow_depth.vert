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

void main()
{
    gl_Position = u_lightViewProj * u_model * vec4(aPos, 1.0);
    // Forwarded so the alpha-test-aware fragment can sample the diffuse
    // texture and discard transparent texels (hair / brow strands), so
    // their cutout silhouette is reflected in the cast shadow rather than
    // appearing as solid card-shaped occluders.
    TexCoords = aTexCoords;
}
