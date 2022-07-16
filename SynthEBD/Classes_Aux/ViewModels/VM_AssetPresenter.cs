using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

            this.WhenAnyValue(x => x.AssetPack.DisplayedSubgroup).Subscribe(x => UpdatePreviewImages(AssetPack));
        }

        public VM_SettingsTexMesh ParentUI { get; private set; }
        public VM_AssetPack AssetPack { get; set; }
        public ObservableCollection<VM_PreviewImage> PreviewImages { get; set; } = new();

        public async void UpdatePreviewImages(VM_AssetPack source)
        {
            //this.PreviewImages.Clear();
            this.PreviewImages = new ObservableCollection<VM_PreviewImage>();
            if (source.DisplayedSubgroup == null) { return; }
            foreach (var sourcedFile in source.DisplayedSubgroup.ImagePaths)
            {
                Pfim.IImage image = await Task.Run(() => Pfim.Pfim.FromFile(sourcedFile.Path));
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
            return;
        }
    }
}
