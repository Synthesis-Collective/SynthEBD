#version 330 core

in vec3 v_worldNormal;
out vec4 FragColor;

uniform vec3 u_color;
uniform float u_shaded; // 0 = flat (unshaded line), 1 = lit (3D arrow)
// Constant output alpha. 1.0 for every historical caller (markers, measurement
// lines, light arrows); the section-clip plane visualization draws its fill at a
// low alpha so the model stays readable through it. Callers that never set it get
// the GL default 0.0, so GlRenderer pushes it explicitly on every debug-shader use.
uniform float u_alpha;

void main()
{
    if (u_shaded > 0.5)
    {
        vec3 N = normalize(v_worldNormal);
        // Two-sided fake sun for even visibility across orbit angles
        vec3 L1 = normalize(vec3(0.45, 0.80, 0.40));
        vec3 L2 = normalize(vec3(-0.30, 0.20, -0.60));
        float lamb = max(dot(N, L1), 0.0) * 0.8
                   + max(dot(N, L2), 0.0) * 0.35;
        float fresnel = pow(1.0 - abs(N.z), 2.0) * 0.25;
        float shade = clamp(0.35 + lamb + fresnel, 0.0, 1.4);
        FragColor = vec4(u_color * shade, u_alpha);
    }
    else
    {
        FragColor = vec4(u_color, u_alpha);
    }
}
