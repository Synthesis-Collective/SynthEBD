Scriptname SynthEBDHeadPartLoaderPAScript extends ReferenceAlias

GlobalVariable Property HeadPartScriptActive Auto
GlobalVariable Property VerboseMode Auto

import SynthEBDCommonFuncs

Event OnInit()
	if (HeadPartScriptActive)
		VerboseLogger("SynthEBD: Player Alias HeadPart Script running OnInit", VerboseMode, false)
		LoadJFormKeyMapsToJFormDB("Data/SynthEBD/HeadPartAssignments", ".SynthEBD.HeadParts", VerboseMode, "HeadPart", "obj")
	endif
EndEvent

Event OnPlayerLoadGame()
	if (HeadPartScriptActive)
		VerboseLogger("SynthEBD: Player Alias HeadPart Script running OnPlayerLoadGame", VerboseMode, false)
		LoadJFormKeyMapsToJFormDB("Data/SynthEBD/HeadPartAssignments", ".SynthEBD.HeadParts", VerboseMode, "HeadPart", "obj")
	endif
EndEvent