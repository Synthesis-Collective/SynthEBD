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
        /// Maps body part name ("Body", "Hands", "Feet", "Head") to a human-readable
        /// string describing the record traversal chain used to resolve its mesh path.
        /// E.g. "NPC:Astrid → WornArmor:000D2B5A → Armature(Body):000D2B5B → Female → femalebody_1.nif"
        /// </summary>
        public Dictionary<string, string> ResolutionChains { get; init; } = new();
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

        // Resolve the armor providing skin meshes (WornArmor, or Race.Skin fallback)
        var (armorGetter, armorSource) = ResolveWornArmor(npcGetter, linkCache, npcName);

        string? bodyPath = null;
        string? handsPath = null;
        string? feetPath = null;
        var chains = new Dictionary<string, string>();
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

                var flags = armaGetter.BodyTemplate.FirstPersonFlags;
                string? meshPath = GetWorldModelPath(armaGetter, gender);

                if (meshPath == null)
                {
                    continue;
                }

                string meshFileName = Path.GetFileName(meshPath);

                if (bodyPath == null && flags.HasFlag(BipedObjectFlag.Body))
                {
                    bodyPath = meshPath;
                    chains["Body"] = npcName + " → " + armorSource + " → Armature(Body):" + armaLink.FormKey +
                        " → " + genderLabel + " → " + meshFileName;
                    _logger.LogMessage("CharacterViewer: Armature[Body]=" + armaLink.FormKey + ", WorldModel=" + meshPath);
                }

                if (handsPath == null && flags.HasFlag(BipedObjectFlag.Hands))
                {
                    handsPath = meshPath;
                    chains["Hands"] = npcName + " → " + armorSource + " → Armature(Hands):" + armaLink.FormKey +
                        " → " + genderLabel + " → " + meshFileName;
                    _logger.LogMessage("CharacterViewer: Armature[Hands]=" + armaLink.FormKey + ", WorldModel=" + meshPath);
                }

                if (feetPath == null && flags.HasFlag(BipedObjectFlag.Feet))
                {
                    feetPath = meshPath;
                    chains["Feet"] = npcName + " → " + armorSource + " → Armature(Feet):" + armaLink.FormKey +
                        " → " + genderLabel + " → " + meshFileName;
                    _logger.LogMessage("CharacterViewer: Armature[Feet]=" + armaLink.FormKey + ", WorldModel=" + meshPath);
                }
            }
        }

        // FaceGen NIF: meshes/actors/character/FaceGenData/FaceGeom/{plugin}/{formID}.nif
        string headPath = BuildFaceGenPath(npcFormKey);
        chains["Head"] = npcName + " → FaceGen → " + Path.GetFileName(headPath);
        _logger.LogMessage("CharacterViewer: FaceGen head mesh=" + headPath);

        return new NpcMeshPaths
        {
            BodyMeshPath = bodyPath,
            HandsMeshPath = handsPath,
            FeetMeshPath = feetPath,
            HeadMeshPath = headPath,
            Gender = gender,
            ResolutionChains = chains
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

    private static string BuildFaceGenPath(FormKey formKey)
    {
        // Skyrim FaceGen NIF convention:
        // meshes/actors/character/FaceGenData/FaceGeom/{PluginName.esp}/{00XXXXXX}.nif
        string plugin = formKey.ModKey.FileName;
        string formId = formKey.ID.ToString("X8");
        return "meshes\\actors\\character\\FaceGenData\\FaceGeom\\" + plugin + "\\" + formId + ".nif";
    }

    private static Gender GetGender(INpcGetter npc)
    {
        return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female)
            ? Gender.Female
            : Gender.Male;
    }
}
