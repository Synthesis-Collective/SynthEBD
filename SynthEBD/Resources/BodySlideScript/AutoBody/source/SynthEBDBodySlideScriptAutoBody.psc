Scriptname SynthEBDBodySlideScriptAutoBody extends ActiveMagicEffect

import PSM_SynthEBD

GlobalVariable Property loadingCompleted Auto

Event OnEffectStart(Actor akTarget, Actor akCaster)
	
	while (loadingCompleted.GetValue() == 0)
		Utility.Wait(2)
	endwhile
	
	If (!JFormDB_hasPath(akCaster, ".SynthEBD.Assigned") || JFormDB_getInt(akCaster, ".SynthEBD.Assigned") == 0)
		form akBase = akCaster.GetLeveledActorBase().GetTemplate()
		string actorName = ""
		bool lockAssignment = false
		if akBase == None
			akBase = akCaster.GetActorBase().GetTemplate()
			actorName = akCaster.GetActorBase().GetName()
			if akBase == None
				akBase = akCaster.GetActorBase()
				lockAssignment = true
			endif
		else
			actorName = akCaster.GetLeveledActorBase().GetName()
		endif
		
		string assignment = JFormDB_getStr(akBase, ".SynthEBD.BodySlide")
		if actorName == ""
			actorName = "Unnamed"
		endif
		if assignment != ""
			autoBodyUtils.ApplyPresetByName(akCaster, assignment)
			;debug.Notification("Assigned bodyslide preset: " + assignment + " to NPC: " + actorName)
			;debug.Trace("Assigned bodyslide preset: " + assignment + " to NPC: " + actorName + " (" + akBase + ")")
			if lockAssignment
				JFormDB_setInt(akCaster, ".SynthEBD.Assigned", 1)
			endif
		else 
			;debug.Notification("No assignment recorded for NPC: " + actorName)
			;debug.Trace("No assignment recorded for NPC: " + actorName + " (" + akBase + ")")
		endif
    EndIf
EndEvent