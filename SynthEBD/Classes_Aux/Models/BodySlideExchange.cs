using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class BodySlideExchange
    {
        public List<BodySlideSetting> BodySlidesMale { get; set; } = new();
        public List<BodySlideSetting> BodySlidesFemale { get; set; } = new();
        public HashSet<BodyShapeDescriptor> TemplateDescriptors { get; set; } = new();
        public List<RaceGrouping> RaceGroupings { get; set; } = new();
        public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();
    }
}
