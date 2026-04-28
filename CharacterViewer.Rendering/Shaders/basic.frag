#version 330 core
out vec4 FragColor;

in vec3 v_viewSpacePos;
in vec2 TexCoords;
in vec4 vertexColor;
in mat3 v_tangentToViewMatrix;
in mat3 v_modelToViewNormalMatrix;
// TEMP DEBUG: world-space normal for DEBUG_VIZ_WORLD_NORMAL branch.
in vec3 v_worldNormal;

#define MAX_LIGHTS 5

// TEMP DEBUG: when true, disables every view-dependent lighting term (specular,
// env reflection, hair backlight, skin rim) and the soft-wrap lighting so the
// remaining pure clamped NdotL diffuse term unambiguously shows which side of
// the mesh each directional light illuminates. Flip to false to restore normal
// shading. Dead branches are eliminated by the GLSL compiler since the value is
// a const, so there's no runtime cost.
const bool DEBUG_DIFFUSE_ONLY = false;

// TEMP DEBUG: when true, ignores all lighting and outputs the mesh's world-space
// normal as RGB color (normal * 0.5 + 0.5). Used to diagnose a 90-degree offset
// between the light arrow and the lit face of the mesh. Expected colors if the
// mesh is correctly converted to Y-up world space:
//   chest (faces -Z)          -> yellowish  (0.5, 0.5, 0.0)
//   back  (faces +Z)          -> blue-cyan  (0.5, 0.5, 1.0)
//   head top (faces +Y)       -> bright green (0.5, 1.0, 0.5)
//   feet soles (faces -Y)     -> purple     (0.5, 0.0, 0.5)
//   right side (faces +X)     -> pink-red   (1.0, 0.5, 0.5)
//   left side  (faces -X)     -> teal       (0.0, 0.5, 0.5)
// If the chest appears purple or the feet appear yellow, normals are rotated
// R_X(+90) extra (i.e. still in NIF Z-up space instead of Y-up). Set to false
// to restore normal shading.
const bool DEBUG_VIZ_WORLD_NORMAL = false;

// TEMP DEBUG: when true, outputs the MSN-texture-sampled normal (AFTER the DX
// G-flip and Z-up->Y-up swizzle) transformed back to world space as an RGB
// color. For MSN meshes (is_model_space=true) with a correctly-swizzled MSN
// path, this should show the same colors as DEBUG_VIZ_WORLD_NORMAL does on
// non-MSN meshes (chest=yellow, head top=green, etc.). For non-MSN meshes this
// branch just falls back to the vertex normal. Used to isolate whether the MSN
// swizzle in the fragment shader or the vertex-normal pipeline is the source
// of the 90-degree lighting offset.
const bool DEBUG_VIZ_MSN_NORMAL = false;

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
uniform bool u_enableToneMapping;

// --- PER-SHAPE TEXTURE VISIBILITY TOGGLES ---
uniform bool u_enableDiffuse;
uniform bool u_enableNormal;
uniform bool u_enableSkin;
uniform bool u_enableSpecular;
uniform bool u_enableFaceTint;
uniform bool u_enableDetail;
uniform bool u_enableEnvMap;
uniform bool u_enableEmissive;
uniform bool u_enableTintColor;

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

// Photoshop-style overlay blend (matches NifSkope / Bethesda engine)
float overlayBlend(float b, float l)
{
    if (b < 0.5)
        return 2.0 * b * l;
    else
        return 1.0 - 2.0 * (1.0 - l) * (1.0 - b);
}

vec3 overlayBlend(vec3 b, vec3 l)
{
    return vec3(overlayBlend(b.r, l.r), overlayBlend(b.g, l.g), overlayBlend(b.b, l.b));
}

void main()
{
    // TEMP DEBUG: short-circuit to visualize world-space mesh normals as color.
    if (DEBUG_VIZ_WORLD_NORMAL) {
        vec3 n = normalize(v_worldNormal);
        FragColor = vec4(n * 0.5 + 0.5, 1.0);
        return;
    }

    // TEMP DEBUG: short-circuit to visualize the MSN-sampled normal (post
    // G-flip, post current swizzle) as color, treated as if it were already
    // in Y-up world space (valid since u_model is identity). For MSN meshes
    // this should produce the same colors as DEBUG_VIZ_WORLD_NORMAL IF the
    // current swizzle on line 178 is correct. If colors are rotated 90deg
    // (e.g. chest green instead of yellow), the swizzle is wrong and the
    // MSN texture is stored in Y-up, not NIF Z-up. Non-MSN meshes fall
    // back to the vertex normal so the viewport stays visually coherent.
    if (DEBUG_VIZ_MSN_NORMAL) {
        vec3 n;
        if (is_model_space && has_normal_map && u_enableNormal) {
            vec3 normal_modelSpace = texture(texture_normal, TexCoords).rgb * 2.0 - 1.0;
            // FIXED: Bethesda MSN is stored in Y-up local model space (character
            // faces +Z_local), not NIF Z-up with DX Y-flip. Our viewer uses
            // Y-up world space with character facing -Z_world, so flip Z.
            // No DX G-flip for MSN.
            normal_modelSpace = vec3(normal_modelSpace.x, normal_modelSpace.y, -normal_modelSpace.z);
            n = normalize(normal_modelSpace);
        } else {
            n = normalize(v_worldNormal);
        }
        FragColor = vec4(n * 0.5 + 0.5, 1.0);
        return;
    }

    // --- 1. BASE COLOR & ALPHA TEST ---
    vec4 baseColor;
    if (u_enableDiffuse) {
        baseColor = texture(texture_diffuse, TexCoords);
    } else {
        baseColor = vec4(0.8, 0.8, 0.8, 1.0); // neutral gray fallback
    }

    if (has_vertex_colors) {
        baseColor.rgb *= vertexColor.rgb;
        baseColor.a *= vertexColor.a;
    }

    if (use_alpha_test && baseColor.a < alpha_threshold) {
        discard;
    }

    // Greyscale-to-palette (hair tinting)
    if (has_greyscale_to_palette && u_enableDiffuse) {
        baseColor.rgb = baseColor.rrr * tint_color * greyscaleToPaletteScale;
    } else if (has_tint_color && u_enableTintColor) {
        baseColor.rgb *= tint_color;
    }

    // Detail map overlay (applied before face tint, matching NifSkope order)
    if (has_detail_map && u_enableDetail) {
        vec3 detailSample = texture(texture_detail, TexCoords).rgb;
        baseColor.rgb = overlayBlend(baseColor.rgb, detailSample);
    }

    // Face tint overlay (RGB only - alpha channel is not used)
    if (has_face_tint_map && u_enableFaceTint) {
        vec3 tintSample = texture(texture_face_tint, TexCoords).rgb;
        baseColor.rgb = overlayBlend(baseColor.rgb, tintSample);
    }

    // --- 2. NORMAL CALCULATION ---
    vec3 normal_viewSpace;
    bool tbnIsValid = length(v_tangentToViewMatrix[0]) > 0.0;

    if (has_normal_map && u_enableNormal) {
        if (is_model_space) {
            vec3 normal_modelSpace = texture(texture_normal, TexCoords).rgb * 2.0 - 1.0;
            // Bethesda MSN textures are stored in Y-up local model space with
            // the character's forward direction as +Z_local. Our viewer uses
            // Y-up world space with character facing -Z_world, so flip Z to
            // align. Unlike tangent-space normal maps, MSN does NOT use the
            // DirectX G-flip convention because MSN is model-space not
            // tangent-space. Empirical verification: raw texture sample at
            // chest is (0.5, 0.5, 1.0) -> u = (0,0,1); after flipping Z we
            // get (0,0,-1) which correctly faces the camera at Az=180.
            normal_modelSpace = vec3(normal_modelSpace.x, normal_modelSpace.y, -normal_modelSpace.z);
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
            if (DEBUG_DIFFUSE_ONLY) {
                diffuseStrength = max(NdotL, 0.0);
            } else if (has_soft_lighting) {
                diffuseStrength = NdotL * 0.5 + 0.5; // wrap lighting
            } else {
                diffuseStrength = max(NdotL, 0.0);
            }
            vec3 diffuse = diffuseStrength * lightColor;

            // Specular (Blinn-Phong)
            vec3 specular = vec3(0.0);
            if (!DEBUG_DIFFUSE_ONLY && has_specular && u_enableSpecular) {
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
            if (!DEBUG_DIFFUSE_ONLY && has_hair_soft_lighting) {
                float rim = pow(1.0 - max(dot(viewDir, normal_viewSpace), 0.0), rimlightPower);
                backlight = rim * u_backlightColor * lightColor * diffuseStrength * baseColor.rgb;
            }

            // Rim lighting (non-hair, e.g. skin translucency)
            vec3 rimlight = vec3(0.0);
            if (!DEBUG_DIFFUSE_ONLY && has_rim_lighting) {
                float rim = pow(1.0 - max(dot(viewDir, normal_viewSpace), 0.0), rimlightPower);
                rimlight = rim * lightColor * baseColor.rgb;
            }

            // Subsurface scattering (skin) -- NdotL-based so it tracks the
            // diffuse term, safe to keep on in debug mode. The SSS color
            // is the warm flesh tint (1.0, 0.3, 0.2) blended halfway with
            // the local diffuse so dark-skinned NPCs don't get unrealistic
            // bright-red transmission while still getting the subsurface
            // warmth in lit-from-behind regions (cheeks, ears, nose).
            vec3 subsurface = vec3(0.0);
            if (has_skin_map && u_enableSkin) {
                float sss_mask = texture(texture_skin, TexCoords).r;
                vec3 sss_color = mix(vec3(1.0, 0.3, 0.2), baseColor.rgb, 0.5);
                float wrap = dot(normal_viewSpace, lightDir) * 0.5 + 0.5;
                subsurface = lightColor * wrap * sss_color * sss_mask * subsurfaceRolloff;
            }

            finalColor += (diffuse + specular + subsurface + backlight + rimlight) * baseColor.rgb;
        }
    }

    // --- 4. ENVIRONMENT MAPPING (spherical 2D) ---
    // TEMP DEBUG: disabled so reflections don't disguise the unlit side.
    if (!DEBUG_DIFFUSE_ONLY && has_environment_map && u_enableEnvMap) {
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
    if (has_emissive && u_enableEmissive) {
        finalColor += emissiveColor * emissiveMultiple;
    }

    // --- 6. TONE-MAPPING & COLOR GRADE (CharacterViewer.Rendering 2.5.9+) ---
    // ACES filmic approximation (Narkowicz 2015) compresses HDR highlights,
    // adds a soft toe in the shadows, and produces the warm shoulder that
    // makes the output read as a portrait rather than a flat linear render.
    // Pairs with FRAMEBUFFER_SRGB on the host side: this stage outputs
    // linear values, the framebuffer gamma-encodes on write. Without
    // FRAMEBUFFER_SRGB the output displays too dark.
    //
    // Mild saturation boost (1.10x) afterwards gives skin tones a touch
    // more warmth without looking gaudy. Skip both when the toggle is
    // off so the legacy linear pipeline is reproducible bit-for-bit.
    if (u_enableToneMapping) {
        // Slight exposure pull-down: the lit color sits ~1.0-1.5 in linear
        // space typically; 0.6 keeps the tone-curve toe in a useful range.
        vec3 c = finalColor * 0.6;
        c = (c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14);
        float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
        c = mix(vec3(lum), c, 1.10);
        finalColor = clamp(c, 0.0, 1.0);
    }

    FragColor = vec4(finalColor, baseColor.a);
}
