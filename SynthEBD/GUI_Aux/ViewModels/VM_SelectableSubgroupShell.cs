using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Backs a checkable subgroup row, wrapping a <see cref="VM_SubgroupPlaceHolder"/> with a selection flag
    /// and a display label whose format is chosen via <see cref="SubgroupLabelFormat"/>.
    /// </summary>
    public class VM_SelectableSubgroupShell
    {
        /// <summary>Stores the subgroup and default selection, then builds <see cref="Label"/> per the requested <paramref name="labelFormat"/>.</summary>
        public VM_SelectableSubgroupShell(VM_SubgroupPlaceHolder subgroup, bool defaultSelectedStatus, SubgroupLabelFormat labelFormat)
        {
            if (subgroup == null)
            {
                defaultSelectedStatus = false;
                return;
            }

            Subgroup = subgroup;
            IsSelected = defaultSelectedStatus;
            switch (labelFormat)
            {
                case SubgroupLabelFormat.ID: Label = Subgroup.ID; break;
                case SubgroupLabelFormat.Name: Label = Subgroup.Name; break;
                case SubgroupLabelFormat.IDandName: Label = Subgroup.ID + ": " + Subgroup.Name; break;
                case SubgroupLabelFormat.DeepName: Label = Subgroup.GetNameChain(" -> "); break;
                case SubgroupLabelFormat.IDandDeepName: Label = Subgroup.ID + ": " + Subgroup.GetNameChain(" -> "); break;
            }
        }
        public VM_SubgroupPlaceHolder Subgroup { get; set; }
        public bool IsSelected { get; set; }
        public string Label { get; set; }
    }

    /// <summary>Selects how a subgroup's display label is composed (ID, name, or its parent name chain).</summary>
    public enum SubgroupLabelFormat
    {
        ID,
        Name,
        IDandName,
        DeepName,
        IDandDeepName
    }
}
