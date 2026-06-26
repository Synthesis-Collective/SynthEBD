#version 330 core

// Post-tonemap bloom for the character viewer (off by default; gated on the
// host "Bloom" + "Tone-mapping" toggles). Reproduces the soft highlight glow
// the Skyrim engine produces, which is a large part of why bright blonde hair
// reads as blonde in-game but brunette in a flat studio render.
//
// This runs on the already-tonemapped, display-encoded scene color (the main
// pass output, resolved out of the MSAA FBO into a single-sample texture by
// GlRenderer). It is a cheap LDR/post-tonemap bloom -- not physically HDR --
// but it gives the perceptual glow without disturbing the MSAA scene pass.
//
// One shader, three passes selected by u_pass:
//   0 = bright-pass: keep only pixels above a soft luminance knee
//   1 = separable Gaussian blur along u_direction (run H then V, twice)
//   2 = final scale for the additive composite back over the scene
//
// The fullscreen.vert oversized-triangle trick provides v_uv with no VBO.

in vec2 v_uv;
out vec4 fragColor;

uniform sampler2D u_tex;       // pass 0: scene; pass 1: prev blur; pass 2: blur
uniform int u_pass;
uniform vec2 u_direction;      // pass 1: texel-space blur step (texelSize * axis)
uniform float u_threshold;     // pass 0: luminance knee start
uniform float u_softKnee;      // pass 0: knee width
uniform float u_intensity;     // pass 2: composite gain

// 9-tap Gaussian (sigma ~2.0), symmetric weights for offsets 0..4.
const float W0 = 0.227027;
const float W1 = 0.194595;
const float W2 = 0.121622;
const float W3 = 0.054054;
const float W4 = 0.016216;

float luma(vec3 c)
{
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

void main()
{
    if (u_pass == 0) {
        // Bright-pass with a soft knee so the glow ramps in smoothly instead
        // of hard-clipping at the threshold. Pixels below the knee contribute
        // nothing; pixels above keep their full color, scaled by how far past
        // the knee they sit.
        vec3 c = texture(u_tex, v_uv).rgb;
        float l = luma(c);
        float knee = max(u_softKnee, 1e-4);
        float w = clamp((l - u_threshold) / knee, 0.0, 1.0);
        fragColor = vec4(c * w, 1.0);
    }
    else if (u_pass == 1) {
        // Separable Gaussian. Called once per axis; the host runs H then V
        // (two iterations) for a wide, soft glow.
        vec3 sum = texture(u_tex, v_uv).rgb * W0;
        sum += texture(u_tex, v_uv + u_direction * 1.0).rgb * W1;
        sum += texture(u_tex, v_uv - u_direction * 1.0).rgb * W1;
        sum += texture(u_tex, v_uv + u_direction * 2.0).rgb * W2;
        sum += texture(u_tex, v_uv - u_direction * 2.0).rgb * W2;
        sum += texture(u_tex, v_uv + u_direction * 3.0).rgb * W3;
        sum += texture(u_tex, v_uv - u_direction * 3.0).rgb * W3;
        sum += texture(u_tex, v_uv + u_direction * 4.0).rgb * W4;
        sum += texture(u_tex, v_uv - u_direction * 4.0).rgb * W4;
        fragColor = vec4(sum, 1.0);
    }
    else {
        // Composite scale. Drawn with additive blend (ONE, ONE) over the
        // scene, so this just hands back the blurred bright color times the
        // user intensity.
        vec3 c = texture(u_tex, v_uv).rgb;
        fragColor = vec4(c * u_intensity, 1.0);
    }
}
