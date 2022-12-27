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
using Pfim;

namespace SynthEBD
{
    public class VM_AssetPresenter : VM
    {
        private readonly Logger _logger;
        public VM_AssetPresenter(VM_SettingsTexMesh parent, Logger logger)
        {
            ParentUI = parent;

            _logger = logger;

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
        private const ulong ByteLimit = 157286400; // minimum available RAM for image preview to function (in bytes)

        public async void UpdatePreviewImages(VM_AssetPack source)
        {
            #region Try to free memory as completely as possible before loading more images
            foreach (var i in PreviewImages)
            {
                i.Dispose();
            }
            this.PreviewImages.Clear();
            this.PreviewImages = new ObservableCollection<VM_PreviewImage>();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            #endregion

            if (source == null || source.DisplayedSubgroup == null) { return; }
            foreach (var sourcedImagePath in source.DisplayedSubgroup.ImagePaths)
            {
                var availableRAM = new Microsoft.VisualBasic.Devices.ComputerInfo().AvailablePhysicalMemory;
                if (availableRAM <= ByteLimit) { continue; }
                if (!sourcedImagePath.SourceChain.Contains(AssetPack.DisplayedSubgroup)) { continue; } // stop loading images from a previous subgroup if a different one is selected

                try
                {                   
                    using (IImage image = await Task.Run(() => Pfimage.FromFile(sourcedImagePath.Path)))
                    {
                        if (image != null)
                        {
                            var bmp = ImagePreviewHandler.ResizeIImageAsBitMap(image, ParentUI.MaxPreviewImageSize);
                            var bmpSource = ImagePreviewHandler.CreateBitmapSourceFromGdiBitmap(bmp); // Try setting xaml to display bitmap directly
                            if (!sourcedImagePath.SourceChain.Contains(AssetPack.DisplayedSubgroup)) { continue; } // Intentional duplication: Pfim.FromFile() takes some time to execute and may already be in progress when the user changes the active subgroup, leading to the last previous PreviewImage loading erroneously
                            PreviewImages.Add(new VM_PreviewImage(bmpSource, sourcedImagePath.PrimarySource));
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorStr = "Failed to load preview image from Subgroup " + sourcedImagePath.PrimarySource + " : " + sourcedImagePath.Path + Environment.NewLine + ExceptionLogger.GetExceptionStack(ex, "");
                    _logger.LogMessage(errorStr);
                }
            }
            return;
        }
    }
}
