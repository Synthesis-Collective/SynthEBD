#version 330 core

// Depth-only fragment shader for the shadow-map pass. With no out
// declaration, the GL driver writes only gl_FragDepth implicitly. We DO
// run an alpha test for cutout shapes (hair / brows / eyelashes) so
// transparent texels don't cast a card-shaped shadow.

in vec2 TexCoords;

uniform sampler2D texture_diffuse;
uniform bool use_alpha_test;
uniform float alpha_threshold;
// BSLightingShaderProperty.alpha, folded in exactly as basic.frag does it, so a
// shape hidden by a zeroed material alpha stops casting a shadow it does not
// cast in game.
uniform float material_alpha;

void main()
{
    if (use_alpha_test) {
        float a = texture(texture_diffuse, TexCoords).a * material_alpha;
        if (a < alpha_threshold) discard;
    }
    // gl_FragDepth is auto-written by GL from gl_Position.z / gl_Position.w
    // unless we override it here, which we don't.
}
