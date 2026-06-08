using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B31: VM_SelectableSubgroupShell did not inherit VM, so PropertyChanged.Fody wove no
/// INotifyPropertyChanged onto it -- a two-way "IsChecked" checkbox binding could not reflect a
/// programmatic IsSelected change (latent if a Select-All button is ever added, as sibling windows
/// have). Its null-subgroup guard also wrote to the by-value defaultSelectedStatus parameter instead
/// of IsSelected. The shell now inherits VM; these tests pin the INPC contract (the event did not
/// exist before the fix) and the null-safe guard. A null subgroup is passed so no heavy
/// VM_SubgroupPlaceHolder graph is needed -- the guard returns before touching it.
/// </summary>
public class VM_SelectableSubgroupShellTests
{
    [Fact]
    public void IsSelected_RaisesPropertyChanged()
    {
        var shell = new VM_SelectableSubgroupShell(null!, false, SubgroupLabelFormat.ID);

        bool raised = false;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VM_SelectableSubgroupShell.IsSelected)) raised = true;
        };

        shell.IsSelected = true;

        raised.Should().BeTrue();
    }

    [Fact]
    public void NullSubgroup_ConstructsSafely_AndIsNotSelected()
    {
        // defaultSelectedStatus: true is intentionally overridden by the null guard, which sets IsSelected = false.
        var shell = new VM_SelectableSubgroupShell(null!, true, SubgroupLabelFormat.ID);

        shell.Subgroup.Should().BeNull();
        shell.IsSelected.Should().BeFalse();
    }
}
