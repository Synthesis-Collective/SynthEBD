using System.Collections.Generic;
using System.IO;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Given an NPC FormKey + LinkCache, resolves all mesh file paths needed
/// to render the NPC (body, hands, feet, head).
/// </summary>
public class NpcMeshResolver
{
    private readonly Logger _logger;

    public NpcMeshResolver(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolved mesh paths for an NPC, one per body region.
    /// Paths are game-relative (e.g. "meshes/actors/character/...").
    /// </summary>
    public class NpcMeshPaths
    {
        public string? BodyMeshPath { get; init; }
        public string? HandsMeshPath { get; init; }
        public string? FeetMeshPath { get; init; }
        public string? HeadMeshPath { get; init; }
        public Gender Gender { get; init; }

        /// <summary>
        /// The NPC's race-specific skeleton NIF path (Data-relative), resolved from
        /// Race.SkeletalModel. Used for CPU-side bone-weight skinning to close the
        /// neck gap between head and body meshes.
        /// E.g. "meshes\actors\character\character assets female\skeleton_female.nif"
        /// </summary>
        public string? SkeletonPath { get; init; }

        /// <summary>
        /// Maps body part name ("Body", "Hands", "Feet", "Head") to a human-readable
        /// string describing the record traversal chain used to resolve its mesh path.
        /// E.g. "NPC:Astrid → WornArmor:000D2B5A → Armature(Body):000D2B5B → Female → femalebody_1.nif"
        /// </summary>
        public Dictionary<string, string> ResolutionChains { get; init; } = new();

        /// <summary>
        /// Texture paths from ARMA.SkinTexture (TXST records), keyed by body part
        /// ("Body", "Hands", "Feet") then by texture slot index (0=Diffuse, 1=Normal, etc.).
        /// These override the NIF's embedded BSShaderTextureSet paths at runtime.
        /// Head textures are NOT included — FaceGen NIF paths are ground truth for heads.
        /// </summary>
        public Dictionary<string, Dictionary<int, string>> TxstTextures { get; init; } = new();

        /// <summary>
        /// The NPC's FaceTint DDS path (Data-relative), constructed from the NPC's FormKey.
        /// Used for CPU-side blending onto the head diffuse texture.
        /// </summary>
        public string? FaceTintPath { get; init; }

        /// <summary>
        /// The NPC's QNAM TextureLighting color (RGB, 0-1 range).
        /// Applied as a skin tint to body/hands/feet meshes that use ST_SkinTint shader type.
        /// Null if the NPC has no TextureLighting set.
        /// </summary>
        public (float R, float G, float B)? TextureLightingColor { get; init; }
    }

    /// <summary>
    /// Resolves mesh paths for the given NPC by walking:
    ///   NPC → WornArmor → Armature (filtered by BipedObjectFlag) → WorldModel
    /// Falls back to Race.Skin if the NPC has no WornArmor.
    /// Head mesh is the FaceGen NIF constructed from the NPC's FormKey.
    /// </summary>
    public NpcMeshPaths? ResolveMeshPaths(FormKey npcFormKey, ILinkCache linkCache)
    {
        if (!linkCache.TryResolve<INpcGetter>(npcFormKey, out var npcGetter))
        {
            _logger.LogError("CharacterViewer: Could not resolve NPC " + npcFormKey);
            return null;
        }

        var gender = GetGender(npcGetter);
        string npcName = npcGetter.Name?.String ?? npcGetter.EditorID ?? npcFormKey.ToString();
        _logger.LogMessage("CharacterViewer: Resolving NPC " + npcName + " (" + npcFormKey + ")");

        FormKey? npcRaceKey = (npcGetter.Race != null && !npcGetter.Race.IsNull) ? npcGetter.Race.FormKey : null;

        // Resolve the armor providing skin meshes (WornArmor, or Race.Skin fallback)
        var (armorGetter, armorSource) = ResolveWornArmor(npcGetter, linkCache, npcName);

        string? bodyPath = null;
        string? handsPath = null;
        string? feetPath = null;
        var chains = new Dictionary<string, string>();
        var txstTextures = new Dictionary<string, Dictionary<int, string>>();
        string genderLabel = gender == Gender.Female ? "Female" : "Male";

        if (armorGetter?.Armature != null)
        {
            foreach (var armaLink in armorGetter.Armature)
            {
                if (!linkCache.TryResolve<IArmorAddonGetter>(armaLink.FormKey, out var armaGetter))
                {
                    continue;
                }

                if (armaGetter.BodyTemplate == null)
                {
                    continue;
                }

                // Skip armatures not applicable to this NPC's race. The game engine
                // matches ARMA.Race or any of ARMA.AdditionalRaces against the actor's
                // race (TESObjectARMA::IsValidRace). DefaultRace is NOT a wildcard — it
                // is a specific FormKey that vanilla ARMAs use, and the actor-race match
                // works because vanilla races list DefaultRace in AdditionalRaces (or
                // inversely, AdditionalRaces enumerates the compatible races explicitly).
                if (!IsArmatureForRace(armaGetter, npcRaceKey))
                {
                    _logger.LogMessage("CharacterViewer: Skipping Armature " + armaLink.FormKey +
                        " — race " + (npcRaceKey?.ToString() ?? "(none)") + " not in ARMA.Race/AdditionalRaces");
                    continue;
                }

                var flags = armaGetter.BodyTemplate.FirstPersonFlags;
                string? meshPath = GetWorldModelPath(armaGetter, gender);

                if (meshPath == null)
                {
                    continue;
                }

                string meshFileName = Path.GetFileName(meshPath);

                // Resolve TXST texture paths from ARMA.SkinTexture for this armature
                var txstPaths = ResolveTxstTextures(armaGetter, gender, linkCache, armaLink.FormKey);

                if (bodyPath == null && flags.HasFlag(BipedObjectFlag.Body))
                {
                    bodyPath = meshPath;
                    chains["Body"] = npcName + " → " + armorSource + " → Armature(Body):" + armaLink.FormKey +
                        " → " + genderLabel + " → " + meshFileName;
                    _logger.LogMessage("CharacterViewer: Armature[Body]=" + armaLink.FormKey + ", WorldModel=" + meshPath);
                    if (txstPaths.Count > 0) txstTextures["Body"] = txstPaths;
                }

                if (handsPath == null && flags.HasFlag(BipedObjectFlag.Hands))
                {
                    handsPath = meshPath;
                    chains["Hands"] = npcName + " → " + armorSource + " → Armature(Hands):" + armaLink.FormKey +
                        " → " + genderLabel + " → " + meshFileName;
                    _logger.LogMessage("CharacterViewer: Armature[Hands]=" + armaLink.FormKey + ", WorldModel=" + meshPath);
                    if (txstPaths.Count > 0) txstTextures["Hands"] = txstPaths;
                }

                if (feetPath == null && flags.HasFlag(BipedObjectFlag.Feet))
                {
                    feetPath = meshPath;
                    chains["Feet"] = npcName + " → " + armorSource + " → Armature(Feet):" + armaLink.FormKey +
                        " → " + genderLabel + " → " + meshFileName;
                    _logger.LogMessage("CharacterViewer: Armature[Feet]=" + armaLink.FormKey + ", WorldModel=" + meshPath);
                    if (txstPaths.Count > 0) txstTextures["Feet"] = txstPaths;
                }
            }
        }

        // FaceGen NIF: meshes/actors/character/FaceGenData/FaceGeom/{plugin}/{formID}.nif
        string headPath = BuildFaceGenPath(npcFormKey);
        chains["Head"] = npcName + " → FaceGen → " + Path.GetFileName(headPath);
        _logger.LogMessage("CharacterViewer: FaceGen head mesh=" + headPath);

        // FaceTint DDS: textures/actors/character/FaceGenData/FaceTint/{plugin}/{formID}.dds
        string faceTintPath = BuildFaceTintPath(npcFormKey);

        // Skeleton NIF: resolve from Race.SkeletalModel for CPU-side skinning
        string? skeletonPath = ResolveSkeletonPath(npcGetter, gender, linkCache);

        // QNAM TextureLighting: the NPC's skin tint color applied to body at runtime
        (float R, float G, float B)? textureLightingColor = null;
        var qnam = npcGetter.TextureLighting;
        if (qnam != null)
        {
            textureLightingColor = (qnam.Value.R / 255f, qnam.Value.G / 255f, qnam.Value.B / 255f);
            _logger.LogMessage("CharacterViewer: TextureLighting (QNAM)=RGB(" +
                qnam.Value.R + ", " + qnam.Value.G + ", " + qnam.Value.B + ")");
        }

        return new NpcMeshPaths
        {
            BodyMeshPath = bodyPath,
            HandsMeshPath = handsPath,
            FeetMeshPath = feetPath,
            HeadMeshPath = headPath,
            Gender = gender,
            ResolutionChains = chains,
            TxstTextures = txstTextures,
            FaceTintPath = faceTintPath,
            SkeletonPath = skeletonPath,
            TextureLightingColor = textureLightingColor
        };
    }

    private (IArmorGetter? armor, string source) ResolveWornArmor(INpcGetter npcGetter, ILinkCache linkCache, string npcName)
    {
        // Primary: NPC.WornArmor
        if (npcGetter.WornArmor != null && !npcGetter.WornArmor.IsNull &&
            linkCache.TryResolve<IArmorGetter>(npcGetter.WornArmor.FormKey, out var armorGetter))
        {
            _logger.LogMessage("CharacterViewer: WornArmor=" + npcGetter.WornArmor.FormKey);
            return (armorGetter, "WornArmor:" + npcGetter.WornArmor.FormKey);
        }

        // Fallback: Race.Skin
        if (npcGetter.Race != null && !npcGetter.Race.IsNull &&
            linkCache.TryResolve<IRaceGetter>(npcGetter.Race.FormKey, out var raceGetter) &&
            raceGetter.Skin != null && !raceGetter.Skin.IsNull &&
            linkCache.TryResolve<IArmorGetter>(raceGetter.Skin.FormKey, out var raceSkinArmor))
        {
            _logger.LogMessage("CharacterViewer: No WornArmor for " + npcName + ", falling back to Race.Skin=" + raceGetter.Skin.FormKey);
            return (raceSkinArmor, "Race.Skin:" + raceGetter.Skin.FormKey);
        }

        _logger.LogMessage("CharacterViewer: No WornArmor or Race.Skin found for " + npcName);
        return (null, "(none)");
    }

    /// <summary>
    /// Returns true if the given armature is applicable to the NPC's race, per
    /// the game's TESObjectARMA::IsValidRace logic: ARMA.Race must equal the NPC's
    /// race, or the race must appear in ARMA.AdditionalRaces. If the NPC has no
    /// resolvable race, we allow the armature through rather than skipping everything.
    /// </summary>
    private static bool IsArmatureForRace(IArmorAddonGetter armaGetter, FormKey? npcRaceKey)
    {
        if (npcRaceKey == null) return true;

        if (armaGetter.Race != null && !armaGetter.Race.IsNull &&
            armaGetter.Race.FormKey.Equals(npcRaceKey.Value))
        {
            return true;
        }

        if (armaGetter.AdditionalRaces != null)
        {
            foreach (var addRace in armaGetter.AdditionalRaces)
            {
                if (!addRace.IsNull && addRace.FormKey.Equals(npcRaceKey.Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? GetWorldModelPath(IArmorAddonGetter armaGetter, Gender gender)
    {
        if (armaGetter.WorldModel == null)
        {
            return null;
        }

        var model = gender == Gender.Female
            ? armaGetter.WorldModel.Female
            : armaGetter.WorldModel.Male;

        if (model?.File == null)
        {
            return null;
        }

        string path = model.File.GivenPath;
        if (string.IsNullOrWhiteSpace(path)) return null;

        // WorldModel paths in NIF records are relative to Data\meshes\ but
        // GameAssetResolver expects paths relative to Data\, so prepend "meshes\".
        if (!path.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase))
        {
            path = "meshes\\" + path;
        }

        return path;
    }

    /// <summary>
    /// Resolves texture paths from an ARMA's SkinTexture (TXST record) for the given gender.
    /// Returns a dictionary of texture slot index → game-relative texture path.
    /// </summary>
    private Dictionary<int, string> ResolveTxstTextures(
        IArmorAddonGetter armaGetter, Gender gender, ILinkCache linkCache, FormKey armaFormKey)
    {
        var result = new Dictionary<int, string>();

        try
        {
            if (armaGetter.SkinTexture == null) return result;

            var skinTextureLink = gender == Gender.Female
                ? armaGetter.SkinTexture.Female
                : armaGetter.SkinTexture.Male;

            if (skinTextureLink == null || skinTextureLink.IsNull) return result;

            if (!linkCache.TryResolve<ITextureSetGetter>(skinTextureLink.FormKey, out var txst))
            {
                _logger.LogMessage("CharacterViewer: Could not resolve TXST " + skinTextureLink.FormKey +
                    " from ARMA " + armaFormKey);
                return result;
            }

            // TX00=Diffuse, TX01=Normal, TX02=Glow/Detail, TX03=Height,
            // TX04=Environment, TX05=?, TX06=Multilayer, TX07=Specular
            void TryAdd(int slot, string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;

                // TXST paths are relative to Data\textures\, prepend if needed
                string fullPath = path;
                if (!fullPath.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase) &&
                    !fullPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                {
                    fullPath = "textures\\" + fullPath;
                }
                result[slot] = fullPath;
            }

            TryAdd(0, txst.Diffuse?.DataRelativePath.Path);
            TryAdd(1, txst.NormalOrGloss?.DataRelativePath.Path);
            TryAdd(2, txst.GlowOrDetailMap?.DataRelativePath.Path);
            TryAdd(3, txst.Height?.DataRelativePath.Path);
            TryAdd(4, txst.Environment?.DataRelativePath.Path);
            TryAdd(5, txst.Multilayer?.DataRelativePath.Path);
            TryAdd(7, txst.BacklightMaskOrSpecular?.DataRelativePath.Path);

            if (result.Count > 0)
            {
                _logger.LogMessage("CharacterViewer: TXST " + skinTextureLink.FormKey +
                    " (from ARMA " + armaFormKey + "): " + result.Count + " texture slots" +
                    (result.TryGetValue(0, out var diff) ? " [Diffuse=" + diff + "]" : ""));
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage("CharacterViewer: TXST resolution failed for ARMA " + armaFormKey +
                ": " + ex.Message);
        }

        return result;
    }

    private static string BuildFaceGenPath(FormKey formKey)
    {
        // Skyrim FaceGen NIF convention:
        // meshes/actors/character/FaceGenData/FaceGeom/{PluginName.esp}/{00XXXXXX}.nif
        string plugin = formKey.ModKey.FileName;
        string formId = formKey.ID.ToString("X8");
        return "meshes\\actors\\character\\FaceGenData\\FaceGeom\\" + plugin + "\\" + formId + ".nif";
    }

    private static string BuildFaceTintPath(FormKey formKey)
    {
        // Skyrim FaceTint DDS convention:
        // textures/actors/character/FaceGenData/FaceTint/{PluginName.esp}/{00XXXXXX}.dds
        string plugin = formKey.ModKey.FileName;
        string formId = formKey.ID.ToString("X8");
        return "textures\\actors\\character\\FaceGenData\\FaceTint\\" + plugin + "\\" + formId + ".dds";
    }

    /// <summary>
    /// Resolves the skeleton NIF path from the NPC's Race.SkeletalModel, selecting
    /// the gender-appropriate variant.
    /// </summary>
    private string? ResolveSkeletonPath(INpcGetter npcGetter, Gender gender, ILinkCache linkCache)
    {
        if (npcGetter.Race == null || npcGetter.Race.IsNull)
            return null;

        if (!linkCache.TryResolve<IRaceGetter>(npcGetter.Race.FormKey, out var raceGetter))
            return null;

        if (raceGetter.SkeletalModel == null)
            return null;

        var skelModel = gender == Gender.Female
            ? raceGetter.SkeletalModel.Female
            : raceGetter.SkeletalModel.Male;

        if (skelModel?.File == null)
            return null;

        string path = skelModel.File.GivenPath;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // Ensure path is Data-relative with "meshes\" prefix
        if (!path.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase))
        {
            path = "meshes\\" + path;
        }

        _logger.LogMessage("CharacterViewer: Skeleton=" + path +
            " (Race=" + npcGetter.Race.FormKey + ", " + gender + ")");

        return path;
    }

    private static Gender GetGender(INpcGetter npc)
    {
        return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female)
            ? Gender.Female
            : Gender.Male;
    }
}
