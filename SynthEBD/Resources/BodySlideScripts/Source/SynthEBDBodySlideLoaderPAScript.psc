Scriptname SynthEBDBodySlideLoaderPAScript extends ReferenceAlias

GlobalVariable Property BodySlideScriptActive Auto
GlobalVariable Property VerboseMode Auto

import SynthEBDCommonFuncs

Event OnInit()
	if (BodySlideScriptActive.GetValue())
		VerboseLogger("SynthEBD: Player Alias BodySlide Script running OnInit", VerboseMode, false)
		ReloadSynthEBDDataBase("Data/SynthEBD/BodySlideAssignments.json", ".SynthEBD.BodySlides", VerboseMode, "BodySlide")
	endif
EndEvent

Event OnPlayerLoadGame()
	if (BodySlideScriptActive.GetValue())
		VerboseLogger("SynthEBD: Player Alias BodySlide Script running OnPlayerLoadGame", VerboseMode, false)
		ReloadSynthEBDDataBase("Data/SynthEBD/BodySlideAssignments.json", ".SynthEBD.BodySlides", VerboseMode, "BodySlide")
	endif
EndEvent