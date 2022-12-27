using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class QuestInit
    {
        private SynthEBDPaths _paths;
        private PatcherIO _patcherIO;
        public QuestInit(SynthEBDPaths paths, PatcherIO patcherIO)
        {
            _paths = paths;
            _patcherIO = patcherIO;
        }
        public void WriteQuestSeqFile()
        {
            string questSeqSourcePath = Path.Combine(_paths.ResourcesFolderPath, "QuestSeq", "SynthEBD.seq");
            string questSeqDestPath = Path.Combine(_paths.OutputDataFolder, "Seq", "SynthEBD.seq");
            _patcherIO.TryCopyResourceFile(questSeqSourcePath, questSeqDestPath);
        }
    }
}
