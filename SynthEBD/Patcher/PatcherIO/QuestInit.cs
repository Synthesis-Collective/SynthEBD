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
        private readonly IStateProvider _stateProvider;
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        private readonly Logger _logger;
        public QuestInit(IStateProvider stateProvider, SynthEBDPaths paths, PatcherIO patcherIO, Logger logger)
        {
            _stateProvider = stateProvider;
            _paths = paths;
            _patcherIO = patcherIO;
            _logger = logger;
        }
        public void WriteQuestSeqFile()
        {
            string questSeqSourcePath = Path.Combine(_stateProvider.InternalDataPath, "QuestSeq", "SynthEBD.seq");
            string questSeqDestPath = Path.Combine(_paths.OutputDataFolder, "Seq", "SynthEBD.seq");
            _patcherIO.TryCopyResourceFile(questSeqSourcePath, questSeqDestPath, _logger);
        }
    }
}
