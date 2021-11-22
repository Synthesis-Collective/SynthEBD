using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class FilePathReplacement
    {
        public FilePathReplacement()
        {
            this.Source = "";
            this.Destination = "";
        }

        public string Source { get; set; }
        public string Destination { get; set; }
    }
    
    public class FilePathReplacementParsed
    {
        public FilePathReplacementParsed(FilePathReplacement template)
        {
            this.Source = template.Source;
            var pattern = @"\.(?![^\[]*[\]])";
            this.Destination = Regex.Split(template.Destination, pattern);
        }

        public string Source { get; set; }
        public string[] Destination { get; set; }
    }
}
