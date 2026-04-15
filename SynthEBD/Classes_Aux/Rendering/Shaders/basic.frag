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
uniform sampler2D texture_detail;
uniform sampler2D texture_envmap;
uniform sampler2D texture_envmask;

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
uniform bool has_rim_lighting;
uniform bool has_vertex_colors;
uniform bool has_environment_map;
uniform bool has_env_mask;
uniform bool has_detail_map;
uniform bool is_eye;

// --- RENDERER TOGGLES ---
uniform bool use_alpha_test;

// --- MATERIAL PROPERTIES ---
uniform float alpha_threshold;
uniform float greyscaleToPaletteScale;
uniform vec3 tint_color;
uniform float materialGlossiness;
uniform float materialSpecularStrength;
uniform vec3 specularColor;
uniform float rimlightPower;
uniform float subsurfaceRolloff;
uniform vec3 emissiveColor;
uniform float emissiveMultiple;
uniform float envMapScale;
uniform float eyeCubemapScale;

// --- GENERAL UNIFORMS ---
uniform Light lights[MAX_LIGHTS];
uniform vec3 u_backlightColor;
uniform mat4 u_view;

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

    // Detail map overlay
    if (has_detail_map) {
        vec3 detailSample = texture(texture_detail, TexCoords).rgb;
        baseColor.rgb = mix(baseColor.rgb, baseColor.rgb * detailSample * 2.0, 0.3);
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

    // Eye meshes: invert normals (eyes are typically modeled inside-out in NIFs)
    if (is_eye) {
        normal_viewSpace = -normal_viewSpace;
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
                specular = specAmount * specMask * lightColor * specularColor * materialSpecularStrength;
            }

            // Backlight / rimlight (hair)
            vec3 backlight = vec3(0.0);
            if (has_hair_soft_lighting) {
                float rim = pow(1.0 - max(dot(viewDir, normal_viewSpace), 0.0), rimlightPower);
                backlight = rim * u_backlightColor * lightColor * diffuseStrength * baseColor.rgb;
            }

            // Rim lighting (non-hair, e.g. skin translucency)
            vec3 rimlight = vec3(0.0);
            if (has_rim_lighting) {
                float rim = pow(1.0 - max(dot(viewDir, normal_viewSpace), 0.0), rimlightPower);
                rimlight = rim * lightColor * baseColor.rgb;
            }

            // Subsurface scattering (skin)
            vec3 subsurface = vec3(0.0);
            if (has_skin_map) {
                float sss_mask = texture(texture_skin, TexCoords).r;
                vec3 sss_color = vec3(1.0, 0.3, 0.2);
                float wrap = dot(normal_viewSpace, lightDir) * 0.5 + 0.5;
                subsurface = lightColor * wrap * sss_color * sss_mask * subsurfaceRolloff;
            }

            finalColor += (diffuse + specular + subsurface + backlight + rimlight) * baseColor.rgb;
        }
    }

    // Post-lighting skin tint
    if (has_skin_map) {
        float skinVal = texture(texture_skin, TexCoords).r;
        finalColor += skinVal * vec3(1.0, 0.3, 0.2) * baseColor.rgb;
    }

    // --- 4. ENVIRONMENT MAPPING (spherical 2D) ---
    if (has_environment_map) {
        vec3 viewDir = normalize(-v_viewSpacePos);
        vec3 reflectDir = reflect(-viewDir, normal_viewSpace);
        // Spherical environment mapping: convert reflection vector to 2D UV
        // This maps a 3D reflection direction to a sphere map texture coordinate
        float m = 2.0 * sqrt(reflectDir.x * reflectDir.x + reflectDir.y * reflectDir.y +
                             (reflectDir.z + 1.0) * (reflectDir.z + 1.0));
        vec2 envUV = vec2(reflectDir.x / m + 0.5, reflectDir.y / m + 0.5);
        vec3 envColor = texture(texture_envmap, envUV).rgb;
        float envMask = has_env_mask ? texture(texture_envmask, TexCoords).r : 1.0;
        float scale = is_eye ? eyeCubemapScale : envMapScale;
        finalColor += envColor * envMask * scale;
    }

    // --- 5. EMISSIVE ---
    if (has_emissive) {
        finalColor += emissiveColor * emissiveMultiple;
    }

    FragColor = vec4(finalColor, baseColor.a);
}
