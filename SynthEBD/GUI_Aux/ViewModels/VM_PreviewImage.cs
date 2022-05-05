using System.Windows.Controls;
using Noggog.WPF;

namespace SynthEBD;

public class VM_PreviewImage : ViewModel
{
    public VM_PreviewImage(Image image, string source)
    {
        Image = image;
        Source = source;
    }
    public Image Image { get; set; }
    public string Source { get; set; }
}