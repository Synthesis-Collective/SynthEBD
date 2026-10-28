#version 330 core

// Wireframe overlay pass. Uses only the position attribute from the standard
// 18-float interleaved vertex layout; other attributes are ignored.
//
// ASCII-only (see GlShaderProgram class remarks for why).

layout (location = 0) in vec3 aPos;

uniform mat4 u_model;
uniform mat4 u_view;
uniform mat4 u_projection;

// Section-clip plane, world space, (normal.xyz, -distance). Must be written here
// as well as in basic.vert: while GL_CLIP_DISTANCE0 is enabled, a vertex shader
// that leaves gl_ClipDistance[0] unwritten produces an undefined clip result, so
// the wireframe overlay would tear against the surface it outlines.
uniform vec4 u_clipPlane;

void main()
{
    vec4 pos_worldSpace = u_model * vec4(aPos, 1.0);
    gl_Position = u_projection * u_view * pos_worldSpace;
    gl_ClipDistance[0] = dot(pos_worldSpace, u_clipPlane);
}
