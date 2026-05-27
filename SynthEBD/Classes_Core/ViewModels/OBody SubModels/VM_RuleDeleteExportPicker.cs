using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SynthEBD;

/// <summary>VM for <c>Window_RuleDeleteExportPicker</c> — the modal opened by
/// <c>Ctrl+Shift+Alt+P</c> on the BodyTypeProfileEditor's Rules tab. Displays a three-level
/// tree (Category → Value → individual rule) of every rule in the profile, pre-marking
/// rules under the currently-selected main-tab TreeView node as the patch's add/edit set.
/// Each leaf carries an independent "exclude from add/edit" toggle (so the user can
/// refine the pre-seeded list without backing out) and a "mark for deletion" checkbox.
/// On OK, callers read <see cref="AddEditModels"/> and <see cref="DeleteIds"/> to build
/// the final <c>RulesExportPayload</c>.</summary>
public class VM_RuleDeleteExportPicker : VM
{
    private readonly List<VM_RuleDeletePickerRuleNode> _leaves = new();

    public VM_RuleDeleteExportPicker(IEnumerable<VM_MeasurementRule> allRules,
                                     IEnumerable<VM_MeasurementRule> addEditSeed)
    {
        var seedSet = new HashSet<VM_MeasurementRule>(
            (addEditSeed ?? Enumerable.Empty<VM_MeasurementRule>()).Where(r => r != null));

        // Group by (Category, Value) so the tree mirrors the main Rules tab's layout.
        // Ordinal sort matches the main RebuildRuleTree convention.
        var byCategory = (allRules ?? Enumerable.Empty<VM_MeasurementRule>())
            .Where(r => r != null)
            .GroupBy(r => r.DescriptorCategory ?? "", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var catGroup in byCategory)
        {
            var catNode = new VM_RuleDeletePickerCategoryNode(catGroup.Key);
            var byValue = catGroup
                .GroupBy(r => r.DescriptorValue ?? "", StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var valGroup in byValue)
            {
                var valNode = new VM_RuleDeletePickerValueNode(valGroup.Key);
                int siblingIndex = 1;
                foreach (var rule in valGroup)
                {
                    var leaf = new VM_RuleDeletePickerRuleNode(rule, siblingIndex)
                    {
                        IsInAddEditSet = seedSet.Contains(rule),
                    };
                    leaf.PropertyChanged += OnLeafToggled;
                    valNode.Children.Add(leaf);
                    _leaves.Add(leaf);
                    siblingIndex++;
                }
                catNode.Children.Add(valNode);
            }
            Categories.Add(catNode);
        }

        RecomputeSummary();

        OkCommand = new RelayCommand(canExecute: _ => true, execute: _ =>
        {
            Confirmed = true;
            RequestClose?.Invoke(true);
        });
        CancelCommand = new RelayCommand(canExecute: _ => true, execute: _ =>
        {
            Confirmed = false;
            RequestClose?.Invoke(false);
        });
    }

    public ObservableCollection<VM_RuleDeletePickerCategoryNode> Categories { get; } = new();

    /// <summary>Live count of leaves whose <c>IsInAddEditSet && !IsExcludedFromAddEdit</c>.</summary>
    public int AddEditCount { get; private set; }
    /// <summary>Live count of leaves with <c>IsMarkedForDeletion == true</c>.</summary>
    public int DeleteCount { get; private set; }
    public string SummaryText => $"{AddEditCount} add/edit, {DeleteCount} delete";

    /// <summary>True after the user clicks OK; remains false on Cancel / window-close. The
    /// caller short-circuits on Confirmed=false rather than the dialog result alone so a
    /// window-X close also reads as "cancel" even if DialogResult got set by something else.</summary>
    public bool Confirmed { get; private set; }

    public RelayCommand OkCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>Raised when the user clicks OK or Cancel — argument is true for OK, false for
    /// Cancel. The hosting code-behind subscribes and sets DialogResult + Close().</summary>
    public event Action<bool>? RequestClose;

    /// <summary>Snapshot of the rule models the user wants to add or replace via the patch.
    /// Computed at read-time so a caller can re-read after a late edit if needed.</summary>
    public IReadOnlyList<MeasurementRule> AddEditModels =>
        _leaves
            .Where(l => l.IsInAddEditSet && !l.IsExcludedFromAddEdit)
            .Select(l => l.Rule.DumpToModel())
            .ToList();

    /// <summary>Snapshot of rule Ids the user marked for deletion.</summary>
    public IReadOnlyList<string> DeleteIds =>
        _leaves
            .Where(l => l.IsMarkedForDeletion)
            .Select(l => l.Rule.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

    private void OnLeafToggled(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_RuleDeletePickerRuleNode.IsExcludedFromAddEdit)
            || e.PropertyName == nameof(VM_RuleDeletePickerRuleNode.IsMarkedForDeletion))
        {
            RecomputeSummary();
        }
    }

    private void RecomputeSummary()
    {
        int ae = 0, del = 0;
        foreach (var leaf in _leaves)
        {
            if (leaf.IsInAddEditSet && !leaf.IsExcludedFromAddEdit) ae++;
            if (leaf.IsMarkedForDeletion) del++;
        }
        AddEditCount = ae;
        DeleteCount = del;
    }
}

/// <summary>Category-level node in the picker tree. Mirrors <see cref="VM_RuleTreeCategoryNode"/>'s
/// shape but with rule-leaf children (this picker shows three levels, the main Rules tab only two).</summary>
public class VM_RuleDeletePickerCategoryNode : VM
{
    public VM_RuleDeletePickerCategoryNode(string category)
    {
        Category = category;
    }

    public string Category { get; }
    public ObservableCollection<VM_RuleDeletePickerValueNode> Children { get; } = new();
    public bool IsExpanded { get; set; } = true;
    public string DisplayLabel => string.IsNullOrEmpty(Category) ? "(no category)" : Category;
}

/// <summary>Value-level node in the picker tree. Mirrors <see cref="VM_RuleTreeValueNode"/>'s
/// shape; children are individual rule leaves (multiple rules may share a (Category, Value)).</summary>
public class VM_RuleDeletePickerValueNode : VM
{
    public VM_RuleDeletePickerValueNode(string value)
    {
        Value = value;
    }

    public string Value { get; }
    public ObservableCollection<VM_RuleDeletePickerRuleNode> Children { get; } = new();
    public bool IsExpanded { get; set; } = true;
    public string DisplayLabel => string.IsNullOrEmpty(Value) ? "(no value)" : Value;
}

/// <summary>Leaf node in the picker tree — one per <see cref="VM_MeasurementRule"/>. Carries
/// the two user-editable booleans (exclude-from-add-edit, mark-for-deletion) and a read-only
/// badge (in-add-edit-set) seeded from the main tab's selection.</summary>
public class VM_RuleDeletePickerRuleNode : VM
{
    public VM_RuleDeletePickerRuleNode(VM_MeasurementRule rule, int siblingIndex)
    {
        Rule = rule;
        // Compose a label distinct enough to disambiguate two rules with the same (Category,
        // Value): sibling index, gender, draft flag, and the number of OR groups. The full
        // condition text would be too long for a tree row.
        string draftTag = rule.IsDraft ? " · Draft" : "";
        DisplayLabel = $"#{siblingIndex} · {rule.Gender}{draftTag} · {rule.Groups.Count} group(s)";
    }

    public VM_MeasurementRule Rule { get; }
    public string DisplayLabel { get; }

    /// <summary>True when this rule was in the main-tab TreeView node's set at the time the
    /// picker opened. Read-only badge — the user toggles inclusion via
    /// <see cref="IsExcludedFromAddEdit"/>, not this field.</summary>
    public bool IsInAddEditSet { get; set; }

    /// <summary>User toggle: when true, drop this rule from the patch's add/edit list. Only
    /// meaningful when <see cref="IsInAddEditSet"/> is true (the XAML hides the checkbox
    /// otherwise so it can't be toggled in isolation).</summary>
    public bool IsExcludedFromAddEdit { get; set; }

    /// <summary>User toggle: when true, append this rule's Id to the patch's
    /// <c>RulesToDelete</c> list. Independent of the add/edit flags — a rule can be both
    /// included in add/edit and marked for deletion, in which case the patch loader resolves
    /// the conflict in favor of add/edit (see <c>ApplyRulesPatch</c> in
    /// <c>VM_BodyTypeProfileEditor</c>).</summary>
    public bool IsMarkedForDeletion { get; set; }
}
