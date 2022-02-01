using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SynthEBD
{
    public class Settings_OBody: IHasDescriptorRules
    {
        public Settings_OBody()
        {
            this.BodySlidesMale = new List<BodySlideSetting>();
            this.BodySlidesFemale = new List<BodySlideSetting>();
            this.TemplateDescriptors = new HashSet<BodyShapeDescriptor>();
            this.AttributeGroups = new HashSet<AttributeGroup>();
            this.CurrentlyExistingBodySlides = new HashSet<string>();
            this.MaleSliderGroups = new HashSet<string>();
            this.FemaleSliderGroups = new HashSet<string>();
            this.DescriptorRules = new HashSet<BodyShapeDescriptorRules>();
            this.UseVerboseScripts = false;
        }
        public List<BodySlideSetting> BodySlidesMale { get; set; }
        public List<BodySlideSetting> BodySlidesFemale { get; set; }
        public HashSet<BodyShapeDescriptor> TemplateDescriptors { get; set; }
        public HashSet<AttributeGroup> AttributeGroups { get; set; }
        public HashSet<string> MaleSliderGroups { get; set; }
        public HashSet<string> FemaleSliderGroups { get; set; }
        public HashSet<BodyShapeDescriptorRules> DescriptorRules { get; set; }
        public bool UseVerboseScripts { get; set; }
        
        [JsonIgnore]
        public HashSet<string> CurrentlyExistingBodySlides { get; set; }

        public void ImportBodySlides()
        {
            if (!MaleSliderGroups.Any()) { MaleSliderGroups = new HashSet<string>() { "HIMBO" }; }
            if (!FemaleSliderGroups.Any()) { FemaleSliderGroups = new HashSet<string>() { "CBBE", "3BBB", "3BA", "UNP", "Unified UNP", "BHUNP 3BBB" }; }

            CurrentlyExistingBodySlides.Clear();
            List<BodySlideSetting> currentBodySlides = new List<BodySlideSetting>();
            string loadFolder = System.IO.Path.Join(PatcherEnvironmentProvider.Environment.DataFolderPath, "CalienteTools\\BodySlide\\SliderPresets");
            if (System.IO.Directory.Exists(loadFolder))
            {
                var xmlFilePaths = System.IO.Directory.GetFiles(loadFolder, "*.xml");
                foreach (var xmlFilePath in xmlFilePaths)
                {
                    XDocument presetFile = XDocument.Load(xmlFilePath);
                    var presets = presetFile.Element("SliderPresets");
                    var presetName = "";
                    foreach (var preset in presets.Elements())
                    {
                        var groups = preset.Elements("Group");
                        bool genderFound = false;
                        foreach (var group in groups)
                        {
                            var groupName = group.Attribute("name").Value.ToString();
                            presetName = preset.Attribute("name").Value.ToString();

                            CurrentlyExistingBodySlides.Add(presetName);

                            if (MaleSliderGroups.Contains(groupName))
                            {
                                currentBodySlides = BodySlidesMale;
                                genderFound = true;
                                break;
                            }
                            else if (FemaleSliderGroups.Contains(groupName))
                            {
                                currentBodySlides = BodySlidesFemale;
                                genderFound=true;
                                break;
                            }
                        }
                        if (!genderFound) { continue; }

                        if (currentBodySlides.Where(x => x.Label == presetName).Any()) // skip already loaded presets
                        {
                            continue;
                        }

                        BodySlideSetting newPreset = new BodySlideSetting();
                        newPreset.Label = presetName;

                        if (newPreset.Label.Contains("Zero for OBody", StringComparison.OrdinalIgnoreCase))
                        {
                            newPreset.AllowRandom = false;
                            newPreset.HideInMenu = true;
                        }
                        if (newPreset.Label.Contains("Zeroed Sliders", StringComparison.OrdinalIgnoreCase))
                        {
                            newPreset.AllowRandom = false;
                            newPreset.HideInMenu = true;
                        }
                        if (newPreset.Label.Contains("Clothes", StringComparison.OrdinalIgnoreCase))
                        {
                            newPreset.AllowRandom = false;
                            newPreset.HideInMenu = true;
                        }
                        if (newPreset.Label.Contains("Outfit", StringComparison.OrdinalIgnoreCase))
                        {
                            newPreset.AllowRandom = false;
                            newPreset.HideInMenu = true;
                        }

                        currentBodySlides.Add(newPreset);
                    }
                }
            }
        }
    }

    public class BodySlideSetting : IProbabilityWeighted
    {
        public BodySlideSetting()
        {
            this.Label = "";
            this.Notes = "";
            this.BodyShapeDescriptors = new HashSet<BodyShapeDescriptor>();
            this.AllowedRaces = new HashSet<FormKey>();
            this.AllowedRaceGroupings = new HashSet<string>();
            this.DisallowedRaces = new HashSet<FormKey>();
            this.DisallowedRaceGroupings = new HashSet<string>();
            this.AllowedAttributes = new HashSet<NPCAttribute>();
            this.DisallowedAttributes = new HashSet<NPCAttribute>();
            this.AllowUnique = true;
            this.AllowNonUnique = true;
            this.AllowRandom = true;
            this.ProbabilityWeighting = 1;
            this.WeightRange = new NPCWeightRange();
            this.HideInMenu = false;

            // used during patching, not written to settings file
            this.MatchedForceIfCount = 0;
        }

        public string Label { get; set; }
        public string Notes { get; set; }
        public HashSet<BodyShapeDescriptor> BodyShapeDescriptors { get; set; }
        public HashSet<FormKey> AllowedRaces { get; set; }
        public HashSet<FormKey> DisallowedRaces { get; set; }
        public HashSet<string> AllowedRaceGroupings { get; set; }
        public HashSet<string> DisallowedRaceGroupings { get; set; }
        public HashSet<NPCAttribute> AllowedAttributes { get; set; } // keeping as array to allow deserialization of original zEBD settings files
        public HashSet<NPCAttribute> DisallowedAttributes { get; set; }
        public bool AllowUnique { get; set; }
        public bool AllowNonUnique { get; set; }
        public bool AllowRandom { get; set; }
        public int ProbabilityWeighting { get; set; }
        public NPCWeightRange WeightRange { get; set; }
        public bool HideInMenu { get; set; }

        [JsonIgnore]
        public int MatchedForceIfCount { get; set; }
    }
}
