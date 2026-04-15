#version 330 core
out vec4 FragColor;

in vec3 v_viewSpacePos;
in vec2 TexCoords;
in vec4 vertexColor;
in mat3 v_tangentToViewMatrix;
in mat3 v_modelToViewNormalMatrix;

#define MAX_LIGHTS 5

struct Light {
    int type; // 0:disabled, 1:ambient, 2:directional
    vec3 direction; // pre-transformed to view space
    vec3 color;
    float intensity;
};

// --- TEXTURE SAMPLERS ---
uniform sampler2D texture_diffuse;
uniform sampler2D texture_normal;
uniform sampler2D texture_skin;
uniform sampler2D texture_specular;
uniform sampler2D texture_face_tint;

// --- MATERIAL FLAGS ---
uniform bool has_normal_map;
uniform bool has_skin_map;
uniform bool has_specular;
uniform bool has_specular_map;
uniform bool has_face_tint_map;
uniform bool has_greyscale_to_palette;
uniform bool has_tint_color;
uniform bool has_emissive;
uniform bool is_model_space;
uniform bool has_hair_soft_lighting;
uniform bool has_soft_lighting;
uniform bool has_vertex_colors;

// --- RENDERER TOGGLES ---
uniform bool use_alpha_test;

// --- MATERIAL PROPERTIES ---
uniform float alpha_threshold;
uniform float greyscaleToPaletteScale;
uniform vec3 tint_color;
uniform float materialGlossiness;
uniform float materialSpecularStrength;
uniform float rimlightPower;
uniform float subsurfaceRolloff;

// --- GENERAL UNIFORMS ---
uniform Light lights[MAX_LIGHTS];
uniform vec3 u_backlightColor;

void main()
{
    // --- 1. BASE COLOR & ALPHA TEST ---
    vec4 baseColor = texture(texture_diffuse, TexCoords);

    if (has_vertex_colors) {
        baseColor.rgb *= vertexColor.rgb;
        baseColor.a *= vertexColor.a;
    }

    if (use_alpha_test && baseColor.a < alpha_threshold) {
        discard;
    }

    // Greyscale-to-palette (hair tinting)
    if (has_greyscale_to_palette) {
        baseColor.rgb = baseColor.rrr * tint_color * greyscaleToPaletteScale;
    } else if (has_tint_color) {
        baseColor.rgb *= tint_color;
    }

    // Face tint overlay
    if (has_face_tint_map) {
        vec4 tintSample = texture(texture_face_tint, TexCoords);
        baseColor.rgb = mix(baseColor.rgb, baseColor.rgb * tintSample.rgb, tintSample.a);
    }

    // --- 2. NORMAL CALCULATION ---
    vec3 normal_viewSpace;
    bool tbnIsValid = length(v_tangentToViewMatrix[0]) > 0.0;

    if (has_normal_map) {
        if (is_model_space) {
            vec3 normal_modelSpace = texture(texture_normal, TexCoords).rgb * 2.0 - 1.0;
            normal_modelSpace.g *= -1.0; // DirectX convention
            normal_viewSpace = normalize(v_modelToViewNormalMatrix * normal_modelSpace);
        }
        else if (tbnIsValid) {
            vec3 normal_tangentSpace = texture(texture_normal, TexCoords).rgb * 2.0 - 1.0;
            normal_tangentSpace.g *= -1.0;
            normal_viewSpace = normalize(v_tangentToViewMatrix * normal_tangentSpace);
        }
        else {
            normal_viewSpace = normalize(v_tangentToViewMatrix[2]);
        }
    } else {
        if (tbnIsValid) {
            normal_viewSpace = normalize(v_tangentToViewMatrix[2]);
        } else {
            normal_viewSpace = normalize(v_modelToViewNormalMatrix * vec3(0.0, 0.0, 1.0));
        }
    }

    // --- 3. DYNAMIC LIGHTING ---
    vec3 finalColor = vec3(0.0);

    for (int i = 0; i < MAX_LIGHTS; i++) {
        if (lights[i].type == 0) continue;
        vec3 lightColor = lights[i].color * lights[i].intensity;

        if (lights[i].type == 1) {
            // Ambient
            finalColor += lightColor * baseColor.rgb;
        }
        else if (lights[i].type == 2) {
            // Directional
            vec3 lightDir = normalize(lights[i].direction);
            vec3 viewDir = normalize(-v_viewSpacePos);

            // Diffuse
            float NdotL = dot(normal_viewSpace, lightDir);
            float diffuseStrength;
            if (has_soft_lighting) {
                diffuseStrength = NdotL * 0.5 + 0.5; // wrap lighting
            } else {
                diffuseStrength = max(NdotL, 0.0);
            }
            vec3 diffuse = diffuseStrength * lightColor;

            // Specular (Blinn-Phong)
            vec3 specular = vec3(0.0);
            if (has_specular) {
                float specMask = 1.0;
                if (has_specular_map) {
                    specMask = texture(texture_specular, TexCoords).r;
                }
                vec3 halfwayDir = normalize(lightDir + viewDir);
                float specAmount = pow(max(dot(normal_viewSpace, halfwayDir), 0.0), materialGlossiness);
                specular = specAmount * specMask * lightColor * materialSpecularStrength;
            }

            // Backlight / rimlight (hair)
            vec3 backlight = vec3(0.0);
            if (has_hair_soft_lighting) {
                float rim = pow(1.0 - max(dot(viewDir, normal_viewSpace), 0.0), rimlightPower);
                backlight = rim * u_backlightColor * lightColor * diffuseStrength * baseColor.rgb;
            }

            // Subsurface scattering (skin)
            vec3 subsurface = vec3(0.0);
            if (has_skin_map) {
                float sss_mask = texture(texture_skin, TexCoords).r;
                vec3 sss_color = vec3(1.0, 0.3, 0.2);
                float wrap = dot(normal_viewSpace, lightDir) * 0.5 + 0.5;
                subsurface = lightColor * wrap * sss_color * sss_mask * subsurfaceRolloff;
            }

            finalColor += (diffuse + specular + subsurface + backlight) * baseColor.rgb;
        }
    }

    // Post-lighting skin tint
    if (has_skin_map) {
        float skinVal = texture(texture_skin, TexCoords).r;
        finalColor += skinVal * vec3(1.0, 0.3, 0.2) * baseColor.rgb;
    }

    FragColor = vec4(finalColor, baseColor.a);
}
