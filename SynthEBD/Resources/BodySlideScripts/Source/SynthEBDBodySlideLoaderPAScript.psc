Scriptname SynthEBDBodySlideLoaderPAScript extends ReferenceAlias

GlobalVariable Property BodySlideScriptActive Auto
GlobalVariable Property VerboseMode Auto

import SynthEBDCommonFuncs

Event OnInit()
	if (BodySlideScriptActive.GetValue())
		VerboseLogger("SynthEBD: Player Alias BodySlide Script running OnInit", VerboseMode.GetValue(), false)
		ReloadSynthEBDDataBase("Data/SynthEBD/BodySlideAssignments.json", ".SynthEBD.BodySlides", VerboseMode.GetValue(), "BodySlide")
	endif
EndEvent

Event OnPlayerLoadGame()
	if (BodySlideScriptActive.GetValue())
		VerboseLogger("SynthEBD: Player Alias BodySlide Script running OnPlayerLoadGame", VerboseMode.GetValue(), false)
		ReloadSynthEBDDataBase("Data/SynthEBD/BodySlideAssignments.json", ".SynthEBD.BodySlides", VerboseMode.GetValue(), "BodySlide")
	endif
EndEvent