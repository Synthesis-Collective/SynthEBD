#version 330 core

// Depth-aware (bilateral) box blur for the SSAO texture. The SSAO compute
// pass uses a 4x4 random-vector noise tile to rotate the sample kernel per
// pixel, breaking up banding that a fixed kernel would produce - but the
// downside is a high-frequency 4-pixel-period dot pattern in the raw AO
// output. This pass averages a 4x4 neighborhood to cancel the noise-tile
// period and leaves only the underlying low-frequency occlusion signal.
//
// The blur is BILATERAL: a neighbor only contributes when its reconstructed
// view-space depth is within u_depthThreshold of the center pixel. Without
// that guard a plain box blur averages across depth discontinuities, so the
// AO computed on the body in a beard's strand gaps would bleed back onto the
// strands (re-introducing the exact silhouette/tint that ssao.frag's
// occluder-thickness rejection removes). Rejecting cross-surface neighbors
// keeps each surface's AO to itself. The center pixel always passes, so the
// weight sum is never zero. Near a depth edge fewer neighbors contribute, so
// a little noise-tile residue can survive there - acceptable for the few
// boundary pixels, and far better than the bleed.

in vec2 v_uv;
out float fragOcclusion;

uniform sampler2D u_ssaoTex;
uniform sampler2D u_depthTex;
uniform mat4 u_invProjection;
uniform vec2 u_texelSize;
uniform float u_depthThreshold; // view-space units

float viewZ(vec2 uv)
{
    float d = texture(u_depthTex, uv).r;
    vec4 clip = vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4 view = u_invProjection * clip;
    return view.z / view.w;
}

void main()
{
    float centerZ = viewZ(v_uv);

    float result = 0.0;
    float wsum = 0.0;
    for (int x = -2; x < 2; x++) {
        for (int y = -2; y < 2; y++) {
            vec2 uv = v_uv + vec2(float(x), float(y)) * u_texelSize;
            // 1 when the neighbor is on the same surface (within the occluder
            // thickness in depth), 0 across a discontinuity.
            float w = step(abs(viewZ(uv) - centerZ), u_depthThreshold);
            result += texture(u_ssaoTex, uv).r * w;
            wsum += w;
        }
    }

    fragOcclusion = result / max(wsum, 1.0);
}
