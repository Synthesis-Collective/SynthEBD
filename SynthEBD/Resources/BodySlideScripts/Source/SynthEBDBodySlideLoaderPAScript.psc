Scriptname SynthEBDBodySlideLoaderPAScript extends ReferenceAlias

GlobalVariable Property BodySlideScriptActive Auto
GlobalVariable Property VerboseMode Auto

import SynthEBDCommonFuncs

Event OnInit()
	if (BodySlideScriptActive)
		VerboseLogger("SynthEBD: Player Alias BodySlide Script running OnInit", VerboseMode, false)
		LoadJFormKeyMapsToJFormDB("Data/SynthEBD/BodySlideAssignments", ".SynthEBD.BodySlide", VerboseMode, "BodySlide", "str")
	endif
EndEvent

Event OnPlayerLoadGame()
	if (BodySlideScriptActive)
		VerboseLogger("SynthEBD: Player Alias HeadPart Script running OnPlayerLoadGame", VerboseMode, false)
		LoadJFormKeyMapsToJFormDB("Data/SynthEBD/BodySlideAssignments", ".SynthEBD.BodySlide", VerboseMode, "BodySlide", "str")
	endif
EndEvent