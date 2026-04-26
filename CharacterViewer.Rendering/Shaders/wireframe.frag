#version 330 core

// Wireframe overlay pass. Flat color; depth offset is applied on the C# side
// via glPolygonOffset so lines do not z-fight with the solid surface.
//
// ASCII-only (see GlShaderProgram class remarks for why).

out vec4 FragColor;

uniform vec3 u_color;

void main()
{
    FragColor = vec4(u_color, 1.0);
}
