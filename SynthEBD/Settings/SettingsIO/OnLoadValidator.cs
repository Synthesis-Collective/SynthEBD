using Noggog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class OnLoadValidator // Validation that has to load AFTER the main view model has initialized. This is due to bug in MaterialMessageBox that closes the app if a button is pressed before the main window loads.
    {
        public static void ValidateSettings(PatcherState patcherState)
        {
            patcherState.GeneralSettings.RaceGroupings = CheckGroupDuplicates(patcherState.GeneralSettings.RaceGroupings, "General Settings", "Race Groupings").Cast<RaceGrouping>().ToList();
            foreach (var ap in patcherState.AssetPacks)
            {
                ap.RaceGroupings = CheckGroupDuplicates(ap.RaceGroupings, ap.GroupName, "Race Groupings").Cast<RaceGrouping>().ToList();
            }

            patcherState.GeneralSettings.AttributeGroups = CheckGroupDuplicates(patcherState.GeneralSettings.AttributeGroups, "General Settings", "Attribute Groups").Cast<AttributeGroup>().ToHashSet();
            foreach (var ap in patcherState.AssetPacks)
            {
                ap.AttributeGroups = CheckGroupDuplicates(ap.AttributeGroups, ap.GroupName, "Attribute Groups").Cast<AttributeGroup>().ToHashSet();
            }
            foreach (var bg in patcherState.BodyGenConfigs.Male.And(patcherState.BodyGenConfigs.Female))
            {
                bg.AttributeGroups = CheckGroupDuplicates(bg.AttributeGroups, bg.Label, "Attribute Groups").Cast<AttributeGroup>().ToHashSet();
            }
            patcherState.OBodySettings.AttributeGroups = CheckGroupDuplicates(patcherState.OBodySettings.AttributeGroups, "O/AutoBody Settings", "Attribute Groups").Cast<AttributeGroup>().ToHashSet();


            foreach (var descriptor in patcherState.OBodySettings.TemplateDescriptors)
            {
                descriptor.Label = descriptor.ID.ToString();
            }
            patcherState.OBodySettings.TemplateDescriptors = CheckGroupDuplicates(patcherState.OBodySettings.TemplateDescriptors, "O/AutoBody Settings", "Body Shape Descriptors").Cast<BodyShapeDescriptor>().ToHashSet();
            foreach (var bg in patcherState.BodyGenConfigs.Male.And(patcherState.BodyGenConfigs.Female))
            {
                foreach (var descriptor in bg.TemplateDescriptors)
                {
                    descriptor.Label = descriptor.ID.ToString();
                }
                bg.TemplateDescriptors = CheckGroupDuplicates(bg.TemplateDescriptors, bg.Label, "Body Shape Descriptors").Cast<BodyShapeDescriptor>().ToHashSet();
            }
        }

        public static IEnumerable<IHasLabel> CheckGroupDuplicates(IEnumerable<IHasLabel> groupings, string parentDispName, string type)
        {
            var filteredGroupings = groupings.ToList();

            List<string> names = new();
            List<string> duplicates = new();

            foreach (var g in groupings)
            {
                if (names.Contains(g.Label))
                {
                    duplicates.Add(g.Label);
                }
                names.Add(g.Label);
            }

            if (duplicates.Any())
            {
                string message = "Duplicate " + type + " detected in " + parentDispName + ". Remove duplicates? [Only the first occurrence will be kept; make sure this is the one you want to save.]" + Environment.NewLine;

                foreach (var g in duplicates.Distinct())
                {
                    message += g + " (" + (duplicates.Where(x => x == g).Count() + 1) + ")" + Environment.NewLine;
                }

                if (CustomMessageBox.DisplayNotificationYesNo("Duplicate " + type, message))
                {
                    foreach (var name in duplicates)
                    {
                        bool triggered = false;
                        int duplicateCount = duplicates.Where(x => x == name).ToArray().Count();
                        for (int i = 0; i < filteredGroupings.Count; i++)
                        {
                            if (filteredGroupings[i].Label == name)
                            {
                                if (triggered)
                                {
                                    filteredGroupings.RemoveAt(i);
                                    i--;
                                }
                                else
                                {
                                    triggered = true;
                                }
                            }
                        }
                    }

                    return filteredGroupings;
                }
            }
            return groupings;
        }
    }
}
