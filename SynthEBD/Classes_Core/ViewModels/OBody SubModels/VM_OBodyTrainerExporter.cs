using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class VM_OBodyTrainerExporter : VM
{
    private readonly Func<VM_SettingsOBody> _parentVM;

    public VM_OBodyTrainerExporter(Func<VM_SettingsOBody> parentVM)
    {
        _parentVM = parentVM;
    }
    public ObservableCollection<VM_SelectableBodySlidePlaceHolder> SelectedBodySlides { get; set; } = new();
    public ObservableCollection<VM_SelectableSlider> SelectedSliders { get; set; } = new();

    public void Reinitialize()
    {
        var parentVM = _parentVM();
        SelectedBodySlides.Clear();
        foreach (var bs in parentVM.BodySlidesUI.CurrentlyDisplayedBodySlides)
        {
            var newShell = new VM_SelectableBodySlidePlaceHolder(bs);
            if (bs.AssociatedModel.SliderGroup == parentVM.AnnotatorUI.SelectedSliderGroup)
            {
                newShell.IsSelected = true;
            }
        }
        SelectedBodySlides.Sort(x => x.IsSelected, false);
    }

    private void RefreshAvaliableSliderNames()
    {
        HashSet<BodySlideSlider> availableSliders = SelectedBodySlides.Where(x => x.IsSelected)
            .Select(x => x.Master.AssociatedModel)
            .SelectMany(x => x.SliderValues)
            .Select(x => x.Value)
            .ToHashSet();

        for (int i = 0; i < SelectedSliders.Count; i++)
        {
            if (!availableSliders.Contains(SelectedSliders[i].SubscribedSlider))
            {
                SelectedSliders.RemoveAt(i);
                i--;
            }
        }

        var existingSliders = SelectedSliders.Select(x => x.SubscribedSlider).ToHashSet();
        foreach (var slider in availableSliders.Where(x => !existingSliders.Contains(x)))
        {
            SelectedSliders.Add(new(slider));
        }
    }
}

public class VM_SelectableBodySlidePlaceHolder : VM
{
    public VM_SelectableBodySlidePlaceHolder(VM_BodySlidePlaceHolder master)
    {
        Master = master;
    }

    public VM_BodySlidePlaceHolder Master { get; }
    public bool IsSelected { get; set; }
}

public class VM_SelectableSlider : VM
{
    public VM_SelectableSlider(BodySlideSlider slider)
    {
        SubscribedSlider = slider;
    }
    public BodySlideSlider SubscribedSlider { get; }
    public bool IsSelected { get; set; }
}

