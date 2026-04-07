Scriptname SynthEBDTextureLoaderPAScript extends ReferenceAlias

GlobalVariable Property TextureScriptActive Auto
GlobalVariable Property VerboseMode Auto

import SynthEBDCommonFuncs

Event OnInit()
	if (TextureScriptActive.GetValue())
		VerboseLogger("SynthEBD: Player Alias Texture Script running OnInit", VerboseMode.GetValue(), false)
		ReloadSynthEBDDataBase("Data/SynthEBD/FaceTextureAssignments.json", ".SynthEBD.FaceTextures", VerboseMode.GetValue(), "FaceTexture")
		ReloadSynthEBDDataBase("Data/SynthEBD/SkinTextureAssignments.json", ".SynthEBD.SkinTextures", VerboseMode.GetValue(), "SkinTexture")
	endif
EndEvent

Event OnPlayerLoadGame()
	if (TextureScriptActive.GetValue())
		VerboseLogger("SynthEBD: Player Alias Texture Script running OnPlayerLoadGame", VerboseMode.GetValue(), false)
		ReloadSynthEBDDataBase("Data/SynthEBD/FaceTextureAssignments.json", ".SynthEBD.FaceTextures", VerboseMode.GetValue(), "FaceTexture")
		ReloadSynthEBDDataBase("Data/SynthEBD/SkinTextureAssignments.json", ".SynthEBD.SkinTextures", VerboseMode.GetValue(), "SkinTexture")
	endif
EndEvent