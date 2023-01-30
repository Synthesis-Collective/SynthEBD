using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class FacePartCompliance
    {
        // SKSE for VR and SSE 1.5.97 or < did not yet have the GetPartName() function
        // As a result, the face headpart of an NPC must have a matching Name and EditorID or the headpart script will cause a neck seam

        public FacePartCompliance(IOutputEnvironmentStateProvider environmentProvider, PatcherState patcherState)
        {
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

        public void CheckAndFixFaceName(NPCInfo npcInfo, ILinkCache linkCache, ISkyrimMod outputMod)
        {
            if (npcInfo.NPC != null && npcInfo.NPC.HeadParts != null)
            {
                foreach (var part in npcInfo.NPC.HeadParts)
                {
                    var partGetter = part.TryResolve(linkCache);
                    if (partGetter != null && partGetter.Type != null && partGetter.Type == HeadPart.TypeEnum.Face && (partGetter.EditorID == null || partGetter.Name == null || partGetter.EditorID != partGetter.Name.ToString()))
                    {
                        var headPart = outputMod.HeadParts.GetOrAddAsOverride(partGetter);
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

        private int FacePartCount { get; set; } = 0;
        public bool RequiresComplianceCheck { get; set; } = false;
    }
}
