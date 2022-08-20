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
        public static void WriteQuestSeqFile()
        {
            string questSeqSourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "QuestSeq", "SynthEBD.seq");
            string questSeqDestPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "Seq", "SynthEBD.seq");
            PatcherIO.TryCopyResourceFile(questSeqSourcePath, questSeqDestPath);
        }
    }
}
