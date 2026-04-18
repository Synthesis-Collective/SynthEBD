#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;

uniform mat4 u_view;
uniform mat4 u_projection;

out vec3 v_worldNormal;

void main()
{
    v_worldNormal = aNormal;
    gl_Position = u_projection * u_view * vec4(aPos, 1.0);
}
