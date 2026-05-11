#version 330 core

// Screen-space ambient occlusion post-process. Reads the camera's depth
// texture and a view-space normal G-buffer from the prepass, then samples
// a hemispheric kernel of 16 offsets per pixel to estimate how occluded
// each fragment is by nearby geometry. Outputs a single-channel R8
// occlusion factor in [0,1] (1 = fully lit, 0 = fully occluded) which
// basic.frag multiplies into its diffuse term.
//
// Algorithm: classic Crytek SSAO. Normals come from the prepass G-buffer
// (interpolated vertex normals transformed to view space), NOT from depth
// gradients - cross(dFdx, dFdy) yields a flat geometric face normal that
// is constant inside each triangle, which makes the triangulation pop on
// smooth surfaces (collarbone, neck, cheeks) at any non-trivial radius.
// The noise texture rotates the kernel per-pixel to break up banding
// from a fixed sample pattern; ssao_blur.frag then averages a 4x4
// neighborhood to cancel the noise-tile period.

in vec2 v_uv;
out float fragOcclusion;

uniform sampler2D u_depthTex;
uniform sampler2D u_normalTex;
uniform sampler2D u_noiseTex;
uniform mat4 u_projection;
uniform mat4 u_invProjection;
uniform vec3 u_kernel[16];
uniform vec2 u_noiseScale;
uniform float u_radius;
uniform float u_bias;
uniform float u_intensity;

vec3 viewPosFromDepth(vec2 uv)
{
    float depth = texture(u_depthTex, uv).r;
    // Map [0,1] window-space depth back to NDC, then to view space.
    vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 view = u_invProjection * clip;
    return view.xyz / view.w;
}

void main()
{
    vec3 fragPos = viewPosFromDepth(v_uv);

    // If we're at the far plane (no geometry written), skip - return
    // fully-lit so the background isn't darkened.
    float centerDepth = texture(u_depthTex, v_uv).r;
    if (centerDepth >= 0.9999) {
        fragOcclusion = 1.0;
        return;
    }

    // Sample the smooth view-space normal from the prepass G-buffer
    // (encoded as *0.5+0.5 into an unsigned RGB8 texture).
    vec3 normal = normalize(texture(u_normalTex, v_uv).xyz * 2.0 - 1.0);

    // Tile the noise texture across the screen at 4x4-pixel resolution
    // so neighboring fragments use different rotations of the kernel.
    vec3 randomVec = texture(u_noiseTex, v_uv * u_noiseScale).xyz;

    // Build TBN to orient the hemisphere with the surface normal.
    vec3 tangent = normalize(randomVec - normal * dot(randomVec, normal));
    vec3 bitangent = cross(normal, tangent);
    mat3 TBN = mat3(tangent, bitangent, normal);

    float occlusion = 0.0;
    for (int i = 0; i < 16; i++) {
        // Hemispheric kernel sample, oriented with TBN.
        vec3 samplePos = fragPos + (TBN * u_kernel[i]) * u_radius;

        // Project sample position to screen space.
        vec4 offset = u_projection * vec4(samplePos, 1.0);
        offset.xyz /= offset.w;
        offset.xyz = offset.xyz * 0.5 + 0.5;

        // Read the actual scene depth at the projected screen position.
        float sampleSceneZ = viewPosFromDepth(offset.xy).z;

        // Range check: only count occluders that are within u_radius of
        // the fragment in view space - prevents distant geometry behind
        // the head from contributing fake occlusion to the face.
        float rangeCheck = smoothstep(0.0, 1.0, u_radius / abs(fragPos.z - sampleSceneZ));

        // Bias: sample only counts if it's closer to the camera than
        // the test position by more than u_bias world units.
        // (View-space Z is negative going away from camera.)
        occlusion += (sampleSceneZ >= samplePos.z + u_bias ? 1.0 : 0.0) * rangeCheck;
    }
    occlusion = 1.0 - (occlusion / 16.0);

    // Power curve sharpens the AO signal so the visible darkening is in
    // deep crevices rather than smoothly darkening every surface. The
    // exponent is host-tunable via u_intensity - higher values mean
    // crevice-only darkening; lower values mean a more uniform ambient
    // dimming.
    fragOcclusion = pow(occlusion, u_intensity);
}
