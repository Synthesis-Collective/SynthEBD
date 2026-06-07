using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Backs a single asset preview image in the UI, pairing a decoded <see cref="System.Windows.Media.Imaging.BitmapSource"/>
/// with the <see cref="VM_SubgroupPlaceHolder"/> subgroup it was sourced from.
/// </summary>
public class VM_PreviewImage : VM
{
    /// <summary>Captures the preview <paramref name="image"/> and its originating <paramref name="source"/> subgroup.</summary>
    public VM_PreviewImage(System.Windows.Media.Imaging.BitmapSource image, VM_SubgroupPlaceHolder source)
    {
        Image = image;
        Source = source;
    }
    public System.Windows.Media.Imaging.BitmapSource Image { get; set; }
    public VM_SubgroupPlaceHolder Source { get; set; }
}