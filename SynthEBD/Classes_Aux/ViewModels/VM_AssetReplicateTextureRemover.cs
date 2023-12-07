using DynamicData;
using Mutagen.Bethesda.Fallout4;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.AssetPack;

namespace SynthEBD
{
    public class VM_AssetReplicateTextureRemover : VM
    {
        public VM_AssetReplicateTextureRemover(ConfigDrafter configDrafter, VM_SettingsModManager modManagerSettings, IEnvironmentStateProvider environmentProvider)
        {
            DuplicateCheckProgress = new(report =>
            {
                HashingProgressCurrent = report.Item1;
                HashingProgressMax = report.Item2;
                CurrentlyHashingFile = report.Item3;
            });

            CheckDuplicatesButton = ReactiveCommand.CreateFromTask(
            execute: async _ =>
            {
                if (AssetPack == null)
                {
                    return;
                }

                var subgroups = AssetPack.GetAllSubgroups();
                var texturePaths = subgroups.Select(x => x.AssociatedModel).SelectMany(y => y.Paths).Select(z => Path.Combine(environmentProvider.DataFolderPath, z.Source)).Where(f => File.Exists(f)).Distinct().ToList();

                MultipletTextureGroups.Clear();

                List<string> rootFolders = new() { environmentProvider.DataFolderPath };

                var multiples = await Task.Run(async () => VM_ConfigDrafter.ComputeFileDuplicates(texturePaths, DuplicateCheckProgress, rootFolders, modManagerSettings.ModManagerType != ModManager.None, configDrafter));
                Noggog.ListExt.AddRange(MultipletTextureGroups, multiples.Result);
                HasMultiplets = multiples.Result.Any();
                HashingProgressCurrent = 0;
                CurrentlyHashingFile = string.Empty;
            });

            CleanAssetPack = ReactiveCommand.CreateFromTask(
            execute: async _ =>
            {
                var multiplets = MultipletTextureGroups.Select(x => x.ToMultiplet()).ToList();
                switch (SelectedCleaningMode)
                {
                    case CleaningOptionRemapPaths:
                        RemapTexturePaths(multiplets);
                        break;
                    case CleaningOptionRemoveSubgroups:
                        RemoveReplicateSubgroups(multiplets);
                        break;
                }

                string message = "No Replicate Assets Found";
                if (HasMultiplets)
                {
                    foreach (var multiplet in MultipletTextureGroups)
                    {
                        multiplet.RemoveSelected();
                    }
                    message = "The following Subgroups were cleaned:" + Environment.NewLine + string.Join(Environment.NewLine, CleanedSubgroups.Select(x => x.GetNameChain(" -> ")));
                }
                HasMultiplets = false;
                MessageWindow.DisplayNotificationOK("Replicate Asset Removal", message);
            });
        }
        public VM_AssetPack AssetPack { get; set; }
        public ObservableCollection<VM_FileDuplicateContainer> MultipletTextureGroups { get; set; } = new();
        public bool HasMultiplets { get; set; }
        public string CurrentlyHashingFile { get; set; } = String.Empty;
        public int HashingProgressCurrent { get; set; } = 0;
        public int HashingProgressMax { get; set; } = 1;
        private Progress<(int, int, string)> DuplicateCheckProgress { get; }
        public IReactiveCommand CheckDuplicatesButton { get; }
        private const string CleaningOptionRemoveSubgroups = "Remove Replicate Subgroups";
        private const string CleaningOptionRemapPaths = "Remap Replicate Paths";
        public string ModeToolTipStr { get; set; } = CleaningOptionRemoveSubgroups + ": Removes subgroups if they contain a duplicate texture. Remaps Required Subgroups to account for this if possible" + Environment.NewLine +
            CleaningOptionRemapPaths + ": Keeps all subgroups but changes duplicate texture paths so that they all point to one texture on disk" + Environment.NewLine +
            "Both reduce VRAM usage in game. Removing subgroups can slightly speed up patching (no in-game performance difference) but you will need to make sure that any changed Required Subgroups are still logical. Remapping textures is safer but patching might take longer";
        public ObservableCollection<string> CleaningOptions { get; set; } = new() { CleaningOptionRemapPaths, CleaningOptionRemoveSubgroups };
        public string SelectedCleaningMode { get; set; } = CleaningOptionRemapPaths;
        public IReactiveCommand CleanAssetPack { get; }
        public HashSet<VM_SubgroupPlaceHolder> AllSubgroups { get; set; } = new();
        public List<VM_SubgroupPlaceHolder> CleanedSubgroups { get; set; } = new();

        public void Initialize(VM_AssetPack assetPack)
        {
            AssetPack = assetPack;
            HasMultiplets = false;
            CleanedSubgroups.Clear();
            MultipletTextureGroups.Clear();
            AllSubgroups = AssetPack.GetAllSubgroups();
        }

        private void RemapTexturePaths(List<Multiplet> multiplets)
        {
            var subgroups = AssetPack.GetAllSubgroups();
            foreach (var subgroup in subgroups)
            {
                RemapSubgroup(subgroup, multiplets);
            }
        }

        private void RemapSubgroup(VM_SubgroupPlaceHolder subgroup, List<Multiplet> multiplets)
        {
            bool wasRemapped = false;
            foreach(var path in subgroup.AssociatedModel.Paths)
            {
                if(RemapPath(subgroup, path, multiplets, out _))
                {
                    wasRemapped = true;
                }
            }

            if (wasRemapped)
            {
                CleanedSubgroups.Add(subgroup);
            }
        }

        private bool RemapPath(VM_SubgroupPlaceHolder subgroup, FilePathReplacement path, List<Multiplet> multiplets, out Multiplet? correspondingMultiplet)
        {
            correspondingMultiplet = multiplets.Where(x => x.ReplicatePaths.Contains(path.Source, StringComparer.OrdinalIgnoreCase)).FirstOrDefault();
            if (correspondingMultiplet != null)
            {
                if (subgroup.AssociatedModel.Notes.Any())
                {
                    subgroup.AssociatedModel.Notes += Environment.NewLine;
                }
                subgroup.AssociatedModel.Notes += "Formerly contained replicate asset: " + path.Source;
                path.Source = correspondingMultiplet.PrimaryPath;
                return true;
            }
            return false;
        }

        private void RemoveReplicateSubgroups(List<Multiplet> multiplets)
        {
            foreach (var sg in AssetPack.Subgroups)
            {
                RemoveReplicateSubgroupsRecursive(sg, multiplets); // don't remove any top-level subgroups
            }
        }

        private bool RemoveReplicateSubgroupsRecursive(VM_SubgroupPlaceHolder subgroup, List<Multiplet> multiplets)
        {
            // do recursion first to trip "fingertip nodes" and avoid leaving orphans
            for (int i = 0; i < subgroup.Subgroups.Count; i++)
            {
                if (RemoveReplicateSubgroupsRecursive(subgroup.Subgroups[i], multiplets))
                {
                    AllSubgroups.Remove(subgroup.Subgroups[i]);
                    subgroup.Subgroups.RemoveAt(i);
                    i--;
                }
            }

            bool allFilePathsReplicate = true;
            List<VM_SubgroupPlaceHolder> primarySubgroups = new();

            foreach (var path in subgroup.AssociatedModel.Paths)
            {
                if(RemapPath(subgroup, path, multiplets, out var correspondingMultiplet) && correspondingMultiplet != null && TryGetPrimaryPathHolder(correspondingMultiplet.PrimaryPath, out var primarySubgroup) && primarySubgroup != null) // remap path so that if not all assets in this subgroup are replicates, the ones that are at least get remapped to save VRAM
                {
                    primarySubgroups.Add(primarySubgroup);
                }
                else
                {
                    allFilePathsReplicate = false;
                }
            }

            primarySubgroups = primarySubgroups.Distinct().ToList();

            if (allFilePathsReplicate && !subgroup.Subgroups.Any() && primarySubgroups.Count == 1)
            {
                CleanedSubgroups.Add(subgroup);
                RemapRequiredExcludedSubgroups(subgroup.ID, primarySubgroups.First().ID);
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool TryGetPrimaryPathHolder(string primaryPath, out VM_SubgroupPlaceHolder? subgroup)
        {
            subgroup = AllSubgroups.Where(subgroup => subgroup.AssociatedModel.Paths.Where(path => path.Source.Equals(primaryPath, StringComparison.OrdinalIgnoreCase)).Any()).FirstOrDefault();
            return subgroup != null;
        }

        private void RemapRequiredExcludedSubgroups(string oldID, string newID)
        {
            foreach(var subgroup in AllSubgroups)
            {
                if (subgroup.AssociatedModel.RequiredSubgroups.Contains(oldID))
                {
                    subgroup.AssociatedModel.RequiredSubgroups.Replace(oldID, newID);
                }

                if (subgroup.AssociatedModel.ExcludedSubgroups.Contains(oldID))
                {
                    subgroup.AssociatedModel.ExcludedSubgroups.Replace(oldID, newID);
                }
            }
        }
    }
}
