#version 330 core

// Slider-morph heatmap pass. Unlit on purpose: the interpolated per-vertex
// color IS the datum (|morph delta| -> cold/hot ramp), so applying the fake-sun
// shade the debug pass uses would falsify the magnitudes the user is reading.
// Pure ASCII (see the shader ASCII-only rule in RENDERING_PIPELINE.md).

in vec3 v_color;
out vec4 FragColor;

void main()
{
    FragColor = vec4(v_color, 1.0);
}
