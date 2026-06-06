using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Copies SynthEBD's quest start-up sequence (<c>.seq</c>) file into the output data folder so the
    /// EBD runtime quest auto-starts on a new game.
    /// </summary>
    public class QuestInit
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        private readonly Logger _logger;
        /// <summary>Initializes a new <see cref="QuestInit"/> with the environment, paths, IO helper, and logger.</summary>
        public QuestInit(IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths, PatcherIO patcherIO, Logger logger)
        {
            _environmentProvider = environmentProvider;
            _paths = paths;
            _patcherIO = patcherIO;
            _logger = logger;
        }
        /// <summary>Copies <c>SynthEBD.seq</c> from internal data into the output <c>Seq</c> folder.</summary>
        public void WriteQuestSeqFile()
        {
            string questSeqSourcePath = Path.Combine(_environmentProvider.InternalDataPath, "QuestSeq", "SynthEBD.seq");
            string questSeqDestPath = Path.Combine(_paths.OutputDataFolder, "Seq", "SynthEBD.seq");
            _patcherIO.TryCopyResourceFile(questSeqSourcePath, questSeqDestPath, _logger);
        }
    }
}
