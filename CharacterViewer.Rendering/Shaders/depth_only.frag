#version 330 core

// Depth + view-space normal prepass fragment shader. Writes the encoded
// view-space normal to color attachment 0 (RGB8, packed *0.5+0.5 so that
// negative components survive an unsigned format). Depth is written
// implicitly via gl_Position.z / gl_Position.w.
//
// Alpha-test path matches shadow_depth.frag - cutout shapes (hair /
// brows / eyelashes) must contribute their cutout silhouette so neither
// the depth texture nor the normal G-buffer report a solid card-shaped
// occluder where the shape is actually transparent.

in vec2 TexCoords;
in vec3 v_viewNormal;

layout(location = 0) out vec4 fragNormal;

uniform sampler2D texture_diffuse;
uniform bool use_alpha_test;
uniform float alpha_threshold;
// Kept in lockstep with shadow_depth.frag per the note above. Inert today: this
// pass skips every alpha-test and alpha-blend mesh, so nothing reaching it has a
// cutout to fold the material alpha into.
uniform float material_alpha;

void main()
{
    if (use_alpha_test) {
        float a = texture(texture_diffuse, TexCoords).a * material_alpha;
        if (a < alpha_threshold) discard;
    }
    fragNormal = vec4(normalize(v_viewNormal) * 0.5 + 0.5, 1.0);
    // gl_FragDepth is implicit from gl_Position.z / gl_Position.w.
}
