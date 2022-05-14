using System.Windows.Controls;

namespace SynthEBD;

public class VM_PreviewImage : VM
{
    public VM_PreviewImage(Image image, string source)
    {
        Image = image;
        Source = source;
    }
    public Image Image { get; set; }
    public string Source { get; set; }
}