Scriptname SynthEBDHeadPartLoaderPAScript extends ReferenceAlias

GlobalVariable Property HeadPartScriptActive Auto
GlobalVariable Property VerboseMode Auto

import SynthEBDCommonFuncs

Event OnInit()
	if (HeadPartScriptActive.GetValue())
		VerboseLogger("SynthEBD: Player Alias HeadPart Script running OnInit", VerboseMode.GetValue(), false)
		ReloadSynthEBDDataBase("Data/SynthEBD/HeadPartAssignments.json", ".SynthEBD.HeadParts", VerboseMode.GetValue(), "HeadPart")
	endif
EndEvent

Event OnPlayerLoadGame()
	if (HeadPartScriptActive.GetValue())
		VerboseLogger("SynthEBD: Player Alias HeadPart Script running OnPlayerLoadGame", VerboseMode.GetValue(), false)
		ReloadSynthEBDDataBase("Data/SynthEBD/HeadPartAssignments.json", ".SynthEBD.HeadParts", VerboseMode.GetValue(), "HeadPart")
	endif
EndEvent