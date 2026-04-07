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

public class FacePartCompliance
{
    // SKSE for VR and SSE 1.5.97 or < did not yet have the GetPartName() function
    // As a result, the face headpart of an NPC must have a matching Name and EditorID or the headpart script will cause a neck seam

    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    
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

    public void Reinitialize()
    {
        FacePartCount = 0;
        RequiresComplianceCheck = true;
    }

    private int FacePartCount { get; set; } = 0;
    public bool RequiresComplianceCheck { get; set; } = false;
}
