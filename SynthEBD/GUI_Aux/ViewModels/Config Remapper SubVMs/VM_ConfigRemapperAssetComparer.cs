using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD;

/// <summary>
/// Config Path Remapper sub-panel that compares two arbitrary files by MD5 hash and reports whether they are identical.
/// </summary>
public class VM_ConfigRemapperAssetComparer : VM, IConfigRemapperSubVM
{
    /// <summary>
    /// Wires a subscription gating evaluation on both paths existing, plus the SetPath1/SetPath2 file pickers and the
    /// ComparePaths command that hashes and compares the two files.
    /// </summary>
    public VM_ConfigRemapperAssetComparer()
    {
        this.WhenAnyValue(x => x.Path1, x => x.Path2).Subscribe(paths =>
        {
            DisplayStr = string.Empty;
            if (!paths.Item1.IsNullOrWhitespace() && !paths.Item2.IsNullOrWhitespace() && File.Exists(paths.Item1) && File.Exists(paths.Item2))
            {
                _canEvaluate = true;
            }
            else
            {
                _canEvaluate = false;
            }
        }).DisposeWith(this);

        SetPath1 = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile("", "", "Select the first file to compare", out string filePath))
                {
                    Path1 = filePath;
                }
            });

        SetPath2 = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile("", "", "Select the second file to compare", out string filePath))
                {
                    Path2 = filePath;
                }
            });

        ComparePaths = new RelayCommand(
            canExecute: _ => _canEvaluate,
            execute: _ =>
            {
                var hash1 = MiscFunctions.CalculateMD5(Path1);
                var hash2 = MiscFunctions.CalculateMD5(Path2);
                if (hash1 == hash2)
                {
                    DisplayStr = "Files are identical";
                    DisplayColor = CommonColors.Green;
                }
                else
                {
                    DisplayStr = "Files are not identical";
                    DisplayColor = CommonColors.Red;
                }
            });
    }

    public string Path1 { get; set; } = string.Empty;
    public string Path2 { get; set; } = string.Empty;
    public string DisplayStr { get; set; } = string.Empty;
    public SolidColorBrush DisplayColor { get; set; } = CommonColors.White;
    private bool _canEvaluate = false;

    public RelayCommand SetPath1 { get; }
    public RelayCommand SetPath2 { get; }
    public RelayCommand ComparePaths { get; }

    /// <summary>No-op; this comparer has no searchable list, so it ignores the filter parameters.</summary>
    public void Refresh(string subgroupSearchStr, bool subgroupCaseSensitive, string pathSearchStr, bool pathCaseSensitive)
    {
        return;
    }
}
