using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using System.Xml.Linq;

namespace SynthEBD;

public enum AutoBodySelectionMode
{
    INI,
    JSON
}

public class Settings_OBody
{
    private readonly IStateProvider _stateProvider;
    public Settings_OBody(IStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }
    public List<BodySlideSetting> BodySlidesMale { get; set; } = new();
    public List<BodySlideSetting> BodySlidesFemale { get; set; } = new();
    public HashSet<BodyShapeDescriptor> TemplateDescriptors { get; set; } = new()
    {
        new BodyShapeDescriptor(){ ID = new(){ Category = "Build", Value = "Slight" } },
        new BodyShapeDescriptor(){ ID = new(){Category = "Build", Value = "Medium" } },
        new BodyShapeDescriptor() { ID = new(){Category = "Build", Value = "Curvy"} },
        new BodyShapeDescriptor() { ID = new(){Category = "Build", Value = "Chubby"}},
        new BodyShapeDescriptor() { ID = new(){Category = "Build", Value = "Exaggerated" }},
        new BodyShapeDescriptor() { ID = new(){Category = "Build", Value = "Powerful" }},
        new BodyShapeDescriptor() { ID = new(){Category = "Chest", Value = "Busty" }},
        new BodyShapeDescriptor() { ID = new(){Category = "Chest", Value = "Medium" }},
        new BodyShapeDescriptor() { ID = new(){Category = "Chest", Value = "Petite" }},
    };

    public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();
    public HashSet<string> MaleSliderGroups { get; set; } = new();
    public HashSet<string> FemaleSliderGroups { get; set; } = new();
    public bool bUseVerboseScripts { get; set; } = false;
    public AutoBodySelectionMode AutoBodySelectionMode { get; set; } = AutoBodySelectionMode.INI;

    [JsonIgnore]
    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new();

    public void ImportBodySlides(HashSet<BodyShapeDescriptor> templateDescriptors, SettingsIO_OBody oBodyIO)
    {
        if (!MaleSliderGroups.Any()) { MaleSliderGroups = new HashSet<string>() { "HIMBO" }; }
        if (!FemaleSliderGroups.Any()) { FemaleSliderGroups = new HashSet<string>() { "CBBE", "3BBB", "3BA", "UNP", "Unified UNP", "BHUNP 3BBB" }; }

        var defaultAnnotationDict = oBodyIO.LoadDefaultBodySlideAnnotation();

        CurrentlyExistingBodySlides.Clear();
        List<BodySlideSetting> currentBodySlides = new List<BodySlideSetting>();
        string loadFolder = System.IO.Path.Join(_stateProvider.DataFolderPath, "CalienteTools\\BodySlide\\SliderPresets");
        if (System.IO.Directory.Exists(loadFolder))
        {
            var xmlFilePaths = System.IO.Directory.GetFiles(loadFolder, "*.xml");
            foreach (var xmlFilePath in xmlFilePaths)
            {
                XDocument presetFile = XDocument.Load(xmlFilePath);
                var presets = presetFile.Element("SliderPresets");
                if (presets == null) { continue; }

                var presetName = "";
                foreach (var preset in presets.Elements())
                {
                    var groups = preset.Elements("Group");
                    if (groups == null) { continue; }

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

                    if (defaultAnnotationDict.ContainsKey(presetName))
                    {
                        foreach (var annotation in defaultAnnotationDict[presetName])
                        {
                            var descriptor = templateDescriptors.Where(x => x.ID.ToString().Equals(annotation, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                            if (descriptor != null)
                            {
                                newPreset.BodyShapeDescriptors.Add(descriptor.ID);
                            }
                        }
                    }

                    currentBodySlides.Add(newPreset);
                }
            }
        }
    }
}

public class BodySlideSetting : IProbabilityWeighted
{
    public string Label { get; set; } = "";
    public string Notes { get; set; } = "";
    public HashSet<BodyShapeDescriptor.LabelSignature> BodyShapeDescriptors { get; set; } = new();
    public HashSet<FormKey> AllowedRaces { get; set; } = new();
    public HashSet<FormKey> DisallowedRaces { get; set; } = new();
    public HashSet<string> AllowedRaceGroupings { get; set; } = new();
    public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
    public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
    public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public bool AllowRandom { get; set; } = true;
    public double ProbabilityWeighting { get; set; } = 1;
    public NPCWeightRange WeightRange { get; set; } = new();
    public bool HideInMenu { get; set; } = false;

    [JsonIgnore]
    public int MatchedForceIfCount { get; set; } = 0;
}