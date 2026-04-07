using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class VM_TexMeshBatchActions : VM
{
    private readonly VM_Settings_General _generalSettings;
    private readonly VM_SettingsTexMesh _texMeshSettings;
    private readonly VM_NPCAttribute.VM_NPCAttributeCreator _attributeCreator;

    public VM_TexMeshBatchActions(VM_Settings_General generalSettings, VM_SettingsTexMesh texMeshSettings, VM_NPCAttribute.VM_NPCAttributeCreator attributeCreator)
    {
        _generalSettings = generalSettings;
        _texMeshSettings = texMeshSettings;
        
        foreach (var assetPack in _texMeshSettings.AssetPacks)
        {
            AssetPacks.Add(new(assetPack));
        }

        _attributeCreator = attributeCreator;
        DisplayedAttribute = _attributeCreator.CreateNewFromUI(new ObservableCollection<VM_NPCAttribute>(), true, true, _generalSettings.AttributeGroupMenu.Groups);

        ApplyAsAllowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                foreach (var assetPack in AssetPacks)
                {
                    var clonedAttribute = DisplayedAttribute.CloneInto(assetPack.WrappedAssetPack.DistributionRules.AllowedAttributes, assetPack.WrappedAssetPack.AttributeGroupMenu.Groups);
                    assetPack.WrappedAssetPack.DistributionRules.AllowedAttributes.Add(clonedAttribute);
                }
                RegenerateDisplayedAttribute();
            });

        ApplyAsDisallowedAttribute = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedAttribute.DisplayForceIfOption = false;
                DisplayedAttribute.DisplayForceIfWeight = false;

                foreach (var assetPack in AssetPacks)
                {
                    var clonedAttribute = DisplayedAttribute.CloneInto(assetPack.WrappedAssetPack.DistributionRules.DisallowedAttributes, assetPack.WrappedAssetPack.AttributeGroupMenu.Groups);
                    assetPack.WrappedAssetPack.DistributionRules.DisallowedAttributes.Add(clonedAttribute);
                }
                RegenerateDisplayedAttribute();
            });

        SelectAll = new RelayCommand(
           canExecute: _ => true,
           execute: async _ =>
           {
               foreach (var assetPack in AssetPacks)
               {
                   assetPack.IsSelected = true;
               }
           });

        DeselectAll = new RelayCommand(
           canExecute: _ => true,
           execute: async _ =>
           {
               foreach (var assetPack in AssetPacks)
               {
                   assetPack.IsSelected = false;
               }
           });
    }


    public ObservableCollection<VM_AssetPackWrapper> AssetPacks { get; set; } = new();
    public VM_NPCAttribute DisplayedAttribute { get; set; }
    public RelayCommand SelectAll { get; set; }
    public RelayCommand DeselectAll { get; set; }
    public RelayCommand ApplyAsAllowedAttribute { get; set; }
    public RelayCommand ApplyAsDisallowedAttribute { get; set; }

    private void RegenerateDisplayedAttribute()
    {
        DisplayedAttribute = _attributeCreator.CreateNewFromUI(new ObservableCollection<VM_NPCAttribute>(), true, true, _generalSettings.AttributeGroupMenu.Groups);
        foreach (var assetPack in AssetPacks)
        {
            assetPack.IsSelected = false;
        }
    }

    public class VM_AssetPackWrapper : VM
    {
        public VM_AssetPackWrapper (VM_AssetPack assetPack)
        {
            WrappedAssetPack = assetPack;
        }
        public VM_AssetPack WrappedAssetPack { get; }
        public bool IsSelected { get; set; }
    }
}
