#version 330 core

// Full-screen-quad vertex shader for post-process passes (SSAO + future
// bloom / color grading). The host binds an empty VAO and issues
// glDrawArrays(GL_TRIANGLES, 0, 3); this shader generates the three
// vertices of a single oversized triangle that covers the screen using
// gl_VertexID, avoiding any vertex buffer setup. The "oversized
// triangle" trick (vs. a 2-triangle quad) avoids a redundant diagonal
// fragment-shader invocation along the seam.

out vec2 v_uv;

void main()
{
    // gl_VertexID 0,1,2 -> NDC (-1,-1), (3,-1), (-1,3); UV (0,0), (2,0), (0,2).
    // The portion outside the [0,1] UV range is clipped away because
    // gl_Position is also outside the [-1,1] NDC clip box.
    vec2 ndc = vec2((gl_VertexID == 1) ? 3.0 : -1.0,
                    (gl_VertexID == 2) ? 3.0 : -1.0);
    v_uv = ndc * 0.5 + 0.5;
    gl_Position = vec4(ndc, 0.0, 1.0);
}
