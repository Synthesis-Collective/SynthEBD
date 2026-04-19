using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// UI editor for <see cref="Settings_OBody.BodyTypeRegistry"/>. Each row wraps one
/// <see cref="BodyTypeRegistryEntry"/>. Runtime-derived fields (IsInstalled, ResolvedSliders
/// count, SupersetOfBodyType) surface as read-only diagnostics so the user can see what the
/// scanner + extractor concluded on the last load.
/// </summary>
public class VM_BodyTypeRegistry : VM
{
    public delegate VM_BodyTypeRegistry Factory();

    public VM_BodyTypeRegistry()
    {
        AddEntry = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Entries.Add(new VM_BodyTypeRegistryEntry(new BodyTypeRegistryEntry { IsUserDefined = true }, Entries)));
    }

    public ObservableCollection<VM_BodyTypeRegistryEntry> Entries { get; set; } = new();
    public RelayCommand AddEntry { get; }

    public void CopyInViewModelFromModel(Settings_OBody model)
    {
        Entries.Clear();
        if (model?.BodyTypeRegistry == null) return;
        foreach (var e in model.BodyTypeRegistry)
        {
            if (e == null) continue;
            Entries.Add(new VM_BodyTypeRegistryEntry(e, Entries));
        }
    }

    public void DumpViewModelToModel(Settings_OBody model)
    {
        if (model == null) return;
        model.BodyTypeRegistry = new List<BodyTypeRegistryEntry>();
        foreach (var vm in Entries)
        {
            var entry = vm.DumpToModel();
            if (!string.IsNullOrWhiteSpace(entry.Name)) model.BodyTypeRegistry.Add(entry);
        }
    }
}

/// <summary>
/// Row VM for a single registry entry. Edits Name / Gender / fingerprints / ShapeData folders
/// / IsUserDefined; shows IsInstalled, ResolvedSliders.Count, and auto-detected superset as
/// read-only feedback from the last scan.
/// </summary>
public class VM_BodyTypeRegistryEntry : VM
{
    private readonly ObservableCollection<VM_BodyTypeRegistryEntry> _parent;
    private readonly BodyTypeRegistryEntry _source;

    public VM_BodyTypeRegistryEntry(BodyTypeRegistryEntry source, ObservableCollection<VM_BodyTypeRegistryEntry> parent)
    {
        _source = source;
        _parent = parent;

        Name = source.Name ?? "";
        Gender = source.Gender;
        FingerprintsCsv = source.IdentityFingerprints != null ? string.Join(", ", source.IdentityFingerprints) : "";
        ShapeDataFoldersCsv = source.ShapeDataFolders != null ? string.Join(", ", source.ShapeDataFolders) : "";
        IsUserDefined = source.IsUserDefined;

        IsInstalled = source.IsInstalled;
        ResolvedSliderCount = source.ResolvedSliders?.Count ?? 0;
        SupersetOfBodyType = source.SupersetOfBodyType ?? "";

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => _parent.Remove(this));
    }

    public string Name { get; set; }
    public Gender Gender { get; set; }
    public string FingerprintsCsv { get; set; }
    public string ShapeDataFoldersCsv { get; set; }
    public bool IsUserDefined { get; set; }

    // Read-only diagnostics from the most recent load pass.
    public bool IsInstalled { get; }
    public int ResolvedSliderCount { get; }
    public string SupersetOfBodyType { get; }

    public RelayCommand DeleteCommand { get; }

    public BodyTypeRegistryEntry DumpToModel()
    {
        return new BodyTypeRegistryEntry
        {
            Name = Name?.Trim() ?? "",
            Gender = Gender,
            IdentityFingerprints = SplitCsv(FingerprintsCsv),
            ShapeDataFolders = SplitCsv(ShapeDataFoldersCsv),
            SupersetOfBodyType = _source.SupersetOfBodyType ?? "",
            IsUserDefined = IsUserDefined,
        };
    }

    private static List<string> SplitCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new List<string>();
        return csv.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
    }
}
