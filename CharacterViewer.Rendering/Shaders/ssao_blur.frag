#version 330 core

// Box-blur post-pass for the SSAO texture. The SSAO compute pass uses a
// 4x4 random-vector noise tile to rotate the sample kernel per pixel,
// breaking up banding that a fixed kernel would produce - but the
// downside is a high-frequency 4-pixel-period dot pattern in the raw
// AO output. This pass averages a 4x4 neighborhood of AO samples per
// pixel, which exactly cancels out the noise-tile period and leaves
// only the underlying low-frequency occlusion signal.
//
// Single-pass 4x4 box blur (16 fetches per pixel) rather than two
// separable 1D passes - SSAO is single-channel R8 so 16 fetches is
// cheap compared to setting up a second FBO + intermediate texture.

in vec2 v_uv;
out float fragOcclusion;

uniform sampler2D u_ssaoTex;
uniform vec2 u_texelSize;

void main()
{
    float result = 0.0;
    for (int x = -2; x < 2; x++) {
        for (int y = -2; y < 2; y++) {
            vec2 off = vec2(float(x), float(y)) * u_texelSize;
            result += texture(u_ssaoTex, v_uv + off).r;
        }
    }
    fragOcclusion = result / 16.0;
}
