using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using System.Xml.Linq;
using Z.Expressions.Compiler;

namespace SynthEBD;

public enum AutoBodySelectionMode
{
    INI,
    JSON
}

public class Settings_OBody
{
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
    public int HIMBOAnnotationVersion { get; set; } = 0; // increment as necessary to update HIMBO versions

    [JsonIgnore]
    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new();

    public void ImportBodySlides(HashSet<BodyShapeDescriptor> templateDescriptors, SettingsIO_OBody oBodyIO, string gameDataFolder, Logger logger)
    {
        logger.LogStartupEventStart("Detecting currently installed BodySlides");
        if (!MaleSliderGroups.Any()) { MaleSliderGroups = new HashSet<string>() { "HIMBO" }; }
        if (!FemaleSliderGroups.Any()) { FemaleSliderGroups = new HashSet<string>() { "CBBE", "3BBB", "3BA", "UNP", "Unified UNP", "BHUNP 3BBB" }; }

        var defaultAnnotationDict = oBodyIO.LoadDefaultBodySlideAnnotation();

        CurrentlyExistingBodySlides.Clear();
        List<BodySlideSetting> currentBodySlides = new List<BodySlideSetting>();
        string loadFolder = System.IO.Path.Join(gameDataFolder, "CalienteTools\\BodySlide\\SliderPresets");
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

                    if (currentBodySlides.Where(x => x.ReferencedBodySlide == presetName).Any()) // skip already loaded presets
                    {
                        continue;
                    }

                    BodySlideSetting newPreset = new BodySlideSetting();
                    newPreset.Label = presetName;
                    newPreset.ReferencedBodySlide = presetName;

                    if (newPreset.Label.Contains("Zero for OBody", StringComparison.OrdinalIgnoreCase) ||
                        newPreset.Label.Contains("Zeroed Sliders", StringComparison.OrdinalIgnoreCase) ||
                        newPreset.Label.Contains("Clothes", StringComparison.OrdinalIgnoreCase) ||
                        newPreset.Label.Contains("Outfit", StringComparison.OrdinalIgnoreCase) ||
                        newPreset.Label.Contains("Refit ", StringComparison.OrdinalIgnoreCase))
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
        logger.LogStartupEventEnd("Detecting currently installed BodySlides");

        if (HIMBOAnnotationVersion == 0)
        {
            InitialHIMBOSetup();
        }
    }

    public void InitialHIMBOSetup() // Some default HIMBO presets are of the "different descriptors at different weights" variety. Clone presets here to reflect this.
    {
        for (int i = 0; i < BodySlidesMale.Count; i++)
        {
            var currentBodySlide = BodySlidesMale[i];
            // skip processing if the user has pre-existing multiple entries for this bodyslide; they've probably already handled this manually
            if (HIMBOPresets_0.Contains(currentBodySlide.ReferencedBodySlide) && BodySlidesMale.Where(x => x.ReferencedBodySlide == currentBodySlide.ReferencedBodySlide).Count() == 1)  
            {
                currentBodySlide.BodyShapeDescriptors.Clear();
                var copiedBodySlide = currentBodySlide.DeepCopyByExpressionTree();
                
                copiedBodySlide.WeightRange.Upper = 50;
                currentBodySlide.WeightRange.Lower = 51;

                if (copiedBodySlide.ReferencedBodySlide == "HIMBO Daddy")
                {
                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Medium" });
                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Chubby" });
                    copiedBodySlide.Label += " (Low Weight)";
                    currentBodySlide.Label += " (High Weight)";
                }
                else if (copiedBodySlide.ReferencedBodySlide == "HIMBO Jack")
                {
                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Slight" });
                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Chubby" });
                    copiedBodySlide.Label += " (Low Weight)";
                    currentBodySlide.Label += " (High Weight)";
                }
                else if (copiedBodySlide.ReferencedBodySlide == "HIMBO Simple")
                {
                    currentBodySlide.WeightRange.Lower = 34;
                    currentBodySlide.WeightRange.Upper = 66;

                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Slight" });
                    copiedBodySlide.WeightRange.Upper = 33;

                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Medium" });

                    var copiedBodySlide2 = BodySlidesMale[i].DeepCopyByExpressionTree();
                    copiedBodySlide2.BodyShapeDescriptors.Clear();
                    copiedBodySlide2.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Powerful" });
                    copiedBodySlide2.WeightRange.Lower = 67;
                    copiedBodySlide2.WeightRange.Upper = 100;
                    BodySlidesMale.Insert(i + 1, copiedBodySlide2);

                    copiedBodySlide.Label += " (Low Weight)";
                    currentBodySlide.Label += " (Medium Weight)";
                    copiedBodySlide2.Label += " (High Weight)";
                }

                BodySlidesMale.Insert(i, copiedBodySlide);
                i++;
            }
        }
        HIMBOAnnotationVersion = 1;
    }

    public static HashSet<string> HIMBOPresets_0 = new()
    {
        "HIMBO Daddy",
        "HIMBO Jack",
        "HIMBO Simple"
    };
}

public class BodySlideSetting : IProbabilityWeighted
{
    public string Label { get; set; } = "";
    public string ReferencedBodySlide { get; set; } = "";
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