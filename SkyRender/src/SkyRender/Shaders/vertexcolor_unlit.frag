#version 330 core

// Per-vertex colour overlay pass. Unlit on purpose: the interpolated colour
// is the datum, so applying the fake-sun shade the debug pass uses would
// falsify the values the user is reading.
// Pure ASCII (see the shader ASCII-only rule in RENDERING_PIPELINE.md).

in vec3 v_color;
out vec4 FragColor;

void main()
{
    FragColor = vec4(v_color, 1.0);
}
