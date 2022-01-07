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
    public class Settings_OBody
    {
        public Settings_OBody()
        {
            this.BodySlidesMale = new List<BodySlideSetting>();
            this.BodySlidesFemale = new List<BodySlideSetting>();
            this.TemplateDescriptors = new HashSet<BodyShapeDescriptor>();
            this.AttributeGroups = new HashSet<AttributeGroup>();
            this.CurrentlyExistingBodySlides = new HashSet<string>();
        }
        public List<BodySlideSetting> BodySlidesMale { get; set; }
        public List<BodySlideSetting> BodySlidesFemale { get; set; }
        public HashSet<BodyShapeDescriptor> TemplateDescriptors { get; set; }
        public HashSet<AttributeGroup> AttributeGroups { get; set; }
        
        [JsonIgnore]
        public HashSet<string> CurrentlyExistingBodySlides { get; set; }

        public void ImportBodySlides()
        {
            List<string> maleSliderGroups = JSONhandler<List<string>>.loadJSONFile(PatcherSettings.Paths.MaleTemplateGroupsPath);
            if (maleSliderGroups == null) { maleSliderGroups = new List<string>() { "HIMBO" }; }
            List<string> femaleSliderGroups = JSONhandler<List<string>>.loadJSONFile(PatcherSettings.Paths.FemaleTemplateGroupsPath);
            if (femaleSliderGroups == null) { femaleSliderGroups = new List<string>() { "CBBE, UNP" }; }

            CurrentlyExistingBodySlides.Clear();
            List<BodySlideSetting> currentBodySlides = new List<BodySlideSetting>();
            string loadFolder = System.IO.Path.Join(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "CalienteTools\\BodySlide\\SliderPresets");
            if (System.IO.Directory.Exists(loadFolder))
            {
                var xmlFilePaths = System.IO.Directory.GetFiles(loadFolder, "*.xml");
                foreach (var xmlFilePath in xmlFilePaths)
                {
                    XDocument presetFile = XDocument.Load(xmlFilePath);
                    var presets = presetFile.Element("SliderPresets");
                    foreach (var preset in presets.Elements())
                    {
                        var group = preset.Element("Group");
                        var groupName = group.Attribute("name").Value.ToString();
                        var presetName = preset.Attribute("name").Value.ToString();

                        CurrentlyExistingBodySlides.Add(presetName);

                        if (maleSliderGroups.Contains(groupName))
                        {
                            currentBodySlides = BodySlidesMale;
                        }
                        else if (femaleSliderGroups.Contains(groupName))
                        {
                            currentBodySlides = BodySlidesFemale;
                        }
                        else
                        {
                            continue;
                        }

                        if (currentBodySlides.Where(x => x.Label == presetName).Any()) // skip already loaded presets
                        {
                            continue;
                        }

                        BodySlideSetting newPreset = new BodySlideSetting();
                        newPreset.Label = presetName;

                        if (newPreset.Label.Contains("Zeroed Sliders", StringComparison.OrdinalIgnoreCase))
                        {
                            newPreset.AllowRandom = false;
                        }
                        if (newPreset.Label.Contains("Outfit", StringComparison.OrdinalIgnoreCase))
                        {
                            newPreset.AllowRandom = false;
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

        [JsonIgnore]
        public int MatchedForceIfCount { get; set; }
    }
}
