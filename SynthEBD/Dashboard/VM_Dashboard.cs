using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

/// <summary>
/// The Dashboard home page: one tile per functional module, each with a small status readout and
/// (for the patcher axes) a power toggle bridged to the module's existing enable flag. Tiles
/// navigate to their detail menus by setting the shared <see cref="DisplayedItemVm"/> slot (the
/// same mechanism <see cref="VM_NavPanel"/> uses; injected directly to avoid a DI cycle with the
/// nav panel). Environment, Specific NPC Assignments and Block List are status-only tiles: the
/// latter two read as active whenever they contain entries. Status readouts are recomputed
/// whenever the dashboard becomes the displayed view. Registered as a singleton in
/// <see cref="MainModule"/>.
/// </summary>
public class VM_Dashboard : VM
{
    public VM_Dashboard(
        DisplayedItemVm displayedItemVm,
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        VM_Settings_General general,
        VM_SettingsTexMesh texMesh,
        VM_SettingsBodyGen bodyGen,
        VM_SettingsOBody oBody,
        VM_SettingsHeight height,
        VM_Settings_Headparts headParts,
        VM_SettingsDestandalone destandalone,
        VM_SpecificNPCAssignmentsUI specificAssignments,
        VM_ConsistencyUI consistency,
        VM_BlockListUI blockList)
    {
        var environmentTile = new VM_DashboardTile("Environment", "Dashboard.Environment", hasPowerToggle: false,
            statusProvider: () =>
            {
                bool valid = environmentProvider.LinkCache != null && environmentProvider.LinkCache.ListedOrder.Count > 1;
                if (!valid)
                {
                    return "Game environment is NOT valid - check the game data directory in General Settings.";
                }
                return environmentProvider.SkyrimVersion + " | " + environmentProvider.LoadOrder.Count + " plugins"
                    + System.Environment.NewLine + environmentProvider.DataFolderPath;
            },
            healthProvider: () => environmentProvider.LinkCache != null && environmentProvider.LinkCache.ListedOrder.Count > 1
                ? TileHealth.Ok : TileHealth.Warning,
            navigate: () => displayedItemVm.DisplayedViewModel = general);

        var assetsTile = new VM_DashboardTile("Asset Patching", "Dashboard.AssetPatching", hasPowerToggle: true,
            statusProvider: () => texMesh.AssetPacks.Count(x => x.IsSelected) + " of " + texMesh.AssetPacks.Count
                + " config files active" + System.Environment.NewLine + "Face patching: " + texMesh.FacePatchingMode,
            healthProvider: () => general.bChangeMeshesOrTextures ? TileHealth.Ok : TileHealth.Off,
            navigate: () => displayedItemVm.DisplayedViewModel = texMesh);

        var bodyTile = new VM_DashboardTile("Body Shape Patching", "Dashboard.BodyShape", hasPowerToggle: true,
            statusProvider: () =>
            {
                switch (general.BodySelectionMode)
                {
                    case BodyShapeSelectionMode.BodyGen:
                        int maleConfigs = patcherState.BodyGenConfigs?.Male.Count ?? 0;
                        int femaleConfigs = patcherState.BodyGenConfigs?.Female.Count ?? 0;
                        return "BodyGen (RaceMenu morphs)" + System.Environment.NewLine
                            + maleConfigs + " male / " + femaleConfigs + " female morph configs";
                    case BodyShapeSelectionMode.BodySlide:
                        int malePresets = patcherState.OBodySettings?.BodySlidesMale.Count ?? 0;
                        int femalePresets = patcherState.OBodySettings?.BodySlidesFemale.Count ?? 0;
                        return "BodySlide via " + general.BSSelectionMode + System.Environment.NewLine
                            + malePresets + " male / " + femalePresets + " female presets";
                    default:
                        return "Off";
                }
            },
            healthProvider: () => general.BodySelectionMode != BodyShapeSelectionMode.None ? TileHealth.Ok : TileHealth.Off,
            navigate: () => displayedItemVm.DisplayedViewModel =
                general.BodySelectionMode == BodyShapeSelectionMode.BodyGen ? bodyGen : oBody);

        var headPartsTile = new VM_DashboardTile("Headpart Patching", "Dashboard.Headparts", hasPowerToggle: true,
            statusProvider: () =>
            {
                int headPartCount = patcherState.HeadPartSettings?.Types?.Sum(x => x.Value.HeadParts.Count) ?? 0;
                return headPartCount + " head parts imported for distribution";
            },
            healthProvider: () => general.bChangeHeadParts ? TileHealth.Ok : TileHealth.Off,
            navigate: () => displayedItemVm.DisplayedViewModel = headParts);

        var heightTile = new VM_DashboardTile("Height Patching", "Dashboard.Height", hasPowerToggle: true,
            statusProvider: () => (height.SelectedHeightConfig?.Label).IsNullOrWhitespace()
                ? height.AvailableHeightConfigs.Count + " height configs installed"
                : "Using \"" + height.SelectedHeightConfig.Label + "\" (" + height.AvailableHeightConfigs.Count + " installed)",
            healthProvider: () => general.bChangeHeight ? TileHealth.Ok : TileHealth.Off,
            navigate: () => displayedItemVm.DisplayedViewModel = height);

        var destandaloneTile = new VM_DashboardTile("Destandalone Patching", "Dashboard.Destandalone", hasPowerToggle: true,
            statusProvider: () =>
            {
                if (!texMesh.bForceVanillaBodyMeshPath) { return "Off - config files keep their own body mesh paths"; }
                var status = "Forcing vanilla body mesh paths" + (texMesh.bAllowUBEBodyPaths ? " (UBE preserved)" : "");
                if (!general.bChangeMeshesOrTextures)
                {
                    status += System.Environment.NewLine + "No effect while Asset Patching is off";
                }
                return status;
            },
            healthProvider: () => !texMesh.bForceVanillaBodyMeshPath ? TileHealth.Off
                : general.bChangeMeshesOrTextures ? TileHealth.Ok : TileHealth.Warning,
            navigate: () => displayedItemVm.DisplayedViewModel = destandalone);

        var specificAssignmentsTile = new VM_DashboardTile("Specific Assignments", "Dashboard.SpecificAssignments", hasPowerToggle: false,
            statusProvider: () => specificAssignments.Assignments.Count + " NPCs with specific assignments",
            healthProvider: () => specificAssignments.Assignments.Count > 0 ? TileHealth.Ok : TileHealth.Off,
            navigate: () => displayedItemVm.DisplayedViewModel = specificAssignments);

        var consistencyTile = new VM_DashboardTile("Consistency", "Dashboard.Consistency", hasPowerToggle: true,
            statusProvider: () => (patcherState.Consistency?.Count ?? 0) + " NPCs remembered from previous runs",
            healthProvider: () => general.bEnableConsistency ? TileHealth.Ok : TileHealth.Off,
            navigate: () => displayedItemVm.DisplayedViewModel = consistency);

        var blockListTile = new VM_DashboardTile("Block List", "Dashboard.BlockList", hasPowerToggle: false,
            statusProvider: () => blockList.BlockedNPCs.Count + " NPCs / " + blockList.BlockedPlugins.Count + " plugins blocked",
            healthProvider: () => blockList.BlockedNPCs.Count > 0 || blockList.BlockedPlugins.Count > 0 ? TileHealth.Ok : TileHealth.Off,
            navigate: () => displayedItemVm.DisplayedViewModel = blockList);

        Tiles = new()
        {
            environmentTile, assetsTile, bodyTile, headPartsTile, heightTile,
            destandaloneTile, specificAssignmentsTile, consistencyTile, blockListTile,
        };

        // Power-toggle bridges: tile -> module flag, and module flag -> tile (Fody only raises on
        // real changes, so the two directions can't ping-pong).
        BridgePower(assetsTile, general.WhenAnyValue(x => x.bChangeMeshesOrTextures), v => general.bChangeMeshesOrTextures = v);
        BridgePower(headPartsTile, general.WhenAnyValue(x => x.bChangeHeadParts), v => general.bChangeHeadParts = v);
        BridgePower(heightTile, general.WhenAnyValue(x => x.bChangeHeight), v => general.bChangeHeight = v);
        BridgePower(consistencyTile, general.WhenAnyValue(x => x.bEnableConsistency), v => general.bEnableConsistency = v);
        BridgePower(destandaloneTile, texMesh.WhenAnyValue(x => x.bForceVanillaBodyMeshPath), v => texMesh.bForceVanillaBodyMeshPath = v);

        // Body Shape is a three-state mode behind a binary toggle: off remembers the current mode
        // in LastBodySelectionMode; on restores it.
        BridgePower(bodyTile,
            general.WhenAnyValue(x => x.BodySelectionMode).Select(mode => mode != BodyShapeSelectionMode.None),
            powered =>
            {
                if (powered && general.BodySelectionMode == BodyShapeSelectionMode.None)
                {
                    general.BodySelectionMode = general.LastBodySelectionMode;
                }
                else if (!powered && general.BodySelectionMode != BodyShapeSelectionMode.None)
                {
                    general.LastBodySelectionMode = general.BodySelectionMode;
                    general.BodySelectionMode = BodyShapeSelectionMode.None;
                }
            });

        // Any user-selected non-None mode becomes the mode the power toggle restores.
        general.WhenAnyValue(x => x.BodySelectionMode)
            .Where(mode => mode != BodyShapeSelectionMode.None)
            .Subscribe(mode => general.LastBodySelectionMode = mode).DisposeWith(this);

        // Refresh all status readouts whenever the dashboard becomes the displayed view (cheap,
        // and avoids wiring observers into every underlying collection). Toggling power also
        // refreshes so the readouts react while the dashboard is visible.
        displayedItemVm.WhenAnyValue(x => x.DisplayedViewModel)
            .Where(displayed => ReferenceEquals(displayed, this))
            .Subscribe(_ => RefreshAllTiles()).DisposeWith(this);
        foreach (var tile in Tiles)
        {
            tile.WhenAnyValue(x => x.IsPowered).Subscribe(_ => RefreshAllTiles()).DisposeWith(this);
        }
    }

    public ObservableCollection<VM_DashboardTile> Tiles { get; }

    /// <summary>Wires a tile's IsPowered both ways to a module flag.</summary>
    private void BridgePower(VM_DashboardTile tile, IObservable<bool> source, Action<bool> setter)
    {
        source.Subscribe(v => tile.IsPowered = v).DisposeWith(this);
        tile.WhenAnyValue(x => x.IsPowered).Subscribe(v => setter(v)).DisposeWith(this);
    }

    private void RefreshAllTiles()
    {
        foreach (var tile in Tiles)
        {
            tile.Refresh();
        }
    }
}
