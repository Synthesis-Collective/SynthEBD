using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class PrePatcher
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly VM_StatusBar _statusBar;
        private readonly PatchableRaceResolver _raceResolver;

        public PrePatcher(IEnvironmentStateProvider environtProvider, PatcherState patcherState, Logger logger, VM_StatusBar statusBar, PatchableRaceResolver raceResolver)
        {
            _environmentProvider = environtProvider;
            _patcherState = patcherState;
            _logger = logger;
            _statusBar = statusBar;
            _raceResolver = raceResolver;
        }

        public void BlockByArmature()
        {
            var allNPCs = _environmentProvider.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<INpcGetter>();

            // UI Pre-patching tasks:
            _logger.UpdateStatus("Pre-Patching", false);
            _logger.StartTimer();
            _logger.PatcherExecutionStart = DateTime.Now;
            _statusBar.IsPatching = true;
            _statusBar.ProgressBarMax = allNPCs.Count();
            _statusBar.ProgressBarCurrent = 0;

            _raceResolver.ResolvePatchableRaces();

            foreach (var npc in allNPCs)
            {
                if (_patcherState.GeneralSettings.PrepatchedMods.Contains(npc.FormKey.ModKey))
                {
                    continue;
                }

                if (!_raceResolver.PatchableRaces.Contains(npc.Race))
                {
                    continue;
                }

                if (!AppearsHumanoidByArmature(npc))
                {
                    var blockedNPC = new BlockedNPC()
                    {
                        FormKey = npc.FormKey,
                        Assets = true,
                        BodyShape = true,
                        HeadParts = true,
                        HeadPartTypes = new()
                        {
                            { HeadPart.TypeEnum.Eyebrows, true },
                            { HeadPart.TypeEnum.Eyes, true},
                            { HeadPart.TypeEnum.Face, true},
                            { HeadPart.TypeEnum.FacialHair, true},
                            { HeadPart.TypeEnum.Hair, true},
                            {  HeadPart.TypeEnum.Misc, true},
                            {  HeadPart.TypeEnum.Scars, true}
                        },
                        VanillaBodyPath = true
                    };
                    _patcherState.BlockList.NPCs.Add(blockedNPC);
                    _logger.LogMessage("Added " + _logger.GetNPCLogNameString(npc) + " to Blocked NPC List");
                }

                _statusBar.ProgressBarCurrent++;
            }

            foreach (var mod in _environmentProvider.LoadOrder.PriorityOrder)
            {
                if (!_patcherState.GeneralSettings.PrepatchedMods.Contains(mod.ModKey))
                {
                    _patcherState.GeneralSettings.PrepatchedMods.Add(mod.ModKey);
                }
            }
            _logger.StopTimer();
            _statusBar.IsPatching = false;
        }
        public bool AppearsHumanoidByArmature(INpcGetter npc) // tries to identify creatures that are wrongly assigned a humanoid race via their armature
        {
            if (!_patcherState.TexMeshSettings.bFilterNPCsByArmature)
            {
                return true;
            }

            if (npc.WornArmor == null || npc.WornArmor.IsNull)
            {
                return true;
            }

            var armor = npc.WornArmor.TryResolve(_environmentProvider.LinkCache);
            if (armor == null || armor.Armature == null || armor.Armature.Count == 0) { return true; }

            if (armor.Armature.Count >= 3)
            {
                var arma = armor.Armature.Select(x => x.TryResolve(_environmentProvider.LinkCache)).Where(x => x != null && x.BodyTemplate != null).ToList();
                var toMatch = new List<BipedObjectFlag>() { BipedObjectFlag.Body, BipedObjectFlag.Hands, BipedObjectFlag.Feet };

                for (int i = 0; i < arma.Count; i++)
                {
                    var armature = arma[i];

                    for (int j = 0; j < toMatch.Count; j++)
                    {
                        var bodypart = toMatch[j];
                        if (armature.BodyTemplate.FirstPersonFlags.HasFlag(bodypart))
                        {
                            toMatch.Remove(bodypart);
                            arma.Remove(armature);
                            i--;
                            break;
                        }
                    }
                }

                if (toMatch.Count == 0) // npc has skin with torso, hands, and feet components
                {
                    return true;
                }
            }
            return false;
        }
    }
}
