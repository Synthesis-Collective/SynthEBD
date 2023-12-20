using DynamicData;
using DynamicData.Binding;
using Microsoft.CodeAnalysis;
using Noggog;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;

namespace SynthEBD;

public class VM_BodySlideAnnotator : VM
{
    private readonly PatcherState _patcherState;
    private readonly VM_BodyShapeDescriptorCreationMenu _oBodyDescriptorMenu;
    private readonly VM_BodySlidesMenu _bodySlideMenu;
    private readonly BodySlideAnnotator _bodySlideAnnotator;
    private readonly Logger _logger;

    public delegate VM_BodySlideAnnotator Factory(VM_BodyShapeDescriptorCreationMenu oBodyDescriptorMenu, VM_BodySlidesMenu bodySlideMenu, VM_OBodyMiscSettings miscMenu);
    public VM_BodySlideAnnotator(PatcherState patcherState, VM_BodyShapeDescriptorCreationMenu oBodyDescriptorMenu, VM_BodySlidesMenu bodySlideMenu, VM_OBodyMiscSettings miscMenu, BodySlideAnnotator bodySlideAnnotator, Logger logger)
    {
        _patcherState = patcherState;
        _oBodyDescriptorMenu = oBodyDescriptorMenu;
        _bodySlideMenu = bodySlideMenu;
        _bodySlideAnnotator = bodySlideAnnotator;
        _logger = logger;

        SubscribedFemaleBodySlideGroups = miscMenu.FemaleBodySlideGroups;
        SubscribedMaleBodySlideGroups = miscMenu.MaleBodySlideGroups;

        ApplyAnnotationsCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ApplyAnnotations(null, null));
    }

    public string SelectedSliderGroup { get; set; }
    public ObservableCollection<VM_SliderClassificationRulesByBodyType> AnnotationRules { get; set; } = new();

    public VM_SliderClassificationRulesByBodyType DisplayedRuleSet { get; set; }

    public ObservableCollection<VM_CollectionMemberString> SubscribedMaleBodySlideGroups { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> SubscribedFemaleBodySlideGroups { get; set; } = new();

    public Dictionary<string, ObservableCollection<string>> SliderNamesByGroup { get; set; } = new();
    private List<SliderClassificationRulesByBodyType> _stashedUnloadedBodyTypeRules { get; set; } = new(); // for storing rules for descriptors that a user may have inadvertently removed
    public RelayCommand ApplyAnnotationsCommand { get; }

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
        _bodySlideMenu.AvailableSliderGroups.Add(VM_BodySlidesMenu.SliderGroupSelectionAll);
        Noggog.ListExt.AddRange(_bodySlideMenu.AvailableSliderGroups, SliderNamesByGroup.Keys);
    }

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

            var correspondingVM = AnnotationRules.Where(x => x.BodyTypeGroup == bodyType).FirstOrDefault();
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

    public void ApplyAnnotations(string? specifiedSliderGroup, string? specifiedDescriptorCategory)
    {
        var targetVMs = _bodySlideMenu.BodySlidesMale.And(_bodySlideMenu.BodySlidesFemale).Where(x => x.AssociatedModel.AutoAnnotated || !x.AssociatedModel.BodyShapeDescriptors.Any()).ToList();

        if (specifiedSliderGroup != null)
        {
            targetVMs = targetVMs.Where(x => x.AssociatedModel.SliderGroup == specifiedSliderGroup).ToList();
        }

        _bodySlideAnnotator.AnnotateBodySlides(targetVMs.Select(x => x.AssociatedModel).ToList(), this.DumpToModel(), true, specifiedDescriptorCategory);

        foreach (var targetVM in targetVMs)
        {
            if (targetVM.AssociatedModel.BodyShapeDescriptors.Any())
            {
                targetVM.InitializeBorderColor();
            }
        }

        _logger.CallTimedNotifyStatusUpdateAsync("Auto-Applied Annotations", 3, CommonColors.Yellow);
    }
}

[DebuggerDisplay("{SliderGroup}: Rule List for {DescriptorClassifiers.Count} Descriptors")]
public class VM_SliderClassificationRulesByBodyType : VM // contains a list of rules for each descriptor
{
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

    public void CopyInFromModel(SliderClassificationRulesByBodyType model)
    {
        _stashedUnloadedDescriptorRules.Clear();

        foreach (var perDescriptorRuleSet in model.DescriptorClassifiers)
        {
            var correspondingVM = DescriptorClassifiers.Where(x => x.DescriptorCategory == perDescriptorRuleSet.DescriptorCategory).FirstOrDefault();
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

    public SliderClassificationRulesByBodyType DumpToModel()
    {
        SliderClassificationRulesByBodyType model = new();
        model.BodyTypeGroup = BodyTypeGroup;
        model.DescriptorClassifiers = DescriptorClassifiers.Select(x => x.DumpToModel()).ToList();
        model.DescriptorClassifiers.AddRange(_stashedUnloadedDescriptorRules);
        return model;
    }
}

[DebuggerDisplay("{DescriptorCategory}: {RuleList.Count} Rule Groups")]
public class VM_DescriptorClassificationRuleSet : VM // rule set for a given descriptor
{
    public VM_DescriptorClassificationRuleSet(VM_BodyShapeDescriptorShell subscribedDescriptorShell, ObservableCollection<string> availableSliderNames, VM_BodySlideAnnotator annotatorVM, VM_SliderClassificationRulesByBodyType parentVM)
    {
        _subscribedDescriptorShell = subscribedDescriptorShell;
        SubscribedDescriptors = subscribedDescriptorShell.Descriptors;
        DescriptorCategory = _subscribedDescriptorShell.Category;
        AvailableSliderNames = availableSliderNames;

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
    public ObservableCollection<string> AvailableSliderNames { get; }
    public string DescriptorCategory { get; }
    public VM_BodyShapeDescriptor DefaultDescriptorValue { get; set; }
    public ObservableCollection<VM_DescriptorAssignmentRuleSet> RuleList { get; set; } = new();
    public RelayCommand ApplyAnnotationsCommand { get; }
    public RelayCommand AddNewRuleGroup { get; }

    public void CopyInFromModel(DescriptorClassificationRuleSet model)
    {
        DefaultDescriptorValue = _subscribedDescriptorShell.Descriptors.Where(x => x.Value == model.DefaultDescriptorValue).FirstOrDefault();

        RuleList.Clear();
        foreach (var rulesForDescriptor in model.RuleList)
        {
            var rulesForDescriptorVM = new VM_DescriptorAssignmentRuleSet(_subscribedDescriptorShell, AvailableSliderNames, RuleList);
            rulesForDescriptorVM.CopyInFromModel(rulesForDescriptor);
            RuleList.Add(rulesForDescriptorVM);
        }
    }

    public DescriptorClassificationRuleSet DumpToModel()
    {
        DescriptorClassificationRuleSet model = new();
        model.DescriptorCategory = DescriptorCategory;
        model.DefaultDescriptorValue = DefaultDescriptorValue?.Value ?? String.Empty;
        model.RuleList = RuleList.Select(x => x.DumpToModel()).ToList();
        return model;
    }
}

public class VM_DescriptorAssignmentRuleSet : VM
{
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

    public void CopyInFromModel(DescriptorAssignmentRuleSet model)
    {
        SelectedDescriptorValue = SubscribedDescriptorValues.Where(x => x.Value == model.SelectedDescriptorValue).FirstOrDefault();
        foreach (var ruleSet in model.RuleListORlogic)
        {
            var ruleSetVM = new VM_AndGatedSliderRuleGroup(AvailableSliderNames, RuleListORlogic);
            ruleSetVM.CopyInFromModel(ruleSet);
            RuleListORlogic.Add(ruleSetVM);
        }
    }

    public DescriptorAssignmentRuleSet DumpToModel()
    {
        var model = new DescriptorAssignmentRuleSet();
        model.RuleListORlogic = RuleListORlogic.Select(x => x.DumpToModel()).ToList();
        model.SelectedDescriptorValue = SelectedDescriptorValue?.Value ?? String.Empty;
        return model;
    }
}

[DebuggerDisplay("AND-gated Rule List: Count = {RuleListANDlogic.Count}")]
public class VM_AndGatedSliderRuleGroup : VM
{
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
    public bool IsEmpty => !RuleListANDlogic.Any() || !RuleListANDlogic.Where(x => !x.IsEmpty).Any();

    public void CopyInFromModel(AndGatedSliderRuleGroup model)
    {
        foreach (var andGatedRuleGroup in model.RuleListANDlogic)
        {
            RuleListANDlogic.Add(VM_SliderClassificationRule.CreateFromModel(andGatedRuleGroup, AvailableSliderNames, RuleListANDlogic));
        }
    }

    public AndGatedSliderRuleGroup DumpToModel()
    {
        var model = new AndGatedSliderRuleGroup();
        model.RuleListANDlogic = RuleListANDlogic.Select(x => x.DumpToModel()).ToList();
        return model;
    }
}


[DebuggerDisplay("{SliderName} ({SliderType}) {Comparator} {Value}")]
public class VM_SliderClassificationRule : VM
{
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
    public bool IsEmpty => SliderName.IsNullOrWhitespace();
    public RelayCommand DeleteMe { get; }
    public RelayCommand AddANDRule { get; }

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
