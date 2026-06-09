using DynamicData;
using DynamicData.Binding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Noggog;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;

namespace SynthEBD;

/// <summary>
/// View model for the OBody BodySlide annotation-rules panel. Builds and edits per-body-type,
/// per-descriptor slider-classification rules and applies them (via <see cref="BodySlideAnnotator"/>)
/// to assign body-shape descriptors to the loaded BodySlide presets. Round-trips to
/// <see cref="OBodySettings.BodySlideClassificationRules"/>.
/// </summary>
public class VM_BodySlideAnnotator : VM
{
    private readonly PatcherState _patcherState;
    private readonly VM_BodyShapeDescriptorCreationMenu _oBodyDescriptorMenu;
    private readonly VM_BodySlidesMenu _bodySlideMenu;
    private readonly BodySlideAnnotator _bodySlideAnnotator;
    private readonly Logger _logger;

    /// <summary>Autofac factory delegate for <see cref="VM_BodySlideAnnotator"/>.</summary>
    public delegate VM_BodySlideAnnotator Factory(VM_BodyShapeDescriptorCreationMenu oBodyDescriptorMenu, VM_BodySlidesMenu bodySlideMenu, VM_OBodyMiscSettings miscMenu);
    /// <summary>Wires up the ApplyAnnotations command.</summary>
    public VM_BodySlideAnnotator(PatcherState patcherState, VM_BodyShapeDescriptorCreationMenu oBodyDescriptorMenu, VM_BodySlidesMenu bodySlideMenu, VM_OBodyMiscSettings miscMenu, BodySlideAnnotator bodySlideAnnotator, Logger logger)
    {
        _patcherState = patcherState;
        _oBodyDescriptorMenu = oBodyDescriptorMenu;
        _bodySlideMenu = bodySlideMenu;
        _bodySlideAnnotator = bodySlideAnnotator;
        _logger = logger;

        ApplyAnnotationsCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ApplyAnnotations(null, null));
    }

    public string SelectedSliderGroup { get; set; }
    public ObservableCollection<VM_SliderClassificationRulesByBodyType> AnnotationRules { get; set; } = new();

    public VM_SliderClassificationRulesByBodyType DisplayedRuleSet { get; set; }

    public Dictionary<string, ObservableCollection<string>> SliderNamesByGroup { get; set; } = new();
    private List<SliderClassificationRulesByBodyType> _stashedUnloadedBodyTypeRules { get; set; } = new(); // for storing rules for descriptors that a user may have inadvertently removed
    public RelayCommand ApplyAnnotationsCommand { get; }

    /// <summary>Scans all loaded male/female BodySlides to build the slider-name-by-group map, creates a rule-set VM per body-type group, and repopulates the menu's available slider groups.</summary>
    public void InitializeBodySlideInfo()
    {
        SliderNamesByGroup.Clear();
        foreach (var templateVM in _bodySlideMenu.BodySlidesMale.And(_bodySlideMenu.BodySlidesFemale))
        {
            var template = templateVM.AssociatedModel;

            var currentSliderGroup = template.SliderGroup;

            if (currentSliderGroup == null) // can happen for manually renamed bodyslides
            {
                continue;
            }

            if (!SliderNamesByGroup.ContainsKey(currentSliderGroup))
            {
                SliderNamesByGroup.Add(currentSliderGroup, new ObservableCollection<string>());
            }

            var currentSliderNameList = SliderNamesByGroup[currentSliderGroup];

            foreach (var slider in template.SliderValues.Keys)
            {
                if (!currentSliderNameList.Contains(slider))
                {
                    currentSliderNameList.Add(slider);
                }
            }
        }

        foreach (var bodyTypeGroup in SliderNamesByGroup.Keys)
        {
            SliderNamesByGroup[bodyTypeGroup].Sort(x => x, false);
            AnnotationRules.Add(new VM_SliderClassificationRulesByBodyType(_oBodyDescriptorMenu, bodyTypeGroup, SliderNamesByGroup[bodyTypeGroup], this));
        }

        _bodySlideMenu.AvailableSliderGroups.Clear();
        _bodySlideMenu.AvailableSliderGroups.Add(VM_BodySlidesMenu.BodyTypeSelectionAll);
        Noggog.ListExt.AddRange(_bodySlideMenu.AvailableSliderGroups, SliderNamesByGroup.Keys);
    }

    /// <summary>Loads persisted classification rules into the rule-set VMs by body type; rules for body types not present in the current BodySlide set are stashed so they aren't lost on save.</summary>
    public void CopyInFromModel()
    {
        InitializeBodySlideInfo();
        _stashedUnloadedBodyTypeRules.Clear();

        var bodyTypes = SliderNamesByGroup.Keys.ToHashSet().And(_patcherState.OBodySettings.BodySlideClassificationRules.Keys).Distinct().ToArray();

        foreach (var bodyType in bodyTypes)
        {
            SliderClassificationRulesByBodyType rulesByBodyType;
            if (_patcherState.OBodySettings.BodySlideClassificationRules.ContainsKey(bodyType))
            {
                rulesByBodyType = _patcherState.OBodySettings.BodySlideClassificationRules[bodyType];
            }
            else
            {
                rulesByBodyType= new SliderClassificationRulesByBodyType();
            }

            var correspondingVM = AnnotationRules.FirstOrDefault(x => x.BodyTypeGroup == bodyType);
            if (correspondingVM != null)
            {
                correspondingVM.CopyInFromModel(rulesByBodyType);
            }
            else
            {
                _stashedUnloadedBodyTypeRules.Add(rulesByBodyType);
            }
        }
    }

    /// <summary>Serializes the per-body-type rule-set VMs (plus any stashed unloaded rules) into the classification-rules dictionary, warning on duplicate body-type keys.</summary>
    public Dictionary<string, SliderClassificationRulesByBodyType> DumpToModel()
    {
        Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules = new();
        foreach (var rule in AnnotationRules)
        {
            if (!bodySlideClassificationRules.ContainsKey(rule.BodyTypeGroup))
            {
                bodySlideClassificationRules.Add(rule.BodyTypeGroup, rule.DumpToModel());
            }
            else
            {
                _logger.LogError("Warning: Saving Body Slide Annotation Rules from UI: Body Type " + rule.BodyTypeGroup + " has multiple copies in UI");
            }
        }
        foreach (var stashedRule in _stashedUnloadedBodyTypeRules)
        {
            if (!bodySlideClassificationRules.ContainsKey(stashedRule.BodyTypeGroup))
            {
                bodySlideClassificationRules.Add(stashedRule.BodyTypeGroup, stashedRule);
            }
            else
            {
                _logger.LogError("Warning: Saving Body Slide Annotation Rules from Stashed Rules: Body Type " + stashedRule.BodyTypeGroup + " appears to already exist");
            }
        }
        return bodySlideClassificationRules;
    }

    /// <summary>Runs the rule-based annotator over the loaded BodySlides (optionally filtered to one slider group and/or one descriptor category), refreshes border colors, and posts a status notification.</summary>
    public void ApplyAnnotations(string? specifiedSliderGroup, string? specifiedDescriptorCategory)
    {
        var targetVMs = _bodySlideMenu.BodySlidesMale.And(_bodySlideMenu.BodySlidesFemale).ToList();

        if (specifiedSliderGroup != null)
        {
            targetVMs = targetVMs.Where(x => x.AssociatedModel.SliderGroup == specifiedSliderGroup).ToList();
        }

        _bodySlideAnnotator.AnnotateBodySlides(targetVMs.Select(x => x.AssociatedModel).ToList(), this.DumpToModel(), _oBodyDescriptorMenu.DumpToViewModels().Flatten().Select(x => x.ID).ToHashSet(), true, specifiedDescriptorCategory);

        foreach (var targetVM in targetVMs)
        {
            if (targetVM.AssociatedModel.HasAnyDescriptors())
            {
                targetVM.InitializeBorderColor();
            }
        }

        _logger.CallTimedNotifyStatusUpdateAsync("Auto-Applied Annotations", 3, CommonColors.Yellow);
    }
}

/// <summary>
/// View model of a <see cref="SliderClassificationRulesByBodyType"/>: the full set of descriptor
/// classification rule-sets for one body type (e.g. CBBE, HIMBO).
/// </summary>
[DebuggerDisplay("{SliderGroup}: Rule List for {DescriptorClassifiers.Count} Descriptors")]
public class VM_SliderClassificationRulesByBodyType : VM // contains a list of rules for each descriptor
{
    /// <summary>Creates a per-descriptor rule-set VM for each descriptor shell in the subscribed menu and wires the ApplyAnnotations command scoped to this body type.</summary>
    public VM_SliderClassificationRulesByBodyType(VM_BodyShapeDescriptorCreationMenu subscribedMenu, string bodyTypeGroup, ObservableCollection<string> availableSliderNames, VM_BodySlideAnnotator annotatorVM)
    {
        _subscribedDescriptorMenu = subscribedMenu;

        BodyTypeGroup = bodyTypeGroup;

        foreach (var descriptorShell in _subscribedDescriptorMenu.TemplateDescriptors)
        {
            DescriptorClassifiers.Add(new(descriptorShell, availableSliderNames, annotatorVM, this));
        }

        ApplyAnnotationsCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => annotatorVM.ApplyAnnotations(BodyTypeGroup, null)
        );
    }
    public string BodyTypeGroup { get; } // E.g. HIMBO, CBBE, etc
    public ObservableCollection<VM_DescriptorClassificationRuleSet> DescriptorClassifiers { get; set; } = new();
    public VM_DescriptorClassificationRuleSet SelectedDescriptor { get; set; }
    private VM_BodyShapeDescriptorCreationMenu _subscribedDescriptorMenu { get; }
    public RelayCommand ApplyAnnotationsCommand { get; }
    private List<DescriptorClassificationRuleSet> _stashedUnloadedDescriptorRules { get; set; } = new(); // for storing rules for descriptors that a user may have inadvertently removed

    /// <summary>Loads each descriptor rule-set from the model into its matching child VM; rule-sets for descriptors not present in the UI are stashed to avoid loss on save.</summary>
    public void CopyInFromModel(SliderClassificationRulesByBodyType model)
    {
        _stashedUnloadedDescriptorRules.Clear();

        foreach (var perDescriptorRuleSet in model.DescriptorClassifiers)
        {
            var correspondingVM = DescriptorClassifiers.FirstOrDefault(x => x.DescriptorCategory == perDescriptorRuleSet.DescriptorCategory);
            if (correspondingVM != null)
            {
                correspondingVM.CopyInFromModel(perDescriptorRuleSet);
            }
            else
            {
                _stashedUnloadedDescriptorRules.Add(perDescriptorRuleSet);
            }
        }
    }

    /// <summary>Serializes the child descriptor rule-set VMs (plus stashed unloaded rules) into a <see cref="SliderClassificationRulesByBodyType"/> model.</summary>
    public SliderClassificationRulesByBodyType DumpToModel()
    {
        SliderClassificationRulesByBodyType model = new();
        model.BodyTypeGroup = BodyTypeGroup;
        model.DescriptorClassifiers = DescriptorClassifiers.Select(x => x.DumpToModel()).ToList();
        model.DescriptorClassifiers.AddRange(_stashedUnloadedDescriptorRules);
        return model;
    }
}

/// <summary>
/// View model of a <see cref="DescriptorClassificationRuleSet"/>: the classification rules and default
/// value for a single descriptor category (e.g. "Build") within one body type.
/// </summary>
[DebuggerDisplay("{DescriptorCategory}: {RuleList.Count} Rule Groups")]
public class VM_DescriptorClassificationRuleSet : VM // rule set for a given descriptor
{
    /// <summary>Captures the descriptor category/values, refreshes the available default-value list, and wires the Add-rule-group and ApplyAnnotations (scoped to this category) commands.</summary>
    public VM_DescriptorClassificationRuleSet(VM_BodyShapeDescriptorShell subscribedDescriptorShell, ObservableCollection<string> availableSliderNames, VM_BodySlideAnnotator annotatorVM, VM_SliderClassificationRulesByBodyType parentVM)
    {
        _subscribedDescriptorShell = subscribedDescriptorShell;
        SubscribedDescriptors = subscribedDescriptorShell.Descriptors;
        DescriptorCategory = _subscribedDescriptorShell.Category;
        AvailableSliderNames = availableSliderNames;

        RefreshAvailableDefaults();

        AddNewRuleGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var newRule = new VM_DescriptorAssignmentRuleSet(subscribedDescriptorShell, AvailableSliderNames, RuleList);
                var newRuleGroup = new VM_AndGatedSliderRuleGroup(AvailableSliderNames, newRule.RuleListORlogic);
                newRuleGroup.RuleListANDlogic.Add(new(AvailableSliderNames, newRuleGroup.RuleListANDlogic));
                newRule.RuleListORlogic.Add(newRuleGroup);
                RuleList.Add(newRule);
            } 
        );

        ApplyAnnotationsCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => annotatorVM.ApplyAnnotations(parentVM.BodyTypeGroup, DescriptorCategory)
        );
    }

    private VM_BodyShapeDescriptorShell _subscribedDescriptorShell { get; }
    public ObservableCollection<VM_BodyShapeDescriptor> SubscribedDescriptors { get; }
    public ObservableCollection<IHasValueString> AvailableDefaultDescriptors { get; set; } = new();
    public ObservableCollection<string> AvailableSliderNames { get; }
    public string DescriptorCategory { get; }
    public IHasValueString DefaultDescriptorValue { get; set; }
    public ObservableCollection<VM_DescriptorAssignmentRuleSet> RuleList { get; set; } = new();
    public RelayCommand ApplyAnnotationsCommand { get; }
    public RelayCommand AddNewRuleGroup { get; }

    /// <summary>Loads the default descriptor value and rebuilds the rule list from the model.</summary>
    public void CopyInFromModel(DescriptorClassificationRuleSet model)
    {
        DefaultDescriptorValue = _subscribedDescriptorShell.Descriptors.FirstOrDefault(x => x.Value == model.DefaultDescriptorValue);

        RuleList.Clear();
        foreach (var rulesForDescriptor in model.RuleList)
        {
            var rulesForDescriptorVM = new VM_DescriptorAssignmentRuleSet(_subscribedDescriptorShell, AvailableSliderNames, RuleList);
            rulesForDescriptorVM.CopyInFromModel(rulesForDescriptor);
            RuleList.Add(rulesForDescriptorVM);
        }
    }

    /// <summary>Serializes the category, default value, and rule list into a <see cref="DescriptorClassificationRuleSet"/> model.</summary>
    public DescriptorClassificationRuleSet DumpToModel()
    {
        DescriptorClassificationRuleSet model = new();
        model.DescriptorCategory = DescriptorCategory;
        model.DefaultDescriptorValue = DefaultDescriptorValue?.Value ?? String.Empty;
        model.RuleList = RuleList.Select(x => x.DumpToModel()).ToList();
        return model;
    }

    /// <summary>Syncs <see cref="AvailableDefaultDescriptors"/> with the subscribed descriptors, keeping a leading empty/dummy option and dropping entries no longer present.</summary>
    private void RefreshAvailableDefaults()
    {
        if (!AvailableDefaultDescriptors.Any(x => x.Value.IsNullOrEmpty()))
        {
            AvailableDefaultDescriptors.Insert(0, new DummyDescriptor());
        }

        foreach (var descriptor in SubscribedDescriptors)
        {
            if (!AvailableDefaultDescriptors.Contains(descriptor))
            {
                AvailableDefaultDescriptors.Add(descriptor);
            }
        }

        for (int i = 0; i < AvailableDefaultDescriptors.Count; i++)
        {
            var descriptor = AvailableDefaultDescriptors[i];
            if (descriptor is DummyDescriptor)
            {
                continue;
            }

            if (!SubscribedDescriptors.Contains(descriptor))
            {
                AvailableDefaultDescriptors.RemoveAt(i);
                i--;
            }
        }
    }

    /// <summary>Placeholder empty-value descriptor used as the "no default" option in the default-value dropdown.</summary>
    private class DummyDescriptor: IHasValueString
    {
        public string Value { get; set; } = string.Empty;
    }
}

/// <summary>
/// View model of a <see cref="DescriptorAssignmentRuleSet"/>: one descriptor value plus the OR-list of
/// AND-gated slider rule groups that, when matched, assign that value. Self-removes when its OR-list empties.
/// </summary>
public class VM_DescriptorAssignmentRuleSet : VM
{
    /// <summary>Captures the descriptor category/values, wires the Add-rule-set command, and auto-removes this rule-set from its parent when its OR-logic list becomes empty.</summary>
    public VM_DescriptorAssignmentRuleSet(VM_BodyShapeDescriptorShell subscribedDescriptorShell, ObservableCollection<string> availableSliderNames, ObservableCollection<VM_DescriptorAssignmentRuleSet> parentCollection)
    {
        DescriptorCategory = subscribedDescriptorShell.Category;
        SubscribedDescriptorValues = subscribedDescriptorShell.Descriptors;
        AvailableSliderNames = availableSliderNames;

        RuleListORlogic.ToObservableChangeSet().Subscribe(x =>
        {
            if (!RuleListORlogic.Any())
            {
                parentCollection.Remove(this);
            }
        }).DisposeWith(this);

        AddNewRuleSet = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var newRuleGroup = new VM_AndGatedSliderRuleGroup(AvailableSliderNames, RuleListORlogic);
                newRuleGroup.RuleListANDlogic.Add(new(AvailableSliderNames, newRuleGroup.RuleListANDlogic));
                RuleListORlogic.Add(newRuleGroup);
            }
        );
    }

    public string DescriptorCategory { get; }
    public ObservableCollection<VM_BodyShapeDescriptor> SubscribedDescriptorValues { get; }
    public ObservableCollection<string> AvailableSliderNames { get; }
    public VM_BodyShapeDescriptor SelectedDescriptorValue { get; set; }
    public ObservableCollection<VM_AndGatedSliderRuleGroup> RuleListORlogic { get; set; } = new();
    public RelayCommand AddNewRuleSet { get; }

    /// <summary>Loads the selected descriptor value and the OR-list of AND-gated rule groups from the model.</summary>
    public void CopyInFromModel(DescriptorAssignmentRuleSet model)
    {
        SelectedDescriptorValue = SubscribedDescriptorValues.FirstOrDefault(x => x.Value == model.SelectedDescriptorValue);
        foreach (var ruleSet in model.RuleListORlogic)
        {
            var ruleSetVM = new VM_AndGatedSliderRuleGroup(AvailableSliderNames, RuleListORlogic);
            ruleSetVM.CopyInFromModel(ruleSet);
            RuleListORlogic.Add(ruleSetVM);
        }
    }

    /// <summary>Serializes the selected descriptor value and OR-list of rule groups into a <see cref="DescriptorAssignmentRuleSet"/> model.</summary>
    public DescriptorAssignmentRuleSet DumpToModel()
    {
        var model = new DescriptorAssignmentRuleSet();
        model.RuleListORlogic = RuleListORlogic.Select(x => x.DumpToModel()).ToList();
        model.SelectedDescriptorValue = SelectedDescriptorValue?.Value ?? String.Empty;
        return model;
    }
}

/// <summary>
/// View model of an <see cref="AndGatedSliderRuleGroup"/>: a set of slider rules that must all match
/// (AND logic) for the group to fire. Self-removes when its rule list empties.
/// </summary>
[DebuggerDisplay("AND-gated Rule List: Count = {RuleListANDlogic.Count}")]
public class VM_AndGatedSliderRuleGroup : VM
{
    /// <summary>Wires the Add-rule command and auto-removes this group from its parent when its AND-logic rule list becomes empty.</summary>
    public VM_AndGatedSliderRuleGroup(ObservableCollection<string> availableSliderNames, ObservableCollection<VM_AndGatedSliderRuleGroup> parentCollection)
    {
        AvailableSliderNames = availableSliderNames;

        RuleListANDlogic.ToObservableChangeSet().Subscribe(x =>
            {
                if (!RuleListANDlogic.Any())
                {
                    parentCollection.Remove(this);
                }
        }).DisposeWith(this);

        AddNewRule = new RelayCommand(
            canExecute: _ => true,
            execute: _ => RuleListANDlogic.Add(new VM_SliderClassificationRule(AvailableSliderNames, RuleListANDlogic))
        );
    }
    public ObservableCollection<string> AvailableSliderNames { get; }
    public ObservableCollection<VM_SliderClassificationRule> RuleListANDlogic { get; set; } = new();
    public RelayCommand AddNewRule { get; }
    /// <summary>True when this group has no rules, or none of its rules are non-empty.</summary>
    public bool IsEmpty => !RuleListANDlogic.Any() || !RuleListANDlogic.Any(x => !x.IsEmpty);

    /// <summary>Loads the AND-logic slider rules from the model.</summary>
    public void CopyInFromModel(AndGatedSliderRuleGroup model)
    {
        foreach (var andGatedRuleGroup in model.RuleListANDlogic)
        {
            RuleListANDlogic.Add(VM_SliderClassificationRule.CreateFromModel(andGatedRuleGroup, AvailableSliderNames, RuleListANDlogic));
        }
    }

    /// <summary>Serializes the AND-logic slider rules into an <see cref="AndGatedSliderRuleGroup"/> model.</summary>
    public AndGatedSliderRuleGroup DumpToModel()
    {
        var model = new AndGatedSliderRuleGroup();
        model.RuleListANDlogic = RuleListANDlogic.Select(x => x.DumpToModel()).ToList();
        return model;
    }
}


/// <summary>
/// View model of a single <see cref="SliderClassificationRule"/>: one slider name, type (big/small),
/// comparator, and threshold value — the atomic predicate of the annotation rule engine.
/// </summary>
[DebuggerDisplay("{SliderName} ({SliderType}) {Comparator} {Value}")]
public class VM_SliderClassificationRule : VM
{
    /// <summary>Captures the available slider names and wires the Delete and Add-AND-rule commands.</summary>
    public VM_SliderClassificationRule(ObservableCollection<string> sliderNames, ObservableCollection<VM_SliderClassificationRule> parentCollection)
    {
        AvaliableSliderNames = sliderNames;

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ => parentCollection.Remove(this)
        );

        AddANDRule = new RelayCommand(
            canExecute: _ => true,
            execute: _ => parentCollection.Add(new(sliderNames, parentCollection))
        );
    }

    public string SliderName { get; set; }

    public BodySliderType SliderType { get; set; }

    public string Comparator { get; set; } = "=";
    public int Value { get; set; }
    public ObservableCollection<string> AvaliableSliderNames { get; set; }
    /// <summary>True when no slider name has been chosen yet.</summary>
    public bool IsEmpty => SliderName.IsNullOrWhitespace();
    public RelayCommand DeleteMe { get; }
    public RelayCommand AddANDRule { get; }

    /// <summary>Builds a <see cref="VM_SliderClassificationRule"/> from its model, adding the model's slider name to the available list if missing.</summary>
    public static VM_SliderClassificationRule CreateFromModel(SliderClassificationRule model, ObservableCollection<string> sliderNames, ObservableCollection<VM_SliderClassificationRule> parentCollection)
    {
        var sliderClassificationRule = new VM_SliderClassificationRule(sliderNames, parentCollection);
        if (!sliderClassificationRule.AvaliableSliderNames.Contains(model.SliderName))
        {
            sliderClassificationRule.AvaliableSliderNames.Add(model.SliderName);
        }
        sliderClassificationRule.SliderName = model.SliderName;
        sliderClassificationRule.SliderType = model.SliderType;
        sliderClassificationRule.Comparator = model.Comparator;
        sliderClassificationRule.Value = model.Value;
        return sliderClassificationRule;
    }

    /// <summary>Serializes this rule into a <see cref="SliderClassificationRule"/> model.</summary>
    public SliderClassificationRule DumpToModel()
    {
        return new()
        {
            SliderName = SliderName,
            SliderType = SliderType,
            Comparator = Comparator,
            Value = Value
        };
    }

    public ObservableCollection<string> SliderNames { get; set; }

    public List<string> ComparatorOptions { get; } = new()
    {
        "=",
        "<=",
        "<",
        ">=",
        ">"
    };
}
