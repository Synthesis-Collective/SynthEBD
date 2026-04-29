#version 330 core

// Depth-only fragment shader for the SSAO pre-pass. Same alpha-test
// path as shadow_depth.frag - cutout shapes (hair / brows / eyelashes)
// must contribute their cutout silhouette to the depth texture, not a
// solid card-shaped occluder, so the SSAO sampler doesn't see fake
// near-plane depth in places where the shape is actually transparent.

in vec2 TexCoords;

uniform sampler2D texture_diffuse;
uniform bool use_alpha_test;
uniform float alpha_threshold;

void main()
{
    if (use_alpha_test) {
        float a = texture(texture_diffuse, TexCoords).a;
        if (a < alpha_threshold) discard;
    }
    // gl_FragDepth is implicit from gl_Position.z / gl_Position.w.
}
