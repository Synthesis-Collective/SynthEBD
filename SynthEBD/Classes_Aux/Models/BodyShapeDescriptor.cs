﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class BodyShapeDescriptor
    {
        public BodyShapeDescriptor()
        {
            this.Category = "";
            this.Value = "";
            this.DispString = "";
        }
        public string Category { get; set; }
        public string Value { get; set; }
        public string DispString { get; set; }

        public static bool DescriptorsMatch(Dictionary<string, HashSet<string>> DescriptorSet, HashSet<BodyShapeDescriptor> shapeDescriptors)
        {
            foreach (var d in shapeDescriptors)
            {
                if (DescriptorsContainThis(DescriptorSet, d))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool DescriptorsContainThis(Dictionary<string, HashSet<string>> Descriptors, BodyShapeDescriptor currentDescriptor)
        {
            if (Descriptors.ContainsKey(currentDescriptor.Category))
            {
                if (Descriptors[currentDescriptor.Category].Contains(currentDescriptor.Value))
                {
                    return true;
                }
            }
            return false;
        }
    }
}