using Pfim;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SynthEBD;

/// <summary>Helpers for decoding and resizing asset preview images (notably Pfim-decoded DDS textures)
/// into WPF/GDI bitmaps, and a small model pairing an image path with the subgroup(s) it came from.</summary>
public class ImagePreviewHandler
{
    /// <summary>Associates an image file path with the <see cref="VM_SubgroupPlaceHolder"/> that
    /// references it (its <see cref="PrimarySource"/>) plus the full ancestor chain of subgroups,
    /// so the preview UI can attribute an image to every subgroup that contributes it.</summary>
    public class ImagePathWithSource
    {
        /// <summary>Captures the image <paramref name="path"/> and its <paramref name="source"/> subgroup,
        /// seeding <see cref="SourceChain"/> with the source and all of its parents.</summary>
        public ImagePathWithSource(string path, VM_SubgroupPlaceHolder source)
        {
            Path = path;
            PrimarySource = source;
            SourceChain.Add(PrimarySource);
            AddParentToChain(source, SourceChain);
        }
        /// <summary>The image file path.</summary>
        public string Path { get; set; }
        /// <summary>The primary source subgroup plus all of its ancestors.</summary>
        public HashSet<VM_SubgroupPlaceHolder> SourceChain { get; set; } = new();
        /// <summary>The subgroup that directly references this image.</summary>
        public VM_SubgroupPlaceHolder PrimarySource { get; set; }

        /// <summary>Returns a new instance with the same path and primary source (chain rebuilt).</summary>
        public ImagePathWithSource Clone()
        {
            return new ImagePathWithSource(Path, PrimarySource);
        }

        /// <summary>Recursively walks <paramref name="subgroup"/>'s parents, adding each to
        /// <paramref name="sourceChain"/>.</summary>
        public void AddParentToChain(VM_SubgroupPlaceHolder subgroup, HashSet<VM_SubgroupPlaceHolder> sourceChain)
        {
            if (subgroup.ParentSubgroup is not null)
            {
                sourceChain.Add(subgroup.ParentSubgroup);
                AddParentToChain(subgroup.ParentSubgroup, sourceChain);
            }
        }
    }

    /// <summary>Converts a Pfim <see cref="IImage"/> to a GDI <see cref="System.Drawing.Bitmap"/>,
    /// downscaling so its largest dimension is at most <paramref name="targetDimension"/> (preserving
    /// aspect ratio). Images already within the limit are converted without resizing.</summary>
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

    /// <summary>Creates a WPF <see cref="BitmapSource"/> (BGRA32) from a GDI
    /// <see cref="System.Drawing.Bitmap"/>. Throws <see cref="ArgumentNullException"/> if
    /// <paramref name="bitmap"/> is null.</summary>
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
    
    /// <summary>Draws <paramref name="bmSource"/> into a new GDI bitmap of <paramref name="outputSize"/>.
    /// On failure logs to the console and returns the unscaled source bitmap.</summary>
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
    /// <summary>Copies the pixels of a WPF <see cref="ImageSource"/> into a GDI
    /// <see cref="System.Drawing.Bitmap"/> via an intermediate unmanaged buffer (which is freed before
    /// returning a managed clone).</summary>
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

    /// <summary>Maps a Pfim <see cref="ImageFormat"/> to the equivalent GDI
    /// <see cref="System.Drawing.Imaging.PixelFormat"/>. Throws for unsupported formats.</summary>
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

    /// <summary>Maps a Pfim <see cref="ImageFormat"/> to the equivalent WPF
    /// <see cref="PixelFormat"/>. Throws for unsupported formats.</summary>
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