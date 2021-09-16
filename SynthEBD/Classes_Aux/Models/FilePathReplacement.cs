using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    
}
