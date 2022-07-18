using Pfim;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SynthEBD;

public class Graphics
{
    public class ImagePathWithSource
    {
        public ImagePathWithSource(string path, string source)
        {
            Path = path;
            Source = source;
        }
        public string Path { get; set; }
        public string Source { get; set; }

        public ImagePathWithSource Clone()
        {
            return new ImagePathWithSource(Path, Source);
        }

        public override bool Equals(object obj)
        {
            var item = obj as ImagePathWithSource;
            if (item == null) { return false; }
            return item.Path == Path && item.Source == Source;
        }

        public override int GetHashCode()
        {
            return (Path + Source).GetHashCode();
        }

        public static string GetSource(VM_Subgroup subgroup)
        {
            return subgroup.ID + ": " + subgroup.Name;
        }
    }

    public static IEnumerable<Image> WpfImage(IImage image)
    {
        BitmapSource bsource = BitmapSource.Create(
    image.Width,
    image.Height,
    96,
    96,
    PixelFormat(image),
    null,
    image.Data,
    image.Stride);

        var maxDimension = Math.Max(image.Width, image.Height);
        if (maxDimension > 2048)
        {
            double scaleFactor = 2048 / maxDimension;
            var scaledBitmap = new TransformedBitmap(bsource, new ScaleTransform(scaleFactor, scaleFactor));

            yield return new Image
            {
                Source = scaledBitmap.Source
            };
        }
        else
        {
            yield return new Image
            {
                Source = bsource
            };
        }
    }

    private static PixelFormat PixelFormat(IImage image)
    {
        switch (image.Format)
        {
            case ImageFormat.Rgb24:
                return PixelFormats.Bgr24;
            case ImageFormat.Rgba32:
                return PixelFormats.Bgra32;
            case ImageFormat.Rgb8:
                return PixelFormats.Gray8;
            case ImageFormat.R5g5b5a1:
            case ImageFormat.R5g5b5:
                return PixelFormats.Bgr555;
            case ImageFormat.R5g6b5:
                return PixelFormats.Bgr565;
            default:
                throw new Exception($"Unable to convert {image.Format} to WPF PixelFormat");
        }
    }
}