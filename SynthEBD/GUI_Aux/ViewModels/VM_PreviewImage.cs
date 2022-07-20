using System.Windows.Controls;

namespace SynthEBD;

public class VM_PreviewImage : VM
{
    public VM_PreviewImage(System.Windows.Media.Imaging.BitmapSource image, VM_Subgroup source)
    {
        Image = image;
        Source = source;
    }
    public System.Windows.Media.Imaging.BitmapSource Image { get; set; }
    public VM_Subgroup Source { get; set; }
}