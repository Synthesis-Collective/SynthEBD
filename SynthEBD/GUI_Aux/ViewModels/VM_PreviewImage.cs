using System.Windows.Controls;

namespace SynthEBD;

public class VM_PreviewImage : VM
{
    //public VM_PreviewImage(Image image, string source)
    public VM_PreviewImage(System.Windows.Media.Imaging.BitmapSource image, string source)
    {
        Image = image;
        Source = source;
    }
    //public Image Image { get; set; }
    public System.Windows.Media.Imaging.BitmapSource Image { get; set; }
    public string Source { get; set; }
}