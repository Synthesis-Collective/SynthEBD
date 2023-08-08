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
                try
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
                                genderFound = true;
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
                catch
                {
                    logger.LogError("Warning: failed to read BodySlide XML file: " + xmlFilePath + ". This file may be corrupted or incorrectly formatted.");
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
            int numCopies = 0;
            var currentBodySlide = BodySlidesMale[i];
            // skip processing if the user has pre-existing multiple entries for this bodyslide; they've probably already handled this manually
            if (HIMBOPresetsToUpdate_0.Contains(currentBodySlide.ReferencedBodySlide) && BodySlidesMale.Where(x => x.ReferencedBodySlide == currentBodySlide.ReferencedBodySlide).Count() == 1)  
            {
                currentBodySlide.BodyShapeDescriptors.Clear();
                var copiedBodySlide = JSONhandler<BodySlideSetting>.CloneViaJSON(currentBodySlide);

                if (copiedBodySlide.ReferencedBodySlide == "HIMBO Daddy")
                {
                    numCopies = 1;

                    copiedBodySlide.WeightRange.Upper = 19;
                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Medium" });
                    copiedBodySlide.Label += " (Low Weight)";

                    currentBodySlide.WeightRange.Lower = 20;
                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Chubby" });
                    currentBodySlide.Label += " (High Weight)";
                }
                else if (copiedBodySlide.ReferencedBodySlide == "HIMBO Jack")
                {
                    numCopies = 2;

                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Slight" });
                    copiedBodySlide.WeightRange.Upper = 44;
                    copiedBodySlide.Label += " (Low Weight)";

                    var copiedBodySlide2 = JSONhandler<BodySlideSetting>.CloneViaJSON(currentBodySlide);
                    copiedBodySlide2.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Chubby" });
                    copiedBodySlide2.WeightRange.Lower = 56;
                    copiedBodySlide2.WeightRange.Upper = 100;
                    copiedBodySlide2.Label += " (High Weight)";

                    currentBodySlide.WeightRange.Lower = 45;
                    currentBodySlide.WeightRange.Upper = 55;
                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Medium" });
                    currentBodySlide.Label += " (Medium Weight)";

                    BodySlidesMale.Insert(i + 1, copiedBodySlide2);
                }
                else if (copiedBodySlide.ReferencedBodySlide == "HIMBO Simple")
                {
                    numCopies = 1;

                    copiedBodySlide.WeightRange.Upper = 40;
                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Slight" });
                    copiedBodySlide.Label += " (Low Weight)";

                    currentBodySlide.WeightRange.Lower = 41;
                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Powerful" });
                    currentBodySlide.Label += " (High Weight)";
                }
                else if (copiedBodySlide.ReferencedBodySlide == "HIMBO Sultry")
                {
                    numCopies = 1;

                    copiedBodySlide.WeightRange.Upper = 19;
                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Medium" });
                    copiedBodySlide.Label += " (Low Weight)";

                    currentBodySlide.WeightRange.Lower = 20;
                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Powerful" });
                    currentBodySlide.Label += " (High Weight)";
                }
                else if (copiedBodySlide.ReferencedBodySlide == "HIMBO Hugh")
                {
                    numCopies = 1;

                    copiedBodySlide.WeightRange.Upper = 59;
                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Medium" });
                    copiedBodySlide.Label += " (Low Weight)";

                    currentBodySlide.WeightRange.Lower = 60;
                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Chubby" });
                    currentBodySlide.Label += " (High Weight)";
                }
                else if (copiedBodySlide.ReferencedBodySlide == "HIMBO Hideo")
                {
                    numCopies = 1;

                    copiedBodySlide.WeightRange.Upper = 66;
                    copiedBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Slight" });
                    copiedBodySlide.Label += " (Low Weight)";

                    currentBodySlide.WeightRange.Lower = 67;
                    currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Medium" });
                    currentBodySlide.Label += " (High Weight)";
                }

                BodySlidesMale.Insert(i, copiedBodySlide);
                i+= numCopies;
            }
            else if (currentBodySlide.ReferencedBodySlide == "HIMBO Mike")
            {
                currentBodySlide.BodyShapeDescriptors.Clear();
                currentBodySlide.BodyShapeDescriptors.Add(new BodyShapeDescriptor.LabelSignature() { Category = "Build", Value = "Powerful" });
            }
        }
        HIMBOAnnotationVersion = 1;
    }

    private static HashSet<string> HIMBOPresetsToUpdate_0 = new()
    {
        "HIMBO Daddy",
        "HIMBO Jack",
        "HIMBO Simple",
        "HIMBO Hugh",
        "HIMBO Hideo",
        "HIMBO Sultry"
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