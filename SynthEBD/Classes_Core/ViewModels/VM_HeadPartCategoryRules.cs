using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_HeadPartCategoryRules : VM
    {
        public VM_HeadPartCategoryRules(ObservableCollection<VM_RaceGrouping> raceGroupingVMs, VM_Settings_Headparts parentConfig, VM_SettingsOBody oBody)
        {
            AllowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(oBody.DescriptorUI, raceGroupingVMs, parentConfig);
            DisallowedBodySlideDescriptors = new VM_BodyShapeDescriptorSelectionMenu(oBody.DescriptorUI, raceGroupingVMs, parentConfig);
            AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);
            DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(raceGroupingVMs);

            ParentConfig = parentConfig;

            PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
                .Subscribe(x => lk = x)
                .DisposeWith(this);

            AddAllowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.AllowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.AllowedAttributes, true, null, ParentConfig.AttributeGroupMenu.Groups))
            );

            AddDisallowedAttribute = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.DisallowedAttributes.Add(VM_NPCAttribute.CreateNewFromUI(this.DisallowedAttributes, false, null, ParentConfig.AttributeGroupMenu.Groups))
            );

            AddDistributionWeighting = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (DistributionProbabilities.Any())
                    {
                        var existingWeights = DistributionProbabilities.Select(x => x.DistributionQuantity);
                        var max = existingWeights.Max();
                        var range = new HashSet<int>(Enumerable.Range(0, max));
                        range.ExceptWith(existingWeights); // now only missing values remain
                        var toAdd = max + 1;
                        if (range.Any()) { toAdd = range.Min(); }
                        DistributionProbabilities.Add(new VM_HeadPartQuantityDistributionWeighting(toAdd, 1, this));
                    }
                    else
                    {
                        DistributionProbabilities.Add(new VM_HeadPartQuantityDistributionWeighting(0, 1, this));
                    }

                    DistributionProbabilities = new(DistributionProbabilities.OrderBy(x => x.DistributionQuantity));
                }
            );
        }
        public bool bAllowFemale { get; set; } = true;
        public bool bAllowMale { get; set; } = true;
        public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
        public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
        public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
        public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
        public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
        public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
        public bool bAllowUnique { get; set; } = true;
        public bool bAllowNonUnique { get; set; } = true;
        public bool bAllowRandom { get; set; } = true;
        public NPCWeightRange WeightRange { get; set; } = new();
        public ObservableCollection<VM_HeadPartQuantityDistributionWeighting> DistributionProbabilities { get; set; } = new();
        public RelayCommand AddDistributionWeighting { get; }
        public string Caption_BodyShapeDescriptors { get; set; } = "";
        public ILinkCache lk { get; private set; }
        public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();
        public RelayCommand AddAllowedAttribute { get; }
        public RelayCommand AddDisallowedAttribute { get; }
        public VM_Settings_Headparts ParentConfig { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
        public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }
    }

    public class VM_HeadPartQuantityDistributionWeighting : VM
    {
        public VM_HeadPartQuantityDistributionWeighting(int quantity, double weight, VM_HeadPartCategoryRules parentMenu)
        {
            ParentMenu = parentMenu;
            DistributionQuantity = quantity;
            DistributionWeight = weight;

            DeleteMe = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => ParentMenu.DistributionProbabilities.Remove(this)
            ) ;
        }
        public int DistributionQuantity { get; set; }
        public double DistributionWeight { get; set; }
        public RelayCommand DeleteMe { get; }
        VM_HeadPartCategoryRules ParentMenu { get; set; }   
    }
}
