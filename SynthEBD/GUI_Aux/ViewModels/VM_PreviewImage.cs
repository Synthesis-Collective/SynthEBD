using System.Windows.Controls;

namespace SynthEBD;

public class VM_PreviewImage : VM
{
    public VM_PreviewImage(System.Windows.Media.Imaging.BitmapSource image, VM_SubgroupPlaceHolder source)
    {
        Image = image;
        Source = source;
    }
    public System.Windows.Media.Imaging.BitmapSource Image { get; set; }
    public VM_SubgroupPlaceHolder Source { get; set; }
}