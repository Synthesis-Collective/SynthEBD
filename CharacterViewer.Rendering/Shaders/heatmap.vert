#version 330 core

// Slider-morph heatmap pass. Clones debug.vert but carries a per-vertex color
// (location 2) so the fragment shader can paint each vertex by its morph
// magnitude instead of a single u_color uniform. Pure ASCII (see the shader
// ASCII-only rule in RENDERING_PIPELINE.md).

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec3 aColor;

uniform mat4 u_view;
uniform mat4 u_projection;

out vec3 v_color;

void main()
{
    v_color = aColor;
    gl_Position = u_projection * u_view * vec4(aPos, 1.0);
}
