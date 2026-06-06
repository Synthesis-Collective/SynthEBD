using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Ensures the Face head part of each NPC has a matching Name and EditorID on older SKSE runtimes
/// (Skyrim VR, LE, Enderal LE, or SE configured for the old SKSE version) that lack the
/// <c>GetPartName()</c> function. Without a matching Name/EditorID the head part script would
/// produce a visible neck seam.
/// </summary>
public class FacePartCompliance
{
    // SKSE for VR and SSE 1.5.97 or < did not yet have the GetPartName() function
    // As a result, the face headpart of an NPC must have a matching Name and EditorID or the headpart script will cause a neck seam

    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;

    /// <summary>
    /// Initializes the compliance checker and determines whether the current game release/SKSE
    /// configuration requires the Name/EditorID fix (sets <see cref="RequiresComplianceCheck"/>).
    /// </summary>
    public FacePartCompliance(IOutputEnvironmentStateProvider environmentProvider, PatcherState patcherState)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        
        var gameRelease = environmentProvider.SkyrimVersion;
        if (gameRelease == SkyrimRelease.SkyrimVR || gameRelease == SkyrimRelease.SkyrimLE || gameRelease == SkyrimRelease.EnderalLE || (gameRelease == SkyrimRelease.SkyrimSE && patcherState.TexMeshSettings.bFixedScriptsOldSKSEversion))
        {
            RequiresComplianceCheck = true;
        }
        else
        {
            RequiresComplianceCheck = false;
        }
    }

    /// <summary>
    /// For the given NPC, finds any Face head part whose EditorID and Name do not match and overrides
    /// it in the output mod to bring them into agreement: copies EditorID into Name when EditorID exists,
    /// copies Name into EditorID when only Name exists, or assigns a generated unique name when neither does.
    /// </summary>
    /// <param name="npcInfo">The NPC whose head parts are inspected and fixed.</param>
    public void CheckAndFixFaceName(NPCInfo npcInfo)
    {
        if (npcInfo.NPC != null && npcInfo.NPC.HeadParts != null)
        {
            foreach (var part in npcInfo.NPC.HeadParts)
            {
                var partGetter = part.TryResolve(_environmentProvider.LinkCache);
                if (partGetter != null && partGetter.Type != null && partGetter.Type == HeadPart.TypeEnum.Face && (partGetter.EditorID == null || partGetter.Name == null || partGetter.EditorID != partGetter.Name.ToString()))
                {
                    var headPart = _environmentProvider.OutputMod.HeadParts.GetOrAddAsOverride(partGetter);
                    var name = partGetter.Name?.ToString() ?? String.Empty;

                    if (partGetter.EditorID != null)
                    {
                        headPart.Name = headPart.EditorID;
                    }
                    else if (partGetter.EditorID == null && name != String.Empty)
                    {
                        headPart.EditorID = name;
                    }
                    else
                    {
                        string newName = "SynthEBDFace" + FacePartCount.ToString();
                        headPart.EditorID = newName;
                        headPart.Name = newName;
                        FacePartCount++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resets the generated-name counter and re-enables the compliance check for a new patcher run.
    /// </summary>
    public void Reinitialize()
    {
        FacePartCount = 0;
        RequiresComplianceCheck = true;
    }

    /// <summary>Running counter used to generate unique fallback EditorID/Name values ("SynthEBDFace{N}").</summary>
    private int FacePartCount { get; set; } = 0;
    /// <summary>Whether the current game release/SKSE configuration needs the Face Name/EditorID fix applied.</summary>
    public bool RequiresComplianceCheck { get; set; } = false;
}
