#version 330 core

// Depth-only vertex shader for the SSAO depth pre-pass. Renders the
// scene from the camera's POV so basic.frag's screen-space position
// reconstruction matches what SSAO sampled. Identical structure to
// shadow_depth.vert but uses u_view + u_projection instead of a
// pre-multiplied light view-proj, since the host already has those
// matrices for the main pass.

layout (location = 0) in vec3 aPos;
layout (location = 2) in vec2 aTexCoords;

out vec2 TexCoords;

uniform mat4 u_model;
uniform mat4 u_view;
uniform mat4 u_projection;

void main()
{
    gl_Position = u_projection * u_view * u_model * vec4(aPos, 1.0);
    TexCoords = aTexCoords;
}
