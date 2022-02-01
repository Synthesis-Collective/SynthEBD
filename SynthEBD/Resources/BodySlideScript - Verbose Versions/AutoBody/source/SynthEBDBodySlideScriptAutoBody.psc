Scriptname SynthEBDBodySlideScriptAutoBody extends ActiveMagicEffect

import PSM_SynthEBD

GlobalVariable Property loadingCompleted Auto

Event OnEffectStart(Actor akTarget, Actor akCaster)
	
	while (loadingCompleted.GetValue() == 0)
		Utility.Wait(2)
	endwhile
	
	If (!JFormDB_hasPath(akCaster, ".SynthEBD.Assigned") || JFormDB_getInt(akCaster, ".SynthEBD.Assigned") == 0)
		string assignment = JFormDB_getStr(akCaster.GetActorBase(), ".SynthEBD.BodySlide")
		if assignment != ""
			autoBodyUtils.ApplyPresetByName(akCaster, assignment)
			JFormDB_setInt(akCaster, ".SynthEBD.Assigned", 1)
			debug.Notification("Assigned bodyslide preset: " + assignment + " to NPC: " + akCaster.GetActorBase().GetName())
		else 
			debug.Notification("No assignment recorded for NPC: " + akCaster.GetActorBase().GetName())
		endif
    EndIf
EndEvent