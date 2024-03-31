using Pfim;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SynthEBD;

public class VM_ConfigRemapperTextureComparer : VM
{
    public VM_ConfigRemapperTextureComparer(string texture1Path, string texture2Path)
    {
        Texture1Path = texture1Path;
        Texture2Path = texture2Path;

        InitializeImage(Texture1Path, TexIndexToggle.Texture1); 
        InitializeImage(Texture2Path, TexIndexToggle.Texture2);

        if (!File.Exists(Texture1Path))
        {
            DisplayText = "Texture 1 does not exist";
            DisplayTextColor = CommonColors.Red;
        }
        else if (!File.Exists(Texture2Path))
        {
            DisplayText = "Texture 2 does not exist";
            DisplayTextColor = CommonColors.Red;
        }
        else
        {
            var hash1 = MiscFunctions.CalculateMD5(Texture1Path);
            var hash2 = MiscFunctions.CalculateMD5(Texture2Path);
            if (hash1 == hash2)
            {
                DisplayText = "Textures are identical";
                DisplayTextColor = CommonColors.Green;
            }
            else
            {
                DisplayText = "Textures are different";
                DisplayTextColor = CommonColors.Yellow;
            }
        }
    }
    public string Texture1Path { get; set; }
    public string Texture2Path { get; set; }

    public System.Windows.Media.Imaging.BitmapSource? Image1 { get; set; }
    public System.Windows.Media.Imaging.BitmapSource? Image2 { get; set; }
    public string DisplayText { get; set; } = string.Empty;
    public SolidColorBrush DisplayTextColor { get; set; } = new(Colors.White);

    private async void InitializeImage(string path, TexIndexToggle whichTexture)
    {
        if (!File.Exists(path)) { return; }
        if (!path.EndsWith("dds", StringComparison.OrdinalIgnoreCase)) { return; }

        using (IImage image = await Task.Run(() => Pfimage.FromFile(path)))
        {
            if (image != null)
            {
                var bmp = ImagePreviewHandler.ResizeIImageAsBitMap(image, 2048); // pass resolution
                switch(whichTexture)
                {
                    case TexIndexToggle.Texture1: Image1 = ImagePreviewHandler.CreateBitmapSourceFromGdiBitmap(bmp); break; // Try setting xaml to display bitmap directly
                    case TexIndexToggle.Texture2: Image2 = ImagePreviewHandler.CreateBitmapSourceFromGdiBitmap(bmp); break; // Try setting xaml to display bitmap directly
                }
            }
        }
    }

    private BitmapSource DrawFilledRectangle(int x, int y)
    {
        Bitmap bmp = new Bitmap(x, y);
        using (Graphics graph = Graphics.FromImage(bmp))
        {
            Rectangle ImageSize = new Rectangle(0, 0, x, y);
            graph.FillRectangle(System.Drawing.Brushes.White, ImageSize);
        }
        return Convert(bmp);
    }

    public static BitmapSource Convert(System.Drawing.Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

        var bitmapSource = BitmapSource.Create(
            bitmapData.Width, bitmapData.Height,
            bitmap.HorizontalResolution, bitmap.VerticalResolution,
            PixelFormats.Bgr24, null,
            bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

        bitmap.UnlockBits(bitmapData);

        return bitmapSource;
    }

    private enum TexIndexToggle
    {
        Texture1,
        Texture2
    }
}
