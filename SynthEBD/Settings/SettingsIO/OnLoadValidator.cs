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


            // Shells dedupe by Category; their internal Descriptors dedupe by (Category:Value)
            // via the descriptor's Label field. Stamp Label on each descriptor first so the
            // duplicate-detection plumbing can compare strings, then dedupe shells, then dedupe
            // each surviving shell's values.
            DedupeDescriptorShellList(patcherState.OBodySettings.TemplateDescriptors, "O/AutoBody Settings");
            foreach (var bg in patcherState.BodyGenConfigs.Male.And(patcherState.BodyGenConfigs.Female))
            {
                DedupeDescriptorShellList(bg.TemplateDescriptors, bg.Label);
            }
        }

        /// <summary>Two-level dedupe pass over a <see cref="BodyShapeDescriptorShell"/> list:
        /// first dedupe shells by Category (each shell's <see cref="IHasLabel.Label"/> proxies
        /// Category), then dedupe each surviving shell's Descriptors by their full signature
        /// (Category:Value). Mutates <paramref name="shells"/> in place by reassigning surviving
        /// items; safe to call on the field's own list. <paramref name="parentDispName"/> is
        /// surfaced in the duplicate-detection prompt so users know which config the duplicate
        /// came from.</summary>
        private static void DedupeDescriptorShellList(List<BodyShapeDescriptorShell> shells, string parentDispName)
        {
            if (shells == null) return;

            // Stamp descriptor Labels for the per-value dedupe below.
            foreach (var shell in shells)
            {
                if (shell?.Descriptors == null) continue;
                foreach (var descriptor in shell.Descriptors)
                {
                    if (descriptor?.ID != null) descriptor.Label = descriptor.ID.ToString();
                }
            }

            // Dedupe shells by Category (shell.Label proxies Category via IHasLabel).
            var deduped = CheckGroupDuplicates(shells, parentDispName, "Body Shape Descriptor Categories").Cast<BodyShapeDescriptorShell>().ToList();

            // Per-shell value dedupe.
            foreach (var shell in deduped)
            {
                if (shell?.Descriptors == null) continue;
                var dedupedValues = CheckGroupDuplicates(shell.Descriptors, parentDispName + " → " + shell.Category, "Body Shape Descriptor Values").Cast<BodyShapeDescriptor>().ToList();
                shell.Descriptors = dedupedValues;
            }

            // Replace original list contents with the deduped order.
            shells.Clear();
            shells.AddRange(deduped);
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

                if (MessageWindow.DisplayNotificationYesNo("Duplicate " + type, message))
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
