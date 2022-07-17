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
                i.Dispose();
            }
            this.PreviewImages.Clear();
            this.PreviewImages = new ObservableCollection<VM_PreviewImage>();
            Graphics.ClearHandles();

            if (source == null || source.DisplayedSubgroup == null) { return; }
            foreach (var sourcedFile in source.DisplayedSubgroup.ImagePaths)
            {
                try
                {
                    if (PreviewImages.Select(x => x.Source).Contains(sourcedFile.Source)) { continue; }
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
                    string errorStr = "Failed to load preview image: " + sourcedFile.Path + Environment.NewLine + ExceptionLogger.GetExceptionStack(ex, "");
                    CustomMessageBox.DisplayNotificationOK("Error Displaying Preview Image", errorStr);
                }
            }
            return;
        }
    }
}
