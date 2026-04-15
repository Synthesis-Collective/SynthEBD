#version 330 core

// === INPUTS (Per-Vertex Data) ===
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoords;
layout (location = 3) in vec4 aColor;
layout (location = 4) in vec3 aTangent;
layout (location = 5) in vec3 aBitangent;

// === OUTPUTS ===
out vec3 v_viewSpacePos;
out vec2 TexCoords;
out vec4 vertexColor;
out mat3 v_tangentToViewMatrix;
out mat3 v_modelToViewNormalMatrix;

// === UNIFORMS ===
uniform mat4 u_model;
uniform mat4 u_view;
uniform mat4 u_projection;

void main()
{
    vec4 pos_worldSpace = u_model * vec4(aPos, 1.0);

    gl_Position = u_projection * u_view * pos_worldSpace;
    v_viewSpacePos = vec3(u_view * pos_worldSpace);

    // Normal matrix: model-to-view for MSN path
    mat3 normalMatrix_modelToWorld = mat3(transpose(inverse(u_model)));
    mat3 normalMatrix_modelToView = mat3(u_view) * normalMatrix_modelToWorld;
    v_modelToViewNormalMatrix = normalMatrix_modelToView;

    // TBN matrix for tangent-space normal maps
    vec3 T_viewSpace = normalize(normalMatrix_modelToView * aTangent);
    vec3 B_viewSpace = normalize(normalMatrix_modelToView * aBitangent);
    vec3 N_viewSpace = normalize(normalMatrix_modelToView * aNormal);
    v_tangentToViewMatrix = mat3(T_viewSpace, B_viewSpace, N_viewSpace);

    TexCoords = aTexCoords;
    vertexColor = aColor;
}
