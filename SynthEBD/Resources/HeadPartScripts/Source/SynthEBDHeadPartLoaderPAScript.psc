Scriptname SynthEBDHeadPartLoaderPAScript extends ReferenceAlias

GlobalVariable Property HeadPartScriptActive Auto
GlobalVariable Property VerboseMode Auto

import SynthEBDCommonFuncs

Event OnInit()
	if (HeadPartScriptActive)
		VerboseLogger("SynthEBD: Player Alias HeadPart Script running OnInit", VerboseMode, false)
		ReloadSynthEBDDataBase("Data/SynthEBD/HeadPartAssignments.json", ".SynthEBD.HeadParts", VerboseMode, "HeadPart")
	endif
EndEvent

Event OnPlayerLoadGame()
	if (HeadPartScriptActive)
		VerboseLogger("SynthEBD: Player Alias HeadPart Script running OnPlayerLoadGame", VerboseMode, false)
		ReloadSynthEBDDataBase("Data/SynthEBD/HeadPartAssignments.json", ".SynthEBD.HeadParts", VerboseMode, "HeadPart")
	endif
EndEvent