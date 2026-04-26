#version 330 core

// Wireframe overlay pass. Uses only the position attribute from the standard
// 18-float interleaved vertex layout; other attributes are ignored.
//
// ASCII-only (see GlShaderProgram class remarks for why).

layout (location = 0) in vec3 aPos;

uniform mat4 u_model;
uniform mat4 u_view;
uniform mat4 u_projection;

void main()
{
    gl_Position = u_projection * u_view * u_model * vec4(aPos, 1.0);
}
