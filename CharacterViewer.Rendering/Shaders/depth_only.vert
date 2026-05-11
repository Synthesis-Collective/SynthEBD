#version 330 core

// Depth + view-space normal prepass vertex shader. Renders the scene from
// the camera's POV so basic.frag's screen-space position reconstruction
// matches what SSAO sampled, and forwards the interpolated vertex normal
// transformed into view space so the fragment shader can write it to a
// normal G-buffer. SSAO samples that G-buffer rather than reconstructing
// from depth gradients, which avoids per-triangle faceting on smooth
// surfaces (the cross(dFdx, dFdy) reconstruction yields a flat geometric
// face normal, constant across each triangle).

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoords;

out vec2 TexCoords;
out vec3 v_viewNormal;

uniform mat4 u_model;
uniform mat4 u_view;
uniform mat4 u_projection;
uniform mat3 u_normalMatrix;

void main()
{
    gl_Position = u_projection * u_view * u_model * vec4(aPos, 1.0);
    TexCoords = aTexCoords;
    v_viewNormal = normalize(u_normalMatrix * aNormal);
}
