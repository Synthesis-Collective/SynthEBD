using System.Windows.Controls;

namespace SynthEBD;

/// <summary>
/// Code-behind for <see cref="VM_SuggestRulesPanel"/>. Pure binding host -- per-suggestion
/// rendering is fully data-driven via <see cref="VM_SuggestRulesPanel.RuleSuggestions"/>.
/// </summary>
public partial class UC_SuggestRulesPanel : UserControl
{
    public UC_SuggestRulesPanel()
    {
        InitializeComponent();
    }
}
