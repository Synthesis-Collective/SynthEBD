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

// Section-clip plane, world space, (normal.xyz, -distance). Clipping the SSAO
// depth/normal prepass with the same plane as the main pass is what stops
// clipped-away geometry from still occluding the surfaces it was hiding: SSAO
// samples this G-buffer, so geometry left in here would darken the newly exposed
// cut surface with ambient occlusion cast by something the user can no longer see.
uniform vec4 u_clipPlane;

void main()
{
    vec4 pos_worldSpace = u_model * vec4(aPos, 1.0);
    gl_Position = u_projection * u_view * pos_worldSpace;
    gl_ClipDistance[0] = dot(pos_worldSpace, u_clipPlane);
    TexCoords = aTexCoords;
    v_viewNormal = normalize(u_normalMatrix * aNormal);
}
