using Pfim;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SynthEBD;

public class ImagePreviewHandler
{
    public class ImagePathWithSource
    {
        public ImagePathWithSource(string path, VM_SubgroupPlaceHolder source)
        {
            Path = path;
            PrimarySource = source;
            SourceChain.Add(PrimarySource);
            AddParentToChain(source, SourceChain);
        }
        public string Path { get; set; }
        public HashSet<VM_SubgroupPlaceHolder> SourceChain { get; set; } = new();
        public VM_SubgroupPlaceHolder PrimarySource { get; set; }

        public ImagePathWithSource Clone()
        {
            return new ImagePathWithSource(Path, PrimarySource);
        }

        public void AddParentToChain(VM_SubgroupPlaceHolder subgroup, HashSet<VM_SubgroupPlaceHolder> sourceChain)
        {
            if (subgroup.ParentSubgroup is not null)
            {
                sourceChain.Add(subgroup.ParentSubgroup);
                AddParentToChain(subgroup.ParentSubgroup, sourceChain);
            }
        }
    }

    public static System.Drawing.Bitmap ResizeIImageAsBitMap(IImage image, int targetDimension)
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
        if (maxDimension > targetDimension)
        {
            double scaleFactor = (double) targetDimension / maxDimension;
            int newWidth = (int)(image.Width * scaleFactor);
            int newHeight = (int)(image.Height * scaleFactor);
            System.Drawing.Size size = new System.Drawing.Size(newWidth, newHeight);

            var resized = ResizeImage(bsource, size, DrawingPixelFormat(image));

            return resized;
        }
        else
        {
            return BitmapSourceToBitmap2(bsource, DrawingPixelFormat(image));
        }
    }

    public static BitmapSource CreateBitmapSourceFromGdiBitmap(System.Drawing.Bitmap bitmap)
    {
        if (bitmap == null)
            throw new ArgumentNullException("bitmap");

        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);

        var bitmapData = bitmap.LockBits(
            rect,
            System.Drawing.Imaging.ImageLockMode.ReadWrite,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            var size = (rect.Width * rect.Height) * 4;

            return BitmapSource.Create(
                bitmap.Width,
                bitmap.Height,
                bitmap.HorizontalResolution,
                bitmap.VerticalResolution,
                PixelFormats.Bgra32,
                null,
                bitmapData.Scan0,
                size,
                bitmapData.Stride);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    //https://stackoverflow.com/questions/10839358/resize-bitmap-image
    
    public static System.Drawing.Bitmap ResizeImage(BitmapSource bmSource, System.Drawing.Size outputSize, System.Drawing.Imaging.PixelFormat pixelFormat)
    {
        var bitMap = BitmapSourceToBitmap2(bmSource, pixelFormat);
        try
        {
            System.Drawing.Bitmap b = new System.Drawing.Bitmap(outputSize.Width, outputSize.Height);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage((System.Drawing.Image)b))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Default;
                g.DrawImage(bitMap, 0, 0, outputSize.Width, outputSize.Height);
            }
            return b;
        }
        catch
        {
            Console.WriteLine("Bitmap could not be resized");
            return bitMap;
        }
    }

    //https://stackoverflow.com/questions/5689674/c-sharp-convert-wpf-image-source-to-a-system-drawing-bitmap
    public static System.Drawing.Bitmap BitmapSourceToBitmap2(ImageSource imageSource, System.Drawing.Imaging.PixelFormat pixelFormat)
    {
        BitmapSource srs = (BitmapSource)imageSource;
        int width = srs.PixelWidth;
        int height = srs.PixelHeight;
        int stride = width * ((srs.Format.BitsPerPixel + 7) / 8);
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(height * stride);
            srs.CopyPixels(new System.Windows.Int32Rect(0, 0, width, height), ptr, height * stride, stride);
            using (var btm = new System.Drawing.Bitmap(width, height, stride, pixelFormat, ptr))
            {
                // Clone the bitmap so that we can dispose it and
                // release the unmanaged memory at ptr
                return new System.Drawing.Bitmap(btm);
            }
        }
        finally
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(ptr);
        }
    }

    private static System.Drawing.Imaging.PixelFormat DrawingPixelFormat(IImage image)
    {
        switch (image.Format)
        {
            case ImageFormat.Rgb24:
                return System.Drawing.Imaging.PixelFormat.Format24bppRgb;
            case ImageFormat.Rgba32:
                return System.Drawing.Imaging.PixelFormat.Format32bppArgb;
            case ImageFormat.Rgb8:
                return System.Drawing.Imaging.PixelFormat.Format8bppIndexed;
            case ImageFormat.R5g5b5a1:
            case ImageFormat.R5g5b5:
                return System.Drawing.Imaging.PixelFormat.Format16bppRgb555;
            case ImageFormat.R5g6b5:
                return System.Drawing.Imaging.PixelFormat.Format16bppRgb565;
            default:
                throw new Exception($"Unable to convert {image.Format} to WPF PixelFormat");
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