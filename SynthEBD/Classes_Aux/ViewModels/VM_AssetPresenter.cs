using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_AssetPresenter : VM
    {
        public VM_AssetPresenter(VM_SettingsTexMesh parent)
        {
            ParentUI = parent;

            this.WhenAnyValue(
                x => x.AssetPack.DisplayedSubgroup.ImagePaths,
                x => x.ParentUI.bShowPreviewImages,
                // Just pass along the signal, don't care about the triggering values
                (_, _) => Unit.Default)
            .Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdatePreviewImages(AssetPack));
        }

        public VM_SettingsTexMesh ParentUI { get; private set; }
        public VM_AssetPack AssetPack { get; set; }
        public ObservableCollection<VM_PreviewImage> PreviewImages { get; set; } = new();

        public async void UpdatePreviewImages(VM_AssetPack source)
        {
            foreach (var i in PreviewImages)
            {
                i.Image.Source = System.Windows.Media.Imaging.BitmapImage.Create(
                    2,
                    2,
                    96,
                    96,
                    PixelFormats.Indexed1,
                    new System.Windows.Media.Imaging.BitmapPalette(new List<Color> { Colors.Transparent }),
                    new byte[] { 0, 0, 0, 0 },
                    1);
                i.Dispose();
            }
            this.PreviewImages.Clear();
            this.PreviewImages = new ObservableCollection<VM_PreviewImage>();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (source == null || source.DisplayedSubgroup == null) { return; }
            foreach (var sourcedFile in source.DisplayedSubgroup.ImagePaths)
            {
                try
                {                   
                    using (Pfim.IImage image = await Task.Run(() => Pfim.Pfim.FromFile(sourcedFile.Path)))
                    {
                        if (image != null)
                        {
                            var converted = Graphics.WpfImage(image).FirstOrDefault();
                            if (converted != null)
                            {
                                converted.Stretch = Stretch.Uniform;
                                converted.StretchDirection = System.Windows.Controls.StretchDirection.DownOnly;
                                PreviewImages.Add(new VM_PreviewImage(converted, sourcedFile.Source));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorStr = "Failed to load preview image from Subgroup " + sourcedFile.Source + " : " + sourcedFile.Path + Environment.NewLine + ExceptionLogger.GetExceptionStack(ex, "");
                    Logger.LogMessage(errorStr);
                }
            }
            return;
        }
    }
}
